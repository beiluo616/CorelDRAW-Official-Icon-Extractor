using System.Diagnostics;
using System.IO;
using CDRIconExtractor.Core.Association;
using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;
using CDRIconExtractor.Windows.Automation;
using CDRIconExtractor.Windows.Detection;
using CDRIconExtractor.Windows.Resources;

namespace CDRIconExtractor.App.Services;

public sealed class ScanCoordinator
{
    private readonly UiDefinitionLocator _uiLocator;
    private readonly DrawUiParser _drawUiParser;
    private readonly CrlIconsReader _crlIconsReader;
    private readonly Win32ResourceReader _resourceReader;
    private readonly GenericPeIconScanner _genericScanner;
    private readonly IconAssociationEngine _associationEngine;
    private readonly AppLogger _logger;

    public ScanCoordinator()
        : this(
            new UiDefinitionLocator(),
            new DrawUiParser(),
            new CrlIconsReader(),
            new Win32ResourceReader(),
            new GenericPeIconScanner(),
            new IconAssociationEngine(),
            new AppLogger())
    {
    }

    public ScanCoordinator(
        UiDefinitionLocator uiLocator,
        DrawUiParser drawUiParser,
        CrlIconsReader crlIconsReader,
        Win32ResourceReader resourceReader,
        GenericPeIconScanner genericScanner,
        IconAssociationEngine associationEngine,
        AppLogger logger)
    {
        _uiLocator = uiLocator;
        _drawUiParser = drawUiParser;
        _crlIconsReader = crlIconsReader;
        _resourceReader = resourceReader;
        _genericScanner = genericScanner;
        _associationEngine = associationEngine;
        _logger = logger;
    }

    public async Task<ScanResult> ScanAsync(
        CorelInstallation installation,
        bool deepScan,
        IProgress<ScanProgress>? progress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var total = Stopwatch.StartNew();
        var commands = new List<DrawUiCommand>();
        var assets = new List<IconAsset>();
        var diagnostics = new List<ScanDiagnostic>();
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> guidMap = new Dictionary<ushort, IReadOnlyList<string>>();
        var associations = new List<IconAssociation>();
        var scannedFiles = 0;
        var crlIconsPath = installation.CrlIconsPath;
        ModernIconMapBindResult? modernMapBind = null;
        string? iconMapPath = null;

        try
        {
            Report(progress, 5, "Detect/validate paths", "正在验证 CorelDRAW 安装路径…");
            token.ThrowIfCancellationRequested();
            if (!File.Exists(installation.ProgramPath))
                throw new FileNotFoundException("CorelDRW.exe not found.", installation.ProgramPath);

            var phase = Stopwatch.StartNew();
            Report(progress, 10, "Locate DrawUI", "正在查找 DrawUI*.xml（含 DrawUI.items.xml）…");
            var drawUiFiles = _uiLocator.Locate(installation);
            phase.Stop();
            _logger.Timing("UiLocateMs", phase.ElapsedMilliseconds);
            if (drawUiFiles.Count == 0)
                diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Warning, installation.InstallRoot, "未找到 DrawUI*.xml，将以资源模式继续扫描。"));

            phase.Restart();
            Report(progress, 20, "Parse DrawUI", $"正在解析 {drawUiFiles.Count} 个 UI 定义文件…");
            foreach (var file in drawUiFiles)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var parsed = await Task.Run(() => _drawUiParser.Parse(file), token).ConfigureAwait(false);
                    commands.AddRange(parsed);
                    scannedFiles++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Warning, file, $"DrawUI 解析失败：{ex.Message}"));
                    _logger.Error($"DrawUI parse failed: {file}", ex);
                }
            }
            commands = commands
                .DistinctBy(x => $"{x.Guid}|{x.GuidRef}|{x.XmlPath}", StringComparer.OrdinalIgnoreCase)
                .ToList();
            phase.Stop();
            _logger.Timing("DrawUiParseMs", phase.ElapsedMilliseconds);

            phase.Restart();
            Report(progress, 34, "Resolve captions", "正在读取 CorelDRAW 命令名称与本地化字符串…");
            try
            {
                var stringsMapPath = LocateStringsMap(installation);
                var languageStringFiles = LocateLanguageStringFiles(installation);
                if (stringsMapPath is not null || languageStringFiles.Count > 0)
                {
                    var resolver = new CorelStringTableResolver();
                    var enriched = await Task.Run(
                        () => resolver.Enrich(commands, stringsMapPath, languageStringFiles),
                        token).ConfigureAwait(false);
                    commands = enriched.ToList();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, installation.InstallRoot, $"命令名称解析跳过：{ex.Message}"));
                _logger.Warning($"String table enrichment skipped: {ex.Message}");
            }
            phase.Stop();
            _logger.Timing("StringTableMs", phase.ElapsedMilliseconds);

            phase.Restart();
            Report(progress, 38, "Workspace shortcuts", "正在读取当前 Workspace 快捷键…");
            try
            {
                var workspaceFiles = WorkspaceLocator.Locate(installation);
                if (workspaceFiles.Count > 0)
                {
                    var shortcutBefore = commands.Count(x => !string.IsNullOrWhiteSpace(x.Shortcut));
                    var shortcutResolver = new WorkspaceShortcutResolver();
                    commands = (await Task.Run(
                        () => shortcutResolver.Enrich(commands, workspaceFiles),
                        token).ConfigureAwait(false)).ToList();
                    var shortcutAfter = commands.Count(x => !string.IsNullOrWhiteSpace(x.Shortcut));
                    diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, workspaceFiles[0], $"Workspace 快捷键：读取 {workspaceFiles.Count} 个 .cdws，新增 {Math.Max(0, shortcutAfter - shortcutBefore)} 条快捷键。"));
                }
                else
                {
                    diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, installation.InstallRoot, "未找到当前用户的 .cdws Workspace，快捷键仅使用 DrawUI 中可见数据。"));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, installation.InstallRoot, $"Workspace 快捷键解析跳过：{ex.Message}"));
            }
            phase.Stop();
            _logger.Timing("WorkspaceShortcutMs", phase.ElapsedMilliseconds);

            phase.Restart();
            Report(progress, 42, "Live CorelDRAW names", "正在检查运行中的 CorelDRAW 是否可补全真实界面名称…");
            try
            {
                var chineseBefore = CountChineseCaptions(commands);
                using var provider = CorelRunningLocalizationProvider.TryConnect(installation.VersionMajor, out var connectionDiagnostic);
                diagnostics.Add(new ScanDiagnostic(
                    ScanDiagnosticSeverity.Info,
                    installation.ProgramPath,
                    $"CorelDRAW 连接：{connectionDiagnostic.ToCompactText()}"));
                if (provider is not null)
                {
                    // GetCaptionText is the most direct source for the text users actually see in the
                    // running CorelDRAW UI. Keep the request budget bounded because 2026 can expose
                    // tens of thousands of UI entries.
                    var captions = new LiveCaptionResolver().Enrich(commands, provider, 6000, token);
                    commands = captions.Commands.ToList();

                    // LoadLocalizedString remains a secondary path for resource-string GUIDs which do
                    // not correspond to a directly addressable UI item.
                    var localized = new LiveLocalizedStringResolver().Enrich(commands, provider, 2500, token);
                    commands = localized.Commands.ToList();

                    var chineseAfter = CountChineseCaptions(commands);
                    diagnostics.Add(new ScanDiagnostic(
                        ScanDiagnosticSeverity.Info,
                        installation.ProgramPath,
                        $"CorelDRAW 实机名称补全：GetCaptionText 请求 {captions.RequestCount}、解析 {captions.ResolvedCount}；LoadLocalizedString 请求 {localized.RequestCount}、解析 {localized.ResolvedCount}；中文名称 +{Math.Max(0, chineseAfter - chineseBefore)}。"));
                }
                else
                {
                    diagnostics.Add(new ScanDiagnostic(
                        ScanDiagnosticSeverity.Info,
                        installation.ProgramPath,
                        $"未取得对应版本 CorelDRAW COM 运行实例；跳过实机名称补全。{connectionDiagnostic.ToCompactText()}"));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, installation.ProgramPath, $"CorelDRAW 实机名称补全跳过：{ex.Message}"));
            }
            phase.Stop();
            _logger.Timing("LiveLocalizationMs", phase.ElapsedMilliseconds);

            phase.Restart();
            var modernIconsPath = LocateModernCrlIcons(installation);
            if (!string.IsNullOrWhiteSpace(modernIconsPath) && File.Exists(modernIconsPath))
            {
                Report(progress, 46, "Read Modern.crlicons", "正在读取 Modern.crlicons 现代官方图标…");
                try
                {
                    var modernAssets = await Task.Run(
                        () => ModernCrlIconsReader.Read(modernIconsPath, token),
                        token).ConfigureAwait(false);

                    iconMapPath = LocateIconMapXml(installation, modernIconsPath);
                    if (!string.IsNullOrWhiteSpace(iconMapPath) && File.Exists(iconMapPath))
                    {
                        try
                        {
                            Report(progress, 47, "Read icons.map.xml", "正在读取 icons.map.xml 官方 GUID 映射…");
                            var entries = await Task.Run(() => IconMapXmlParser.Parse(iconMapPath!), token).ConfigureAwait(false);
                            modernMapBind = ModernIconMapBinder.Bind(modernAssets, entries, iconMapPath!);
                            modernAssets = modernMapBind.Assets;
                            scannedFiles++;
                            diagnostics.Add(new ScanDiagnostic(
                                ScanDiagnosticSeverity.Info,
                                iconMapPath,
                                $"icons.map.xml：{modernMapBind.TotalMapEntries} 条映射 / {modernMapBind.ReusableGuidEntries} 个标准 GUID；匹配 Modern 资源 {modernMapBind.MatchedResourceCount} 套，匹配 GUID {modernMapBind.MatchedReusableGuidEntries} 条，未匹配路径 {modernMapBind.UnmatchedResourceCount}。"));
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception mapEx)
                        {
                            diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Warning, iconMapPath, $"icons.map.xml 读取失败：{mapEx.Message}；继续以 Modern 图片浏览模式扫描。"));
                            _logger.Warning($"icons.map.xml scan skipped: {mapEx.Message}");
                        }
                    }
                    else
                    {
                        diagnostics.Add(new ScanDiagnostic(
                            ScanDiagnosticSeverity.Warning,
                            modernIconsPath,
                            "未找到 icons.map.xml；Modern 图片可浏览，但新版官方图标 GUID 无法完整建立。"));
                    }

                    AddUniqueAssets(assets, modernAssets);
                    scannedFiles++;
                    diagnostics.Add(new ScanDiagnostic(
                        ScanDiagnosticSeverity.Info,
                        modernIconsPath,
                        $"Modern.crlicons：读取 {modernAssets.Count} 套图标；24/48/72 等多尺寸已合并显示。"));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Warning, modernIconsPath, $"Modern.crlicons 读取失败：{ex.Message}"));
                    _logger.Error("Modern.crlicons scan failed", ex);
                }
            }

            phase.Restart();
            Report(progress, 48, "Read CrlIcons PNGs", "正在读取 CrlIcons.dll 官方图标…");
            if (!string.IsNullOrWhiteSpace(crlIconsPath) && File.Exists(crlIconsPath))
            {
                try
                {
                    var crlAssets = await _crlIconsReader.ReadPngAssetsAsync(crlIconsPath, token).ConfigureAwait(false);
                    assets.AddRange(crlAssets);
                    scannedFiles++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Warning, crlIconsPath, $"CrlIcons PNG 扫描失败：{ex.Message}"));
                    _logger.Error("CrlIcons PNG scan failed", ex);
                }
            }
            else
            {
                diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, installation.InstallRoot, "未找到 CrlIcons.dll，将启用 PE 资源后备扫描。"));
            }
            phase.Stop();
            _logger.Timing("CrlIconsPngScanMs", phase.ElapsedMilliseconds);

            phase.Restart();
            Report(progress, 62, "Read CrlIcons GUID map", "正在建立命令 GUID 与图标资源映射…");
            if (!string.IsNullOrWhiteSpace(crlIconsPath) && File.Exists(crlIconsPath))
            {
                try
                {
                    var blobs = await Task.Run(
                        () => _resourceReader.ReadResources(crlIconsPath, 10),
                        token).ConfigureAwait(false);
                    guidMap = CrlIconGuidMapParser.Parse(blobs.Select(x => (ReadOnlyMemory<byte>)x.Bytes));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Warning, crlIconsPath, $"GUID 映射读取失败：{ex.Message}"));
                    _logger.Error("CrlIcons GUID map failed", ex);
                }
            }
            phase.Stop();
            _logger.Timing("CrlIconsGuidMapMs", phase.ElapsedMilliseconds);

            phase.Restart();
            Report(progress, 76, "Core PE resource scan", "正在读取 CrlGenericUI.dll / CorelDRW.exe 核心图标资源…");
            foreach (var file in CoreResourceModuleLocator.LocateCoreModules(installation))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var isGenericUi = Path.GetFileName(file).Equals("CrlGenericUI.dll", StringComparison.OrdinalIgnoreCase);
                    if (isGenericUi)
                    {
                        try
                        {
                            var typeSummaries = await Task.Run(() => _resourceReader.InspectResourceTypes(file), token).ConfigureAwait(false);
                            var customTypeCount = typeSummaries.Count(x => x.TypeId is null);
                            var formattedTypes = ResourceTypeDiagnostics.Format(typeSummaries, 16);
                            var typeMessage = $"CrlGenericUI资源类型：{typeSummaries.Count} 类（自定义 {customTypeCount}）；{formattedTypes}";
                            diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, file, typeMessage));
                            _logger.Info(typeMessage);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception typeEx)
                        {
                            diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, file, $"CrlGenericUI 资源类型诊断跳过：{typeEx.Message}"));
                        }
                    }

                    var before = assets.Count;
                    var scanned = await Task.Run(() => _genericScanner.Scan(file, token), token).ConfigureAwait(false);
                    AddUniqueAssets(assets, scanned);
                    var added = assets.Count - before;

                    var mapAdded = 0;
                    if (isGenericUi)
                    {
                        try
                        {
                            var resourceBlobs = await Task.Run(() => _resourceReader.ReadResources(file, 10), token).ConfigureAwait(false);
                            var moduleMap = CrlIconGuidMapParser.Parse(resourceBlobs.Select(x => (ReadOnlyMemory<byte>)x.Bytes));
                            mapAdded = moduleMap.Sum(x => x.Value.Count);
                            guidMap = MergeGuidMaps(guidMap, moduleMap);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception mapEx)
                        {
                            _logger.Warning($"CrlGenericUI GUID map skipped: {mapEx.Message}");
                        }
                    }

                    diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, file, $"核心资源扫描：新增 {added} 个图标资源，GUID 映射记录 {mapAdded}。"));
                    scannedFiles++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, file, $"核心 PE 资源扫描跳过：{ex.Message}"));
                    _logger.Warning($"Core PE scan skipped: {Path.GetFileName(file)} | {ex.Message}");
                }
            }
            phase.Stop();
            _logger.Timing("CoreResourceScanMs", phase.ElapsedMilliseconds);

            phase.Restart();
            Report(progress, 86, "Deep PE scan", deepScan ? "正在深度扫描其他 CorelDRAW DLL 图标资源…" : "核心资源扫描完成。 ");
            if (deepScan)
            {
                foreach (var file in EnumerateDeepScanFiles(installation))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var scanned = await Task.Run(() => _genericScanner.Scan(file, token), token).ConfigureAwait(false);
                        AddUniqueAssets(assets, scanned);
                        scannedFiles++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, file, $"深度 PE 资源扫描跳过：{ex.Message}"));
                        _logger.Warning($"Deep PE scan skipped: {Path.GetFileName(file)} | {ex.Message}");
                    }
                }
            }
            phase.Stop();
            _logger.Timing("FallbackPeScanMs", phase.ElapsedMilliseconds);

            phase.Restart();
            Report(progress, 95, "Associate/search index", "正在关联命令与官方图标…");
            associations.AddRange(_associationEngine.Associate(commands, assets, guidMap));
            var modernLinked = associations.Count(x => x.Asset is not null && Path.GetFileName(x.Asset.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase));
            var modernReusableGuids = associations.Count(x => x.Asset is not null && Path.GetFileName(x.Asset.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.IconGuid));
            var mappedModernResources = assets.Count(x =>
                Path.GetFileName(x.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase) && x.IconGuids.Count > 0);
            var mappedModernGuids = assets
                .Where(x => Path.GetFileName(x.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.IconGuids)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            diagnostics.Add(new ScanDiagnostic(
                ScanDiagnosticSeverity.Info,
                iconMapPath ?? modernIconsPath,
                $"Modern 官方 GUID：有 GUID 的资源 {mappedModernResources} 套 / GUID {mappedModernGuids}；命令关联 {modernLinked} 条，其中 {modernReusableGuids} 条取得可复用图标 GUID。"));
            phase.Stop();
            _logger.Timing("AssociationMs", phase.ElapsedMilliseconds);
            _logger.Timing("SearchIndexMs", 0);

            Report(progress, 100, "Finalize", $"扫描完成：{assets.Count} 个图标资源，{associations.Count} 个命令条目。 ");
            total.Stop();
            _logger.Timing("TotalMs", total.ElapsedMilliseconds);
            return ScanResult.Completed(associations, assets, commands, diagnostics, scannedFiles, total.Elapsed);
        }
        catch (OperationCanceledException)
        {
            total.Stop();
            diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Info, null, "扫描已取消（保留已完成结果）。"));
            _logger.Timing("TotalMs", total.ElapsedMilliseconds);
            return ScanResult.Cancelled(associations, assets, commands, diagnostics, scannedFiles, total.Elapsed);
        }
        catch (Exception ex)
        {
            total.Stop();
            diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Error, installation.ProgramPath, ex.Message));
            _logger.Error("Scan failed", ex);
            return ScanResult.Completed(associations, assets, commands, diagnostics, scannedFiles, total.Elapsed);
        }
    }

    public async Task<ScanResult> ScanCrlIconsOnlyAsync(
        string crlIconsPath,
        IProgress<ScanProgress>? progress,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(crlIconsPath))
            throw new ArgumentException("CrlIcons.dll path is required.", nameof(crlIconsPath));
        if (!File.Exists(crlIconsPath))
            throw new FileNotFoundException("CrlIcons.dll not found.", crlIconsPath);

        var total = Stopwatch.StartNew();
        var assets = new List<IconAsset>();
        var diagnostics = new List<ScanDiagnostic>();
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> guidMap = new Dictionary<ushort, IReadOnlyList<string>>();
        var scannedFiles = 0;

        try
        {
            Report(progress, 10, "External CrlIcons", "正在读取外部 CrlIcons.dll 图标资源…");
            var pngAssets = await _crlIconsReader.ReadPngAssetsAsync(crlIconsPath, token).ConfigureAwait(false);
            AddUniqueAssets(assets, pngAssets);

            // Also scan standard PE resources. Some releases mix embedded PNG streams with
            // regular Windows resources in the same CrlIcons.dll.
            Report(progress, 48, "External CrlIcons PE", "正在读取外部 DLL 的标准 PE 图标资源…");
            var peAssets = await Task.Run(() => _genericScanner.Scan(crlIconsPath, token), token).ConfigureAwait(false);
            AddUniqueAssets(assets, peAssets);
            scannedFiles = 1;

            Report(progress, 70, "External CrlIcons GUID map", "正在读取外部 DLL 的 GUID → 资源ID映射…");
            try
            {
                var blobs = await Task.Run(() => _resourceReader.ReadResources(crlIconsPath, 10), token).ConfigureAwait(false);
                guidMap = CrlIconGuidMapParser.Parse(blobs.Select(x => (ReadOnlyMemory<byte>)x.Bytes));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Warning, crlIconsPath, $"外部 CrlIcons GUID 映射读取失败：{ex.Message}"));
            }

            Report(progress, 90, "Build external catalog", "正在建立外部图标资源目录…");
            var catalog = ExternalCrlIconsCatalogBuilder.Build(assets, guidMap);
            diagnostics.Add(new ScanDiagnostic(
                ScanDiagnosticSeverity.Info,
                crlIconsPath,
                $"外部 CrlIcons.dll：图标 {assets.Count}，GUID 映射 {guidMap.Sum(x => x.Value.Count)}。"));

            total.Stop();
            Report(progress, 100, "Finalize", $"外部 CrlIcons.dll 加载完成：{assets.Count} 个图标。 ");
            return ScanResult.Completed(catalog.Associations, assets, catalog.Commands, diagnostics, scannedFiles, total.Elapsed);
        }
        catch (OperationCanceledException)
        {
            total.Stop();
            return ScanResult.Cancelled(Array.Empty<IconAssociation>(), assets, Array.Empty<DrawUiCommand>(), diagnostics, scannedFiles, total.Elapsed);
        }
        catch (Exception ex)
        {
            total.Stop();
            diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Error, crlIconsPath, ex.Message));
            _logger.Error("External CrlIcons scan failed", ex);
            return ScanResult.Completed(Array.Empty<IconAssociation>(), assets, Array.Empty<DrawUiCommand>(), diagnostics, scannedFiles, total.Elapsed);
        }
    }

    public async Task<ScanResult> ScanModernCrlIconsOnlyAsync(
        string modernCrlIconsPath,
        string? iconMapPath,
        IProgress<ScanProgress>? progress,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(modernCrlIconsPath))
            throw new ArgumentException("Modern.crlicons path is required.", nameof(modernCrlIconsPath));
        if (!File.Exists(modernCrlIconsPath))
            throw new FileNotFoundException("Modern.crlicons not found.", modernCrlIconsPath);

        var total = Stopwatch.StartNew();
        var diagnostics = new List<ScanDiagnostic>();
        try
        {
            Report(progress, 10, "Modern.crlicons", "正在打开 Modern.crlicons 资源包…");
            var assets = await Task.Run(
                () => ModernCrlIconsReader.Read(modernCrlIconsPath, token),
                token).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(iconMapPath) && File.Exists(iconMapPath))
            {
                Report(progress, 55, "icons.map.xml", "正在读取新版官方 GUID 映射…");
                var entries = await Task.Run(() => IconMapXmlParser.Parse(iconMapPath!), token).ConfigureAwait(false);
                var bind = ModernIconMapBinder.Bind(assets, entries, iconMapPath!);
                assets = bind.Assets;
                diagnostics.Add(new ScanDiagnostic(
                    ScanDiagnosticSeverity.Info,
                    iconMapPath,
                    $"icons.map.xml：{bind.TotalMapEntries} 条映射 / {bind.ReusableGuidEntries} 个标准 GUID；匹配资源 {bind.MatchedResourceCount} 套 / GUID {bind.MatchedReusableGuidEntries} 条。"));
            }
            else
            {
                diagnostics.Add(new ScanDiagnostic(
                    ScanDiagnosticSeverity.Warning,
                    modernCrlIconsPath,
                    "未加载 icons.map.xml；当前仅能浏览 Modern 图片，不能完整提供新版官方 Icon GUID。"));
            }

            Report(progress, 90, "Modern.crlicons", "正在整理多尺寸图标与 GUID 目录…");
            diagnostics.Add(new ScanDiagnostic(
                ScanDiagnosticSeverity.Info,
                modernCrlIconsPath,
                $"Modern.crlicons：{assets.Count} 套图标，多尺寸资源已合并。"));
            total.Stop();
            Report(progress, 100, "Finalize", $"新版图标资源加载完成：{assets.Count} 套图标。 ");
            return ScanResult.Completed(
                Array.Empty<IconAssociation>(),
                assets,
                Array.Empty<DrawUiCommand>(),
                diagnostics,
                1,
                total.Elapsed);
        }
        catch (OperationCanceledException)
        {
            total.Stop();
            return ScanResult.Cancelled(Array.Empty<IconAssociation>(), Array.Empty<IconAsset>(), Array.Empty<DrawUiCommand>(), diagnostics, 0, total.Elapsed);
        }
        catch (Exception ex)
        {
            total.Stop();
            diagnostics.Add(new ScanDiagnostic(ScanDiagnosticSeverity.Error, modernCrlIconsPath, ex.Message));
            _logger.Error("Modern.crlicons load failed", ex);
            return ScanResult.Completed(Array.Empty<IconAssociation>(), Array.Empty<IconAsset>(), Array.Empty<DrawUiCommand>(), diagnostics, 0, total.Elapsed);
        }
    }

    public static string? LocateIconMapForModern(string modernCrlIconsPath)
    {
        if (string.IsNullOrWhiteSpace(modernCrlIconsPath))
            return null;

        var modernDirectory = Path.GetDirectoryName(Path.GetFullPath(modernCrlIconsPath));
        if (string.IsNullOrWhiteSpace(modernDirectory))
            return null;

        var candidates = new[]
        {
            Path.Combine(modernDirectory, "icons.map.xml"),
            Path.Combine(Directory.GetParent(modernDirectory)?.FullName ?? modernDirectory, "icons.map.xml")
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        var parent = Directory.GetParent(modernDirectory)?.FullName;
        return !string.IsNullOrWhiteSpace(parent)
            ? FindFileBounded(parent, "icons.map.xml", maxDepth: 3)
            : null;
    }

    private static string? LocateIconMapXml(CorelInstallation installation, string modernCrlIconsPath)
    {
        var adjacent = LocateIconMapForModern(modernCrlIconsPath);
        if (adjacent is not null)
            return adjacent;

        foreach (var candidate in new[]
        {
            Path.Combine(installation.InstallRoot, "Data", "Icons", "icons.map.xml"),
            Path.Combine(installation.InstallRoot, "Data", "Resources", "icons.map.xml")
        })
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return FindFileBounded(installation.InstallRoot, "icons.map.xml", maxDepth: 5);
    }

    private static string? LocateModernCrlIcons(CorelInstallation installation)
    {
        var candidates = new List<string>
        {
            Path.Combine(installation.InstallRoot, "Data", "Icons", "Modern.crlicons")
        };
        var programFolder = Path.GetDirectoryName(installation.ProgramPath);
        var suiteRoot = !string.IsNullOrWhiteSpace(programFolder) ? Directory.GetParent(programFolder)?.FullName : null;
        if (!string.IsNullOrWhiteSpace(suiteRoot))
            candidates.Add(Path.Combine(suiteRoot, "Data", "Icons", "Modern.crlicons"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static int CountChineseCaptions(IEnumerable<DrawUiCommand> commands) =>
        commands.Count(x => !string.IsNullOrWhiteSpace(x.LocalizedCaption) && x.LocalizedCaption.Any(ch =>
            ch is >= '\u3400' and <= '\u9FFF' || ch is >= '\uF900' and <= '\uFAFF'));

    private static string? LocateStringsMap(CorelInstallation installation)
    {
        var candidates = new List<string>();
        var programFolder = Path.GetDirectoryName(installation.ProgramPath);
        foreach (var root in new[] { programFolder, installation.InstallRoot })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            foreach (var direct in new[]
            {
                Path.Combine(root, "strings.map.xml"),
                Path.Combine(root, "UIConfig", "strings.map.xml"),
                Path.Combine(root, "Draw", "UIConfig", "strings.map.xml"),
                Path.Combine(root, "Programs64", "strings.map.xml"),
                Path.Combine(root, "Programs", "strings.map.xml")
            })
            {
                if (File.Exists(direct))
                    candidates.Add(Path.GetFullPath(direct));
            }
        }

        if (candidates.Count > 0)
            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Length).First();

        return FindFileBounded(installation.InstallRoot, "strings.map.xml", maxDepth: 5);
    }

    private static IReadOnlyList<string> LocateLanguageStringFiles(CorelInstallation installation)
    {
        // Corel has moved the program directory between Programs, Programs64 and Draw
        // across releases. The language folder normally sits at the suite root, but use
        // several bounded candidates so X4/X8 and newer suites resolve the same way.
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddRoot(string? root)
        {
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                roots.Add(Path.GetFullPath(root));
        }

        AddRoot(Path.Combine(installation.InstallRoot, "Languages"));
        var programFolder = Path.GetDirectoryName(installation.ProgramPath);
        if (!string.IsNullOrWhiteSpace(programFolder))
        {
            AddRoot(Path.Combine(programFolder, "Languages"));
            AddRoot(Path.Combine(Directory.GetParent(programFolder)?.FullName ?? programFolder, "Languages"));
        }

        if (roots.Count == 0)
            return Array.Empty<string>();

        try
        {
            var all = roots
                .SelectMany(root => Directory.EnumerateFiles(root, "strings.xml", SearchOption.AllDirectories))
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Data{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (all.Length == 0)
                return Array.Empty<string>();

            static string LanguageCode(string path)
            {
                var directory = new FileInfo(path).Directory;
                while (directory is not null)
                {
                    var parent = directory.Parent;
                    if (parent is not null && parent.Name.Equals("Languages", StringComparison.OrdinalIgnoreCase))
                        return directory.Name;
                    if (IsChinese(directory.Name) || IsEnglish(directory.Name))
                        return directory.Name;
                    directory = parent;
                }
                return string.Empty;
            }

            static bool IsChinese(string language) =>
                language.Equals("CS", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("CT", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("CHS", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("CHT", StringComparison.OrdinalIgnoreCase) ||
                language.StartsWith("ZH", StringComparison.OrdinalIgnoreCase);

            static bool IsEnglish(string language) =>
                language.StartsWith("EN", StringComparison.OrdinalIgnoreCase);

            // Never let a Chinese pack fall out because of Take(N). Load all Chinese and
            // English string tables first, then at most two other packs as a fallback.
            var preferred = all
                .Where(path => IsChinese(LanguageCode(path)) || IsEnglish(LanguageCode(path)))
                .OrderBy(path => IsChinese(LanguageCode(path)) ? 0 : 1)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            preferred.AddRange(all
                .Where(path => !preferred.Contains(path, StringComparer.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Take(2));

            return preferred.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateDeepScanFiles(CorelInstallation installation)
    {
        var seen = CoreResourceModuleLocator.LocateCoreModules(installation)
            .Append(installation.CrlIconsPath ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var programFolder = Path.GetDirectoryName(installation.ProgramPath);
        if (string.IsNullOrWhiteSpace(programFolder) || !Directory.Exists(programFolder))
            yield break;

        IEnumerable<string> dlls;
        try
        {
            dlls = Directory.EnumerateFiles(programFolder, "*.dll", SearchOption.TopDirectoryOnly)
                .Take(96)
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var file in dlls)
        {
            var full = Path.GetFullPath(file);
            if (seen.Add(full))
                yield return full;
        }
    }

    private static string? FindFileBounded(string root, string fileName, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return null;

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            try
            {
                var match = Directory.EnumerateFiles(current, fileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (match is not null)
                    return Path.GetFullPath(match);
            }
            catch { }

            if (depth >= maxDepth)
                continue;

            try
            {
                foreach (var child in Directory.EnumerateDirectories(current).Take(256))
                    queue.Enqueue((child, depth + 1));
            }
            catch { }
        }
        return null;
    }

    private static IReadOnlyDictionary<ushort, IReadOnlyList<string>> MergeGuidMaps(
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> left,
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> right)
    {
        var result = new Dictionary<ushort, HashSet<string>>();
        foreach (var source in new[] { left, right })
        {
            foreach (var pair in source)
            {
                if (!result.TryGetValue(pair.Key, out var values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[pair.Key] = values;
                }
                foreach (var value in pair.Value)
                    values.Add(value);
            }
        }

        return result.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<string>)x.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void AddUniqueAssets(ICollection<IconAsset> target, IEnumerable<IconAsset> source)
    {
        var keys = target.Select(x => $"{x.SourceFile}|{x.ResourceType}|{x.ResourceId}|{x.Sha256}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in source)
        {
            var key = $"{asset.SourceFile}|{asset.ResourceType}|{asset.ResourceId}|{asset.Sha256}";
            if (keys.Add(key))
                target.Add(asset);
        }
    }

    private static void Report(IProgress<ScanProgress>? progress, int percent, string phase, string message) =>
        progress?.Report(new ScanProgress(percent, phase, message));
}
