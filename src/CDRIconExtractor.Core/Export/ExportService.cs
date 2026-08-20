using System.Text;
using System.Text.Json;
using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Utilities;

namespace CDRIconExtractor.Core.Export;

public sealed record ExportSummary(string OutputRoot, int ExportedPngCount, int IndexedItemCount);

public sealed class ExportService
{
    private static readonly string[] CsvColumns =
    [
        "LocalizedCaption", "Caption", "Shortcut", "Guid", "GuidRef", "IconGuid", "Confidence", "Reason",
        "ResourceId", "Width", "Height", "Sha256", "SourceFile", "ExportedFile"
    ];

    public async Task<ExportSummary> ExportAsync(
        IEnumerable<IconAssociation> items,
        string preferredRoot,
        string corelVersion,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(corelVersion);
        var materialized = items.ToArray();
        token.ThrowIfCancellationRequested();

        var outputRoot = CreateOutputRoot(preferredRoot, corelVersion);
        var iconsRoot = Path.Combine(outputRoot, "Icons");
        Directory.CreateDirectory(iconsRoot);

        var records = new List<ExportIndexRecord>(materialized.Length);
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exportedAssetPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exported = 0;

        foreach (var association in materialized)
        {
            token.ThrowIfCancellationRequested();
            string? relativeExport = null;
            var asset = association.Asset;
            if (asset is not null && asset.PngBytes.Length > 0)
            {
                var assetKey = $"{asset.SourceFile}|{asset.ResourceType}|{asset.ResourceId}|{asset.Sha256}";
                if (!exportedAssetPaths.TryGetValue(assetKey, out relativeExport))
                {
                    // User-facing PNG names deliberately use the Corel resource ID only.
                    // If two different resources share the same ID, add a numeric suffix only
                    // to prevent data loss; duplicate command associations reuse the same file.
                    var resourceId = FileNameSanitizer.Sanitize(asset.ResourceId, "resource", 80);
                    var candidate = EnsureUnique($"{resourceId}.png", usedFileNames);
                    var fullPath = Path.Combine(iconsRoot, candidate);
                    await File.WriteAllBytesAsync(fullPath, asset.PngBytes, token).ConfigureAwait(false);
                    relativeExport = Path.Combine("Icons", candidate).Replace('\\', '/');
                    exportedAssetPaths[assetKey] = relativeExport;
                    exported++;
                }
            }

            records.Add(ExportIndexRecord.From(association, relativeExport));
        }

        await WriteCsvAsync(Path.Combine(outputRoot, "icon_index.csv"), records, token).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "icon_index.json"), json, new UTF8Encoding(false), token).ConfigureAwait(false);
        await WriteReportAsync(Path.Combine(outputRoot, "extraction_report.txt"), records, exported, corelVersion, token).ConfigureAwait(false);

        return new ExportSummary(outputRoot, exported, records.Count);
    }

    private static string CreateOutputRoot(string preferredRoot, string corelVersion)
    {
        var safeVersion = FileNameSanitizer.Sanitize(corelVersion, "unknown", 40);
        var preferred = string.IsNullOrWhiteSpace(preferredRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : preferredRoot;

        var primary = Path.Combine(preferred, "CDR_Icons_Output", $"CorelDRAW_{safeVersion}");
        try
        {
            return ReserveUniqueDirectory(primary);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "CDR_Icons_Output",
                $"CorelDRAW_{safeVersion}");
            return ReserveUniqueDirectory(fallback);
        }
    }

    private static string ReserveUniqueDirectory(string desired)
    {
        for (var i = 0; i < 10_000; i++)
        {
            var candidate = i == 0 ? desired : $"{desired}_{i:000}";
            if (Directory.Exists(candidate) || File.Exists(candidate))
                continue;
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        throw new IOException("Could not reserve a unique export directory.");
    }

    private static string EnsureUnique(string fileName, ISet<string> used)
    {
        if (used.Add(fileName))
            return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 10_000; i++)
        {
            var candidate = $"{stem}_{i}{ext}";
            if (used.Add(candidate))
                return candidate;
        }
        throw new IOException("Too many duplicate export file names.");
    }

    private static async Task WriteCsvAsync(string path, IReadOnlyList<ExportIndexRecord> records, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync(string.Join(',', CsvColumns)).ConfigureAwait(false);
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            string?[] values =
            [
                record.LocalizedCaption, record.Caption, record.Shortcut, record.Guid, record.GuidRef, record.IconGuid,
                record.Confidence, record.Reason, record.ResourceId, record.Width?.ToString() ?? string.Empty,
                record.Height?.ToString() ?? string.Empty, record.Sha256, record.SourceFile, record.ExportedFile
            ];
            await writer.WriteLineAsync(string.Join(',', values.Select(EscapeCsv))).ConfigureAwait(false);
        }
    }

    private static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static async Task WriteReportAsync(string path, IReadOnlyList<ExportIndexRecord> records, int exported, string version, CancellationToken token)
    {
        var exact = records.Count(x => x.Confidence == nameof(AssociationConfidence.Exact));
        var strong = records.Count(x => x.Confidence == nameof(AssociationConfidence.Strong));
        var heuristic = records.Count(x => x.Confidence == nameof(AssociationConfidence.Heuristic));
        var unmapped = records.Count(x => x.Confidence == nameof(AssociationConfidence.Unmapped));
        var iconGuidCount = records.Count(x => !string.IsNullOrWhiteSpace(x.IconGuid));
        var report = $"""
CorelDRAW官方图标提取器 - 提取报告
制作：北落果
CorelDRAW版本：{version}
生成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}

索引条目：{records.Count}
导出PNG：{exported}
Exact：{exact}
Strong：{strong}
Heuristic：{heuristic}
Unmapped：{unmapped}
可复用图标GUID：{iconGuidCount}

说明：本工具仅从用户本机已安装的 CorelDRAW 程序资源中读取并导出图标，不修改 CorelDRAW 安装文件。
""";
        await File.WriteAllTextAsync(path, report, new UTF8Encoding(false), token).ConfigureAwait(false);
    }

    private sealed record ExportIndexRecord(
        string? LocalizedCaption,
        string? Caption,
        string? Shortcut,
        string? Guid,
        string? GuidRef,
        string? IconGuid,
        string Confidence,
        string Reason,
        string? ResourceId,
        int? Width,
        int? Height,
        string? Sha256,
        string? SourceFile,
        string? ExportedFile)
    {
        public static ExportIndexRecord From(IconAssociation item, string? exportedFile) => new(
            item.Command.LocalizedCaption,
            item.Command.Caption,
            item.Command.Shortcut,
            item.Command.Guid,
            item.Command.GuidRef,
            item.IconGuid,
            item.Confidence.ToString(),
            item.Reason,
            item.Asset?.ResourceId,
            item.Asset?.Width,
            item.Asset?.Height,
            item.Asset?.Sha256,
            item.Asset?.SourceFile,
            exportedFile);
    }
}
