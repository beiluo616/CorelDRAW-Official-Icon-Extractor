using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CDRIconExtractor.App;
using CDRIconExtractor.App.Infrastructure;
using CDRIconExtractor.App.Services;
using CDRIconExtractor.Core.Export;
using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Utilities;
using CDRIconExtractor.Windows.Detection;
using CDRIconExtractor.Windows.Automation;
using Microsoft.Win32;

namespace CDRIconExtractor.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int IconWallPageSize = 300;
    private const int SearchDebounceMs = 160;

    private readonly CorelInstallDetector _detector = new();
    private readonly ScanCoordinator _scanCoordinator = new();
    private readonly ExportService _exportService = new();
    private readonly PreviewImageService _previewService = new();
    private readonly AppLogger _logger = new();
    private readonly List<IconItemViewModel> _allItems = [];
    private IReadOnlyList<IconItemViewModel> _filteredItems = Array.Empty<IconItemViewModel>();
    private IReadOnlyList<IconItemViewModel> _listItems = Array.Empty<IconItemViewModel>();
    private IReadOnlyList<IconItemViewModel> _wallItems = Array.Empty<IconItemViewModel>();
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchDebounceCts;
    private CorelInstallation? _selectedInstallation;
    private IconItemViewModel? _selectedItem;
    private string _searchText = string.Empty;
    private string _filterMode = "有图标";
    private bool _isIconWall = true;
    private int _progressValue;
    private string _statusText = "准备就绪";
    private string _diagnosticSummary = string.Empty;
    private string _scanButtonText = "扫描官方图标";
    private string? _lastOutputDirectory;
    private bool _deepScan;
    private int _currentPage = 1;
    private string _activeSourceName = "Unknown";

    public MainViewModel()
    {
        RefreshVersionsCommand = new RelayCommand(_ => RefreshVersions());
        LoadCrlIconsCommand = new AsyncRelayCommand(_ => LoadExternalCrlIconsAsync(), _ => _scanCts is null);
        LoadModernIconsCommand = new AsyncRelayCommand(_ => LoadModernCrlIconsAsync(), _ => _scanCts is null);
        ScanCommand = new RelayCommand(_parameter => { _ = ScanOrCancelAsync(); });
        SetFilterCommand = new RelayCommand(p => SetFilter(p as string));
        SetViewCommand = new RelayCommand(p => SetView(p as string));
        SetPreviewSizeCommand = new RelayCommand(p => SetPreviewSize(p));
        PreviousPageCommand = new RelayCommand(_ => ChangePage(-1), _ => IsIconWall && CurrentPage > 1);
        NextPageCommand = new RelayCommand(_ => ChangePage(1), _ => IsIconWall && CurrentPage < TotalPages);
        ExportCurrentCommand = new AsyncRelayCommand(_ => ExportCurrentAsync(), _ => SelectedItem?.Asset is not null);
        ExportBatchCommand = new AsyncRelayCommand(_ => ExportBatchAsync(), _ => _filteredItems.Any(x => x.Asset is not null));
        OpenOutputCommand = new RelayCommand(_ => OpenOutput(), _ => !string.IsNullOrWhiteSpace(_lastOutputDirectory) && Directory.Exists(_lastOutputDirectory));
        CopyPrimaryGuidCommand = new RelayCommand(_ => CopySelectedText(SelectedItem?.PrimaryGuid, "GUID"), _ => !string.IsNullOrWhiteSpace(SelectedItem?.PrimaryGuid));
        CopyCommandGuidCommand = new RelayCommand(_ => CopySelectedText(SelectedItem?.CommandGuid, "命令 GUID"), _ => !string.IsNullOrWhiteSpace(SelectedItem?.CommandGuid));
        CopyIconGuidCommand = new RelayCommand(_ => CopySelectedText(SelectedItem?.IconGuid, "图标 GUID"), _ => !string.IsNullOrWhiteSpace(SelectedItem?.IconGuid));
        CopyIconGuidUriCommand = new RelayCommand(_ => CopySelectedText(SelectedItem?.IconGuidUri, "guid:// 图标引用"), _ => !string.IsNullOrWhiteSpace(SelectedItem?.IconGuidUri));
        CopyIconAttributeCommand = new RelayCommand(_ => CopySelectedText(SelectedItem?.IconAttribute, "icon 属性"), _ => !string.IsNullOrWhiteSpace(SelectedItem?.IconAttribute));
        CopyVbaTemplateCommand = new RelayCommand(_ => CopyVbaTemplate(), _ => SelectedItem?.HasReusableIconGuid == true);
        CopyCppTemplateCommand = new RelayCommand(_ => CopyCppTemplate(), _ => SelectedItem?.HasReusableIconGuid == true);
        GenerateBatchVbaTemplateCommand = new RelayCommand(_ => GenerateBatchVbaTemplate());
        GenerateBatchCppTemplateCommand = new RelayCommand(_ => GenerateBatchCppTemplate());
        ClearMarkedCommand = new RelayCommand(_ => ClearMarked());
        ValidateIconGuidCommand = new AsyncRelayCommand(_ => ValidateSelectedIconGuidAsync(), _ => SelectedItem?.CanValidateIconGuid == true && SelectedInstallation is not null);
        RefreshVersions();
    }

    public ObservableCollection<CorelInstallation> Installations { get; } = [];

    public IReadOnlyList<IconItemViewModel> ListItems
    {
        get => _listItems;
        private set => SetProperty(ref _listItems, value);
    }

    public IReadOnlyList<IconItemViewModel> WallItems
    {
        get => _wallItems;
        private set => SetProperty(ref _wallItems, value);
    }

    public CorelInstallation? SelectedInstallation
    {
        get => _selectedInstallation;
        set
        {
            if (SetProperty(ref _selectedInstallation, value))
            {
                ValidateIconGuidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IconItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                ExportCurrentCommand.RaiseCanExecuteChanged();
                CopyPrimaryGuidCommand.RaiseCanExecuteChanged();
                CopyCommandGuidCommand.RaiseCanExecuteChanged();
                CopyIconGuidCommand.RaiseCanExecuteChanged();
                CopyIconGuidUriCommand.RaiseCanExecuteChanged();
                CopyIconAttributeCommand.RaiseCanExecuteChanged();
                CopyVbaTemplateCommand.RaiseCanExecuteChanged();
                CopyCppTemplateCommand.RaiseCanExecuteChanged();
                ValidateIconGuidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ScheduleSearchFilter();
        }
    }

    public string FilterMode
    {
        get => _filterMode;
        private set
        {
            if (!SetProperty(ref _filterMode, value))
                return;
            OnPropertyChanged(nameof(IsFilterHasIcon));
            OnPropertyChanged(nameof(IsFilterAllCommands));
            OnPropertyChanged(nameof(IsFilterPending));
            OnPropertyChanged(nameof(IsFilterUnmapped));
            OnPropertyChanged(nameof(IsFilterRawResources));
        }
    }

    public bool IsFilterHasIcon => FilterMode == "有图标";
    public bool IsFilterAllCommands => FilterMode == "全部命令";
    public bool IsFilterPending => FilterMode == "待验证";
    public bool IsFilterUnmapped => FilterMode == "未关联";
    public bool IsFilterRawResources => FilterMode == "原始资源";

    public bool IsIconWall
    {
        get => _isIconWall;
        private set
        {
            if (!SetProperty(ref _isIconWall, value))
                return;
            OnPropertyChanged(nameof(IsListView));
        }
    }

    public bool IsListView => !IsIconWall;

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(PageSummary));
                PreviousPageCommand.RaiseCanExecuteChanged();
                NextPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int TotalPages => Math.Max(1, (_filteredItems.Count + IconWallPageSize - 1) / IconWallPageSize);
    public string PageSummary => $"{CurrentPage} / {TotalPages}";

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DiagnosticSummary
    {
        get => _diagnosticSummary;
        private set => SetProperty(ref _diagnosticSummary, value);
    }

    public string ScanButtonText
    {
        get => _scanButtonText;
        private set => SetProperty(ref _scanButtonText, value);
    }

    public bool DeepScan
    {
        get => _deepScan;
        set => SetProperty(ref _deepScan, value);
    }

    public string ResultSummary => $"显示 {_filteredItems.Count} / {_allItems.Count} 项";

    public RelayCommand RefreshVersionsCommand { get; }
    public AsyncRelayCommand LoadCrlIconsCommand { get; }
    public AsyncRelayCommand LoadModernIconsCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand SetFilterCommand { get; }
    public RelayCommand SetViewCommand { get; }
    public RelayCommand SetPreviewSizeCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public AsyncRelayCommand ExportCurrentCommand { get; }
    public AsyncRelayCommand ExportBatchCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand CopyPrimaryGuidCommand { get; }
    public RelayCommand CopyCommandGuidCommand { get; }
    public RelayCommand CopyIconGuidCommand { get; }
    public RelayCommand CopyIconGuidUriCommand { get; }
    public RelayCommand CopyIconAttributeCommand { get; }
    public RelayCommand CopyVbaTemplateCommand { get; }
    public RelayCommand CopyCppTemplateCommand { get; }
    public RelayCommand GenerateBatchVbaTemplateCommand { get; }
    public RelayCommand GenerateBatchCppTemplateCommand { get; }
    public RelayCommand ClearMarkedCommand { get; }
    public AsyncRelayCommand ValidateIconGuidCommand { get; }

    private void RefreshVersions()
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var previousPath = SelectedInstallation?.ProgramPath;
            Installations.Clear();
            foreach (var item in _detector.Detect())
                Installations.Add(item);
            SelectedInstallation = Installations.FirstOrDefault(x => string.Equals(x.ProgramPath, previousPath, StringComparison.OrdinalIgnoreCase)) ?? Installations.FirstOrDefault();
            StatusText = Installations.Count == 0 ? "未自动检测到 CorelDRAW；仍可直接加载 CrlIcons.dll 或新版图标资源。" : $"检测到 {Installations.Count} 个 CorelDRAW 安装。";
        }
        finally
        {
            timer.Stop();
            _logger.Timing("InstallDetectionMs", timer.ElapsedMilliseconds);
        }
    }

    private async Task ScanOrCancelAsync()
    {
        if (_scanCts is not null)
        {
            _scanCts.Cancel();
            StatusText = "正在取消扫描…";
            return;
        }

        var installation = SelectedInstallation ?? PickInstallation();
        if (installation is null)
            return;

        _scanCts = new CancellationTokenSource();
        LoadCrlIconsCommand.RaiseCanExecuteChanged();
        LoadModernIconsCommand.RaiseCanExecuteChanged();
        ScanButtonText = "取消扫描";
        ProgressValue = 0;
        StatusText = "正在开始扫描…";
        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressValue = p.Percent;
            StatusText = p.Message;
        });

        try
        {
            var result = await _scanCoordinator.ScanAsync(installation, DeepScan, progress, _scanCts.Token);
            _activeSourceName = installation.DisplayName;
            BuildItems(result);
            ProgressValue = result.IsCancelled ? ProgressValue : 100;
            var firstError = result.Diagnostics.FirstOrDefault(x => x.Severity == ScanDiagnosticSeverity.Error);
            StatusText = result.IsCancelled
                ? "扫描已取消（保留已完成结果）"
                : firstError is not null
                    ? $"扫描失败：{firstError.Message}"
                    : BuildCompletedStatus(result);
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
            _logger.Error("UI scan command failed", ex);
        }
        finally
        {
            _scanCts.Dispose();
            _scanCts = null;
            LoadCrlIconsCommand.RaiseCanExecuteChanged();
            LoadModernIconsCommand.RaiseCanExecuteChanged();
            ScanButtonText = "扫描官方图标";
        }
    }

    private static string BuildCompletedStatus(ScanResult result)
    {
        var chinese = result.Commands.Count(x => !string.IsNullOrWhiteSpace(x.LocalizedCaption) && x.LocalizedCaption.Any(ch => ch is >= '\u3400' and <= '\u9FFF' || ch is >= '\uF900' and <= '\uFAFF'));
        var english = result.Commands.Count(x => !string.IsNullOrWhiteSpace(x.Caption));
        var shortcuts = result.Commands.Count(x => !string.IsNullOrWhiteSpace(x.Shortcut));
        var withIcon = result.Associations.Count(x => x.Asset is not null);
        var associationIconGuids = result.Associations
            .Select(x => IconGuidReference.Normalize(x.IconGuid))
            .Where(x => x is not null)
            .Cast<string>();
        var assetIconGuids = result.Assets
            .SelectMany(x => x.IconGuids)
            .Select(IconGuidReference.Normalize)
            .Where(x => x is not null)
            .Cast<string>();
        var iconGuids = associationIconGuids.Concat(assetIconGuids).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var pending = result.Associations.Count(x => x.Asset is null && !string.IsNullOrWhiteSpace(x.IconGuid));
        var warnings = result.Diagnostics.Count(x => x.Severity != ScanDiagnosticSeverity.Info);
        var crlIcons = result.Assets.Count(x => Path.GetFileName(x.SourceFile).Equals("CrlIcons.dll", StringComparison.OrdinalIgnoreCase));
        var genericUi = result.Assets.Count(x => Path.GetFileName(x.SourceFile).Equals("CrlGenericUI.dll", StringComparison.OrdinalIgnoreCase));
        var corelDrw = result.Assets.Count(x => Path.GetFileName(x.SourceFile).Equals("CorelDRW.exe", StringComparison.OrdinalIgnoreCase));
        var modern = result.Assets.Count(x => Path.GetFileName(x.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase));
        var modernLinked = result.Associations.Count(x => x.Asset is not null && Path.GetFileName(x.Asset.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase));
        var modernMappedResources = result.Assets.Count(x =>
            Path.GetFileName(x.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase) && x.IconGuids.Count > 0);
        var modernMappedGuids = result.Assets
            .Where(x => Path.GetFileName(x.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.IconGuids)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return $"扫描完成：图标 {result.Assets.Count}（Modern {modern} / CrlIcons {crlIcons} / CrlGenericUI {genericUi} / CorelDRW {corelDrw}），命令 {result.Associations.Count}，有图标 {withIcon}，Modern有GUID {modernMappedResources}套 / 映射GUID {modernMappedGuids}，命令关联 {modernLinked}，待验证 {pending}，图标GUID {iconGuids}，中文名称 {chinese}，英文名称 {english}，快捷键 {shortcuts}，{warnings} 条提示。";
    }

    private async Task LoadExternalCrlIconsAsync()
    {
        if (_scanCts is not null)
            return;

        var lastPath = ReadLastExternalCrlIconsPath();
        var dialog = new OpenFileDialog
        {
            Title = "加载 CrlIcons.dll 资源文件",
            Filter = "CorelDRAW 图标资源 (CrlIcons.dll)|CrlIcons.dll|DLL 文件 (*.dll)|*.dll",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = !string.IsNullOrWhiteSpace(lastPath) && Directory.Exists(Path.GetDirectoryName(lastPath))
                ? Path.GetDirectoryName(lastPath) ?? string.Empty
                : string.Empty
        };
        if (dialog.ShowDialog() != true)
            return;

        _scanCts = new CancellationTokenSource();
        LoadCrlIconsCommand.RaiseCanExecuteChanged();
        LoadModernIconsCommand.RaiseCanExecuteChanged();
        ProgressValue = 0;
        StatusText = "正在加载外部 CrlIcons.dll…";
        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressValue = p.Percent;
            StatusText = p.Message;
        });

        try
        {
            var result = await _scanCoordinator.ScanCrlIconsOnlyAsync(dialog.FileName, progress, _scanCts.Token);
            var firstError = result.Diagnostics.FirstOrDefault(x => x.Severity == ScanDiagnosticSeverity.Error);
            if (firstError is not null)
            {
                StatusText = $"加载失败：{firstError.Message}";
                return;
            }

            SaveLastExternalCrlIconsPath(dialog.FileName);
            _activeSourceName = $"External-{Path.GetFileName(Path.GetDirectoryName(dialog.FileName)) ?? "CrlIcons"}";
            FilterMode = "有图标";
            BuildItems(result);
            ProgressValue = 100;
            var mapped = result.Associations.Count(x => !string.IsNullOrWhiteSpace(x.IconGuid));
            StatusText = $"已加载外部 CrlIcons.dll：图标 {result.Assets.Count}，可用 GUID {mapped}。无需安装对应 CorelDRAW。";
        }
        catch (Exception ex)
        {
            StatusText = $"加载 CrlIcons.dll 失败：{ex.Message}";
            _logger.Error("Load external CrlIcons failed", ex);
        }
        finally
        {
            _scanCts.Dispose();
            _scanCts = null;
            LoadCrlIconsCommand.RaiseCanExecuteChanged();
            LoadModernIconsCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task LoadModernCrlIconsAsync()
    {
        if (_scanCts is not null)
            return;

        var lastPath = ReadLastModernCrlIconsPath();
        var dialog = new OpenFileDialog
        {
            Title = "加载新版图标资源：选择 Modern.crlicons",
            Filter = "CorelDRAW 新版图标资源 (Modern.crlicons)|Modern.crlicons|CRLICONS 文件 (*.crlicons)|*.crlicons",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = !string.IsNullOrWhiteSpace(lastPath) && Directory.Exists(Path.GetDirectoryName(lastPath))
                ? Path.GetDirectoryName(lastPath) ?? string.Empty
                : string.Empty
        };
        if (dialog.ShowDialog() != true)
            return;

        _scanCts = new CancellationTokenSource();
        LoadCrlIconsCommand.RaiseCanExecuteChanged();
        LoadModernIconsCommand.RaiseCanExecuteChanged();
        ProgressValue = 0;
        StatusText = "正在加载新版图标资源…";
        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressValue = p.Percent;
            StatusText = p.Message;
        });

        try
        {
            var iconMapPath = ScanCoordinator.LocateIconMapForModern(dialog.FileName);
            if (iconMapPath is null)
            {
                var mapDialog = new OpenFileDialog
                {
                    Title = "选择 icons.map.xml（新版官方 GUID 映射）",
                    Filter = "CorelDRAW 图标映射 (icons.map.xml)|icons.map.xml|XML 文件 (*.xml)|*.xml",
                    CheckFileExists = true,
                    Multiselect = false,
                    InitialDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty
                };
                if (mapDialog.ShowDialog() != true)
                {
                    StatusText = "已取消加载：新版图标资源需要 Modern.crlicons + icons.map.xml。";
                    return;
                }
                iconMapPath = mapDialog.FileName;
            }

            var result = await _scanCoordinator.ScanModernCrlIconsOnlyAsync(dialog.FileName, iconMapPath!, progress, _scanCts.Token);
            var firstError = result.Diagnostics.FirstOrDefault(x => x.Severity == ScanDiagnosticSeverity.Error);
            if (firstError is not null)
            {
                StatusText = $"加载失败：{firstError.Message}";
                return;
            }

            SaveLastModernCrlIconsPath(dialog.FileName);
            _activeSourceName = "Modern.crlicons + icons.map.xml";
            FilterMode = "有图标";
            BuildItems(result);
            ProgressValue = 100;
            var multiSize = result.Assets.Count(x => x.Variants.Count > 1);
            var mappedResources = result.Assets.Count(x => x.IconGuids.Count > 0);
            var mappedGuids = result.Assets.SelectMany(x => x.IconGuids).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            StatusText = $"已加载新版图标资源：{result.Assets.Count} 套图标，其中 {multiSize} 套包含多尺寸；有 GUID 的资源 {mappedResources} 套，官方映射 GUID {mappedGuids} 个。";
        }
        catch (Exception ex)
        {
            StatusText = $"加载新版图标资源失败：{ex.Message}";
            _logger.Error("Load modern icon resources failed", ex);
        }
        finally
        {
            _scanCts.Dispose();
            _scanCts = null;
            LoadCrlIconsCommand.RaiseCanExecuteChanged();
            LoadModernIconsCommand.RaiseCanExecuteChanged();
        }
    }

    private static string ModernCrlIconsSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Beiluoguo",
        "CDRIconExtractor",
        "last-modern-crlicons.txt");

    private static string? ReadLastModernCrlIconsPath()
    {
        try
        {
            var file = ModernCrlIconsSettingsPath;
            if (!File.Exists(file))
                return null;
            var path = File.ReadAllText(file).Trim();
            return File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    private static void SaveLastModernCrlIconsPath(string path)
    {
        try
        {
            var file = ModernCrlIconsSettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Path.GetFullPath(path));
        }
        catch { }
    }

    private static string ExternalCrlIconsSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Beiluoguo",
        "CDRIconExtractor",
        "last-crlicons.txt");

    private static string? ReadLastExternalCrlIconsPath()
    {
        try
        {
            var file = ExternalCrlIconsSettingsPath;
            if (!File.Exists(file))
                return null;
            var path = File.ReadAllText(file).Trim();
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveLastExternalCrlIconsPath(string path)
    {
        try
        {
            var file = ExternalCrlIconsSettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Path.GetFullPath(path));
        }
        catch
        {
            // Remembering the path is a convenience only; loading must still succeed.
        }
    }

    private CorelInstallation? PickInstallation()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 CorelDRAW 主程序 CorelDRW.exe",
            Filter = "CorelDRAW 主程序 (CorelDRW.exe)|CorelDRW.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return null;
        if (!_detector.TryCreateInstallation(dialog.FileName, out var installation) || installation is null)
        {
            MessageBox.Show("所选文件不是有效的 CorelDRW.exe 安装位置。", "CorelDRAW官方图标提取器", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        var existing = Installations.FirstOrDefault(x => string.Equals(x.ProgramPath, installation.ProgramPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            Installations.Add(installation);
        SelectedInstallation = existing ?? installation;
        StatusText = $"已选择：{SelectedInstallation.DisplayName}";
        return SelectedInstallation;
    }

    private void BuildItems(ScanResult result)
    {
        _allItems.Clear();
        DiagnosticSummary = result.Diagnostics
            .Select(x => x.Message)
            .FirstOrDefault(x => x.StartsWith("CrlGenericUI资源类型：", StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
        foreach (var association in result.Associations.Where(x => !IsResourceDefinition(x.Command)))
            _allItems.Add(new IconItemViewModel(association, association.Asset, _previewService));

        // Keep raw resources as a separate diagnostic/browser view. They are intentionally
        // duplicated from associated commands only inside the "原始资源" filter.
        foreach (var asset in result.Assets)
            _allItems.Add(new IconItemViewModel(null, asset, _previewService));

        ApplyFilter();
    }

    private void SetFilter(string? mode)
    {
        if (mode is not ("有图标" or "全部命令" or "待验证" or "未关联" or "原始资源"))
            return;
        FilterMode = mode;
        CancelSearchDebounce();
        ApplyFilter();
    }

    private void SetView(string? mode)
    {
        IsIconWall = string.Equals(mode, "wall", StringComparison.OrdinalIgnoreCase);
        CurrentPage = 1;
        UpdateVisibleItems();
    }

    private void SetPreviewSize(object? parameter)
    {
        if (SelectedItem is null)
            return;
        if (parameter is null || !int.TryParse(parameter.ToString(), out var size))
        {
            SelectedItem.SetPreferredPreviewSize(null);
            return;
        }
        SelectedItem.SetPreferredPreviewSize(size);
    }

    private void ScheduleSearchFilter()
    {
        CancelSearchDebounce();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = ApplyFilterDebouncedAsync(cts);
    }

    private async Task ApplyFilterDebouncedAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, cts.Token);
            if (!cts.IsCancellationRequested)
                ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // Expected when the user keeps typing.
        }
        finally
        {
            if (ReferenceEquals(_searchDebounceCts, cts))
            {
                _searchDebounceCts = null;
                cts.Dispose();
            }
        }
    }

    private void CancelSearchDebounce()
    {
        var cts = _searchDebounceCts;
        _searchDebounceCts = null;
        if (cts is null)
            return;
        cts.Cancel();
        cts.Dispose();
    }

    private void ApplyFilter()
    {
        IEnumerable<IconItemViewModel> query = _allItems;
        query = FilterMode switch
        {
            "有图标" => query.Where(x => x.HasConfirmedPreview),
            "全部命令" => query.Where(x => x.Association is not null),
            "待验证" => query.Where(x => x.IsPendingIconPreview),
            "未关联" => query.Where(x => x.IsCommandUnmapped),
            "原始资源" => query.Where(x => x.Association is null && x.Asset is not null),
            _ => query.Where(x => x.Association?.Asset is not null)
        };
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(x => x.Matches(SearchText.Trim()));

        _filteredItems = query.ToArray();
        CurrentPage = 1;
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageSummary));
        UpdateVisibleItems();
    }

    private void ChangePage(int delta)
    {
        if (!IsIconWall)
            return;
        var next = Math.Clamp(CurrentPage + delta, 1, TotalPages);
        if (next == CurrentPage)
            return;
        CurrentPage = next;
        UpdateVisibleItems();
    }

    private void UpdateVisibleItems()
    {
        var selectedKey = SelectedItem is null ? null : ItemKey(SelectedItem);
        IReadOnlyList<IconItemViewModel> activeItems;

        if (IsIconWall)
        {
            var skip = (CurrentPage - 1) * IconWallPageSize;
            WallItems = _filteredItems.Skip(skip).Take(IconWallPageSize).ToArray();
            ListItems = Array.Empty<IconItemViewModel>();
            activeItems = WallItems;
        }
        else
        {
            ListItems = _filteredItems;
            WallItems = Array.Empty<IconItemViewModel>();
            activeItems = ListItems;
        }

        SelectedItem = selectedKey is null
            ? activeItems.FirstOrDefault()
            : activeItems.FirstOrDefault(x => ItemKey(x) == selectedKey) ?? activeItems.FirstOrDefault();

        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageSummary));
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
        ExportBatchCommand.RaiseCanExecuteChanged();
    }

    private async Task ExportCurrentAsync()
    {
        var association = SelectedItem?.ToExportAssociation();
        if (association is null)
            return;
        await ExportAsync(new[] { association });
    }

    private async Task ExportBatchAsync()
    {
        var items = _filteredItems.Select(x => x.ToExportAssociation()).Where(x => x is not null).Cast<IconAssociation>().ToArray();
        if (items.Length == 0)
            return;
        await ExportAsync(items);
    }

    private async Task ExportAsync(IReadOnlyList<IconAssociation> associations)
    {
        var version = _activeSourceName;
        var preferred = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        try
        {
            var summary = await _exportService.ExportAsync(associations, preferred, version, CancellationToken.None);
            _lastOutputDirectory = summary.OutputRoot;
            StatusText = $"已导出 {summary.ExportedPngCount} 个 PNG：{summary.OutputRoot}";
            OpenOutputCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败：{ex.Message}";
            _logger.Error("Export failed", ex);
        }
    }

    private void CopySelectedText(string? value, string label, bool includeValueInStatus = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusText = $"当前条目没有可复制的{label}。";
            return;
        }

        try
        {
            Clipboard.SetText(value);
            StatusText = includeValueInStatus ? $"已复制{label}：{value}" : $"已复制{label}到剪贴板。";
        }
        catch (Exception ex)
        {
            StatusText = $"复制失败：{ex.Message}";
            _logger.Error($"Copy {label} failed", ex);
        }
    }

    private void CopyVbaTemplate()
    {
        var item = SelectedItem;
        if (item is null || string.IsNullOrWhiteSpace(item.IconGuid))
            return;
        var caption = item.LocalizedCaption == "未解析名称" ? "我的功能" : item.LocalizedCaption;
        var template = IconRegistrationTemplateGenerator.GenerateVba(
            item.IconGuid,
            caption: caption,
            resourcePath: item.ResourcePath,
            guidSource: BuildGuidSourceLabel(item));
        ShowCodeTemplate("VBA 图标注册模板", template, ".bas", "CorelDRAW_Icon_Register.bas");
    }

    private void CopyCppTemplate()
    {
        var item = SelectedItem;
        if (item is null || string.IsNullOrWhiteSpace(item.IconGuid))
            return;
        var caption = item.LocalizedCaption == "未解析名称" ? "我的功能" : item.LocalizedCaption;
        var template = IconRegistrationTemplateGenerator.GenerateCpp(
            item.IconGuid,
            caption: caption,
            resourcePath: item.ResourcePath,
            guidSource: BuildGuidSourceLabel(item));
        ShowCodeTemplate("C++ / CPG 图标注册模板", template, ".cpp", "CorelDRAW_Icon_Register.cpp");
    }

    private async Task ValidateSelectedIconGuidAsync()
    {
        var item = SelectedItem;
        var installation = SelectedInstallation;
        if (item is null || installation is null || !item.CanValidateIconGuid)
            return;

        StatusText = "正在连接对应版本 CorelDRAW 验证图标 GUID…";
        await Task.Yield();
        try
        {
            using var validator = CorelRunningIconValidator.TryConnect(installation.VersionMajor, out var connectionDiagnostic);
            if (validator is null)
            {
                var message = $"未取得对应版本 CorelDRAW COM 运行实例。{connectionDiagnostic.ToCompactText()} 请确认已打开对应版本，并尽量让 CorelDRAW 与本工具使用相同权限级别。";
                item.ApplyIconGuidValidation(false, message);
                StatusText = message;
                return;
            }

            var result = await Task.Run(() => validator.Validate(item.IconGuid));
            item.ApplyIconGuidValidation(result.Accepted, result.Message);
            StatusText = result.Accepted
                ? "图标 GUID 已通过 CorelDRAW SetIcon2 实机调用验证；当前仍没有本地预览图。"
                : result.Message;
        }
        catch (Exception ex)
        {
            item.ApplyIconGuidValidation(false, ex.Message);
            StatusText = $"图标 GUID 实机验证失败：{ex.Message}";
            _logger.Error("Live icon GUID validation failed", ex);
        }
    }

    private static string BuildGuidSourceLabel(IconItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.IconGuidSource))
            return $"{Path.GetFileName(item.IconGuidSource)} + {item.SourceFileName}";
        return item.SourceFileName;
    }

    private void GenerateBatchVbaTemplate()
    {
        var items = GetMarkedTemplateItems(isVba: true);
        if (items.Count == 0)
        {
            StatusText = "请先勾选至少一个具有图标 GUID 的图标。";
            return;
        }
        var template = IconRegistrationTemplateGenerator.GenerateVbaBatch(items);
        ShowCodeTemplate($"批量 VBA 图标注册模板（{items.Count}项）", template, ".bas", "CorelDRAW_Icons_Batch_Register.bas");
    }

    private void GenerateBatchCppTemplate()
    {
        var items = GetMarkedTemplateItems(isVba: false);
        if (items.Count == 0)
        {
            StatusText = "请先勾选至少一个具有图标 GUID 的图标。";
            return;
        }
        var template = IconRegistrationTemplateGenerator.GenerateCppBatch(items);
        ShowCodeTemplate($"批量 C++ / CPG 图标注册模板（{items.Count}项）", template, ".cpp", "CorelDRAW_Icons_Batch_Register.cpp");
    }

    private IReadOnlyList<IconRegistrationTemplateItem> GetMarkedTemplateItems(bool isVba)
    {
        var marked = _allItems.Where(x => x.IsMarked && x.HasReusableIconGuid).ToArray();
        var result = new List<IconRegistrationTemplateItem>(marked.Length);
        for (var i = 0; i < marked.Length; i++)
        {
            var item = marked[i];
            var n = i + 1;
            var caption = item.LocalizedCaption == "未解析名称" || string.IsNullOrWhiteSpace(item.LocalizedCaption)
                ? $"我的功能{n}"
                : item.LocalizedCaption;
            var command = isVba ? $"MyMacro.MyModule.Command{n}" : $"MyCommand{n}";
            result.Add(new IconRegistrationTemplateItem(item.IconGuid, command, caption));
        }
        return result;
    }

    private void ClearMarked()
    {
        foreach (var item in _allItems)
            item.IsMarked = false;
        StatusText = "已清除图标勾选。";
    }

    private void ShowCodeTemplate(string title, string code, string extension, string fileName)
    {
        try
        {
            var window = new CodeTemplateWindow(title, code, extension, fileName)
            {
                Owner = Application.Current?.MainWindow
            };
            _ = window.ShowDialog();
            StatusText = $"已生成{title}，可在窗口中复制或另存。";
        }
        catch (Exception ex)
        {
            StatusText = $"打开代码模板失败：{ex.Message}";
            _logger.Error("Open code template failed", ex);
        }
    }

    private void OpenOutput()
    {
        var outputDirectory = _lastOutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            return;
        Process.Start(new ProcessStartInfo(outputDirectory) { UseShellExecute = true });
    }

    private static bool IsResourceDefinition(DrawUiCommand command) =>
        command.ElementName.Equals("resEntry", StringComparison.OrdinalIgnoreCase) ||
        command.ElementName.Equals("resourceEntry", StringComparison.OrdinalIgnoreCase);

    private static string AssetKey(IconAsset asset) => $"{asset.SourceFile}|{asset.ResourceType}|{asset.ResourceId}|{asset.Sha256}";
    private static string ItemKey(IconItemViewModel item) => $"{item.Guid}|{item.ResourceId}|{item.SourceFile}|{item.LocalizedCaption}";

    public void Dispose()
    {
        CancelSearchDebounce();
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }
}
