using System.Windows.Media.Imaging;
using CDRIconExtractor.App.Infrastructure;
using CDRIconExtractor.App.Services;
using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Search;
using CDRIconExtractor.Core.Utilities;

namespace CDRIconExtractor.App.ViewModels;

public sealed class IconItemViewModel : ObservableObject
{
    private readonly PreviewImageService _previewService;
    private BitmapSource? _preview;
    private bool _previewLoaded;
    private readonly string _searchDocument;
    private readonly IconGuidPresentation _guidPresentation;
    private bool _isMarked;
    private bool? _iconGuidApiAccepted;
    private string _iconValidationMessage = string.Empty;
    private int? _preferredPreviewSize;

    public IconItemViewModel(IconAssociation? association, IconAsset? asset, PreviewImageService previewService)
    {
        Association = association;
        Asset = asset ?? association?.Asset;
        _previewService = previewService;
        _guidPresentation = IconGuidPresentation.Create(CommandGuid, IconGuid);
        _searchDocument = TextSearchMatcher.CreateSearchDocument(
        [
            LocalizedCaption, Caption, Shortcut, Guid, GuidRef, CommandGuid, IconGuid, PrimaryGuid, IconGuidUri, IconAttribute,
            AllIconGuidsText, GuidRelation, Confidence, Reason, SourceFile, Path.GetFileName(SourceFile), ResourceId, OriginalSize, AvailableSizes,
            Asset?.DisplayName, Asset?.ResourcePath, Asset?.IconGuidSource, Sha256, Command?.ElementName, Command?.XmlPath
        ]);
    }

    public IconAssociation? Association { get; }
    public IconAsset? Asset { get; }
    public DrawUiCommand? Command => Association?.Command;

    public string LocalizedCaption
    {
        get
        {
            if (Command is not null)
                return FirstReadable(Command.LocalizedCaption, Command.Caption) ?? "未解析名称";
            if (Asset is null)
                return "未命名";
            return !string.IsNullOrWhiteSpace(Asset.DisplayName) ? Asset.DisplayName! : $"图标资源 {Asset.ResourceId}";
        }
    }

    public string Caption => Readable(Command?.Caption) ?? string.Empty;
    public string Shortcut => Command?.Shortcut ?? string.Empty;
    public string Guid => Command?.Guid ?? string.Empty;
    public string GuidRef => Command?.GuidRef ?? string.Empty;
    public string CommandGuid => IconGuidReference.Normalize(Command?.Guid) ?? IconGuidReference.Normalize(Command?.GuidRef) ?? string.Empty;
    public IReadOnlyList<string> AvailableIconGuids => BuildAvailableIconGuids();
    public string IconGuid => IconGuidReference.Normalize(Association?.IconGuid) ?? AvailableIconGuids.FirstOrDefault() ?? string.Empty;
    public int IconGuidCount => AvailableIconGuids.Count;
    public bool HasMultipleIconGuids => IconGuidCount > 1;
    public string AllIconGuidsText => string.Join(Environment.NewLine, AvailableIconGuids);
    public string OtherIconGuidsText => string.Join(Environment.NewLine, AvailableIconGuids.Where(x => !x.Equals(IconGuid, StringComparison.OrdinalIgnoreCase)));
    public string IconGuidSource => Asset?.IconGuidSource ?? string.Empty;
    public string PrimaryGuid => _guidPresentation.PrimaryGuid;
    public string PrimaryGuidLabel => _guidPresentation.PrimaryLabel;
    public bool ShowCombinedGuid => _guidPresentation.ShowCombined;
    public bool ShowSeparateGuids => _guidPresentation.ShowSeparate;
    public bool HasReusableIconGuid => !string.IsNullOrWhiteSpace(IconGuid);
    public string GuidRelation => ShowSeparateGuids
        ? "命令与图标使用不同 GUID"
        : CommandGuid.Length > 0 && IconGuid.Length > 0
            ? "命令/图标共用 GUID"
            : IconGuid.Length > 0
                ? "仅识别到图标 GUID"
                : CommandGuid.Length > 0
                    ? "仅识别到命令 GUID"
                    : "未识别 GUID";
    public string IconGuidUri => IconGuidReference.FormatUri(IconGuid) ?? string.Empty;
    public string IconAttribute => IconGuidReference.FormatIconAttribute(IconGuid) ?? string.Empty;
    public string Confidence => Association?.Confidence.ToString() ?? "Resource";
    public string Reason => Association?.Reason ?? "独立图标资源";
    public string SourceFile => Asset?.SourceFile ?? string.Empty;
    public string SourceFileName => string.IsNullOrWhiteSpace(SourceFile) ? string.Empty : Path.GetFileName(SourceFile);
    public string ResourceId => Asset?.ResourceId ?? Association?.ResourceIdHint ?? string.Empty;
    public string OriginalSize => Asset is null ? string.Empty : $"{Asset.Width} × {Asset.Height}";
    public string ResourcePath => Asset?.ResourcePath ?? string.Empty;
    public string AvailableSizes => Asset is null || Asset.Variants.Count == 0
        ? OriginalSize
        : string.Join(" / ", Asset.Variants.Select(x => x.Width).Distinct().OrderBy(x => x));
    public bool HasMultipleSizes => Asset?.Variants.Select(x => x.Width).Distinct().Skip(1).Any() == true;
    public bool Has24 => Asset?.Variants.Any(x => x.Width == 24) == true;
    public bool Has48 => Asset?.Variants.Any(x => x.Width == 48) == true;
    public bool Has72 => Asset?.Variants.Any(x => x.Width == 72) == true;
    public string Sha256 => Asset?.Sha256 ?? string.Empty;
    public bool HasAsset => Asset is not null;
    public bool HasConfirmedPreview => Asset is not null;
    public bool IsPendingIconPreview => Association is not null && Asset is null && HasReusableIconGuid;
    public bool IsCommandUnmapped => Association is not null && Asset is null && !HasReusableIconGuid;
    public bool CanValidateIconGuid => Association is not null && Asset is null && HasReusableIconGuid;
    public string IconValidationStatus => Asset is not null
        ? "✓ 已取得本地图标预览"
        : !HasReusableIconGuid
            ? "未识别可复用图标 GUID"
            : _iconGuidApiAccepted is true
                ? "✓ CorelDRAW SetIcon2 已接受此 GUID（暂无本地预览）"
                : _iconGuidApiAccepted is false
                    ? "× CorelDRAW 实机验证失败"
                    : "待验证：已识别图标 GUID，但暂无本地预览";
    public string IconValidationMessage => _iconValidationMessage;

    public void ApplyIconGuidValidation(bool accepted, string message)
    {
        _iconGuidApiAccepted = accepted;
        _iconValidationMessage = message ?? string.Empty;
        OnPropertyChanged(nameof(IconValidationStatus));
        OnPropertyChanged(nameof(IconValidationMessage));
    }

    public bool IsMarked
    {
        get => _isMarked;
        set => SetProperty(ref _isMarked, value);
    }

    public BitmapSource? Preview
    {
        get
        {
            if (!_previewLoaded)
            {
                _previewLoaded = true;
                _preview = _previewService.Get(Asset, _preferredPreviewSize);
            }
            return _preview;
        }
    }


    public void SetPreferredPreviewSize(int? size)
    {
        if (size is not null && Asset?.Variants.Any(x => x.Width == size.Value) != true)
            return;
        _preferredPreviewSize = size;
        _previewLoaded = false;
        _preview = null;
        OnPropertyChanged(nameof(Preview));
    }

    public bool Matches(string query) => TextSearchMatcher.MatchesDocument(_searchDocument, query);

    public IconAssociation? ToExportAssociation()
    {
        if (Association is not null)
            return Association;
        if (Asset is null)
            return null;
        var title = !string.IsNullOrWhiteSpace(Asset.DisplayName) ? Asset.DisplayName! : $"图标资源 {Asset.ResourceId}";
        var pseudo = new DrawUiCommand(null, null, title, title, null, "resource", Array.Empty<ResourceHint>(), string.Empty);
        return new IconAssociation(
            pseudo,
            Asset,
            AssociationConfidence.Unmapped,
            Asset.IconGuids.Count > 0 ? "Standalone icon resource with official icons.map.xml GUID" : "Standalone icon resource",
            string.IsNullOrWhiteSpace(IconGuid) ? null : IconGuid,
            Asset.ResourceId);
    }

    private IReadOnlyList<string> BuildAvailableIconGuids()
    {
        var result = new List<string>();
        var associated = IconGuidReference.Normalize(Association?.IconGuid);
        if (associated is not null)
            result.Add(associated);

        if (Asset is not null)
        {
            foreach (var raw in Asset.IconGuids)
            {
                var normalized = IconGuidReference.Normalize(raw);
                if (normalized is not null && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    result.Add(normalized);
            }
        }
        return result;
    }

    private static string? FirstReadable(params string?[] values) =>
        values.Select(Readable).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string? Readable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.StartsWith("*CT(", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("*TT(", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("*ST(", StringComparison.OrdinalIgnoreCase))
            return null;
        return trimmed;
    }
}
