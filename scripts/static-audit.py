from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []


def require(condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)


for project in ROOT.glob('**/*.csproj'):
    try:
        ET.parse(project)
    except ET.ParseError as exc:
        errors.append(f'Invalid XML in {project.relative_to(ROOT)}: {exc}')

for xaml in ROOT.glob('src/**/*.xaml'):
    try:
        ET.parse(xaml)
    except ET.ParseError as exc:
        errors.append(f'Invalid XAML/XML in {xaml.relative_to(ROOT)}: {exc}')

app_root = ROOT / 'src/CDRIconExtractor.App'
app_text = '\n'.join(p.read_text(encoding='utf-8', errors='ignore') for p in app_root.rglob('*') if p.is_file())
require('CorelDRAW官方图标提取器' in app_text, 'App UI is missing product name: CorelDRAW官方图标提取器')
require('By北落果' in app_text, 'App UI is missing branding text: By北落果')
require('联系QQ:517679191' in app_text, 'About window is missing QQ contact')
require('北落果制作' not in app_text, 'Legacy duplicated branding text remains: 北落果制作')

main_xaml = (app_root / 'MainWindow.xaml').read_text(encoding='utf-8')
require('Value="{Binding ProgressValue, Mode=OneWay}"' in main_xaml, 'ProgressBar.ProgressValue binding must stay Mode=OneWay')
require('中文智能搜索：转曲、焊接、解组、二维码、Convert to Curves、Ctrl+Q、GUID、资源ID、文件名等' in main_xaml, 'Search watermark text is missing')
require('CornerRadius="7"' in main_xaml and 'x:Name="SearchBox"' in main_xaml, 'Rounded search box markup is missing')
require('PreviousPageCommand' in main_xaml and 'NextPageCommand' in main_xaml, 'Icon wall pagination UI is missing')
require('EnableRowVirtualization="True"' in main_xaml and 'VirtualizationMode="Recycling"' in main_xaml, 'List virtualization settings are missing')

require('RadioButton Content="有图标"' in main_xaml and 'IsChecked="{Binding IsFilterHasIcon, Mode=OneWay}"' in main_xaml, 'Fix7 persistent icon filter selection is missing')
require('RadioButton Content="图标墙"' in main_xaml and 'IsChecked="{Binding IsIconWall, Mode=OneWay}"' in main_xaml, 'Fix7 persistent icon-wall selection is missing')
require(main_xaml.find('Content="图标墙"') < main_xaml.find('Content="列表"'), '图标墙 must appear before 列表')
require('SegmentRadioStyle' in main_xaml, 'Fix7 persistent selected-state style is not applied')
app_xaml = (app_root / 'App.xaml').read_text(encoding='utf-8')
require('<Trigger Property="IsChecked" Value="True">' in app_xaml, 'Fix7 selected state must be driven by IsChecked')
require('#1D4ED8' in app_xaml and 'Foreground" Value="White' in app_xaml, 'Fix7 selected state must stay visibly dark')

app_cs = (app_root / 'App.xaml.cs').read_text(encoding='utf-8')
require('SingleInstanceMutexName' in app_cs and 'new Mutex' in app_cs, 'Single-instance mutex protection is missing')

project_text = (app_root / 'CDRIconExtractor.App.csproj').read_text(encoding='utf-8')
require('<ApplicationIcon>Assets\\f10ai.ico</ApplicationIcon>' in project_text, 'F10AI EXE icon is not configured')
require((app_root / 'Assets/f10ai.ico').exists(), 'F10AI icon file is missing')

main_vm = (app_root / 'ViewModels/MainViewModel.cs').read_text(encoding='utf-8')
require('SearchDebounceMs' in main_vm, 'Search debounce is missing')
require('IconWallPageSize = 300' in main_vm, 'Icon wall page size must be 300')
require('VisibleItems.Clear()' not in main_vm, 'Per-item VisibleItems Clear/Add refresh path still exists')
require('VisibleItems.Add(' not in main_vm, 'Per-item VisibleItems Add refresh path still exists')

require('private string _filterMode = "有图标";' in main_vm, 'Fix6 default filter must be 有图标')
require('private bool _isIconWall = true;' in main_vm, 'Fix6 default view must be 图标墙')
require('中文名称' in main_vm and '有图标' in main_vm, 'Fix6 scan summary diagnostics are missing')

association = (ROOT / 'src/CDRIconExtractor.Core/Association/IconAssociationEngine.cs').read_text(encoding='utf-8')
require('BuildGuidToIconIdIndex' in association and 'BuildUniqueAssetIndex' in association, 'Association pre-index optimization is missing')


legacy_scanner = (ROOT / 'src/CDRIconExtractor.Windows/Resources/GenericPeIconScanner.cs').read_text(encoding='utf-8')
require('TryConvertHorizontalStrip' in legacy_scanner and 'RT_BITMAP_STRIP_CELL' in legacy_scanner, 'Legacy X4 bitmap strip extraction is missing')
legacy_assoc = association
require('TryLegacyBmpCoordinates' in legacy_assoc and 'bmpRow' in legacy_assoc and 'bmpCol' in legacy_assoc, 'Legacy X4 bmpRow/bmpCol association is missing')
resource_reader = (ROOT / 'src/CDRIconExtractor.Windows/Resources/Win32ResourceReader.cs').read_text(encoding='utf-8')
require('LOAD_LIBRARY_AS_DATAFILE);' in resource_reader, 'Legacy resource modules must be opened as data files')
scan_coordinator = (app_root / 'Services/ScanCoordinator.cs').read_text(encoding='utf-8')
require('CrlGenericUI.dll' in scan_coordinator, 'CrlGenericUI.dll legacy scan path is missing')
ui_locator = (ROOT / 'src/CDRIconExtractor.Windows/Detection/UiDefinitionLocator.cs').read_text(encoding='utf-8')
require(r'Programs\UIConfig\CorelDRAW' in ui_locator, 'CorelDRAW X4 UIConfig path is missing')
require('DrawUI*.xml' in ui_locator, 'Fix9 DrawUI fragment discovery is missing')
resolver = (ROOT / 'src/CDRIconExtractor.Core/Parsing/CorelStringTableResolver.cs').read_text(encoding='utf-8')
require('CaptionTokenRegex' in resolver and 'StringReferenceRegex' in resolver, 'Fix7 Chinese string resource expression support is missing')
require('element.Ancestors()' in resolver and 'nearest ancestor' in resolver.lower(), 'Fix7 nested Chinese string schema support is missing')

search_matcher = (ROOT / 'src/CDRIconExtractor.Core/Search/TextSearchMatcher.cs').read_text(encoding='utf-8')
require('Convert to Curves' in search_matcher and 'Ctrl+Q' in search_matcher and '转曲' in search_matcher, 'CorelDRAW search aliases are missing')

source_files = list((ROOT / 'src').rglob('*.cs'))
source_text = '\n'.join(p.read_text(encoding='utf-8', errors='ignore') for p in source_files)
runtime_source_text = '\n'.join(
    p.read_text(encoding='utf-8', errors='ignore')
    for p in source_files
    if p.name != 'IconRegistrationTemplateGenerator.cs'
)
require('Corel.Interop' not in runtime_source_text, 'Corel.Interop dependency/reference found')
require('VGCore' not in runtime_source_text, 'VGCore runtime dependency/reference found outside generated template text')
require('Process.Start("CorelDRW' not in runtime_source_text, 'Code appears to start CorelDRAW')

publish = (ROOT / 'scripts/publish-win-x64.ps1').read_text(encoding='utf-8-sig')
require('PublishSingleFile=true' in publish, 'Publish script does not force single-file mode')
require('IncludeNativeLibrariesForSelfExtract=true' in publish, 'Publish script does not include native libraries')
require('CorelDRAW官方图标提取器.exe' in publish, 'Publish script does not validate expected EXE name')

for timing in [
    'InstallDetectionMs', 'UiLocateMs', 'DrawUiParseMs', 'StringTableMs', 'CrlIconsPngScanMs',
    'CrlIconsGuidMapMs', 'WorkspaceShortcutMs', 'LiveLocalizationMs', 'CoreResourceScanMs', 'FallbackPeScanMs', 'AssociationMs', 'SearchIndexMs', 'TotalMs'
]:
    require(timing in app_text, f'Missing timing name: {timing}')


# Fix9: layout alignment, filter/view separation, portable X4 and CorelDRAW 2026 UI fragments.
require(main_xaml.find('Content="全部命令"') < main_xaml.find('Content="有图标"') < main_xaml.find('Content="未关联"') < main_xaml.find('Content="原始资源"'), 'Fix9 filter order must be 全部命令 / 有图标 / 未关联 / 原始资源')
require('Text="筛选"' in main_xaml and 'Text="视图"' in main_xaml, 'Fix9 filter/view groups are not visually separated')
require(
    (main_xaml.count('<ColumnDefinition Width="2.15*"/>') >= 2 and main_xaml.count('<ColumnDefinition Width="1*"/>') >= 2)
    or ('<Grid Grid.Column="0">' in main_xaml and 'Grid.Row="0" Margin="0,0,0,7"' in main_xaml and 'Grid.Row="1"' in main_xaml),
    'Search/content shared column alignment is missing')
require('Content="手动添加"' not in main_xaml and 'PickInstallation()' in main_vm, 'Fix17 top-level manual add should be removed while scan fallback remains')
detector = (ROOT / 'src/CDRIconExtractor.Windows/Detection/CorelInstallDetector.cs').read_text(encoding='utf-8')
require('major is >= 14 and <= 40' in detector and 'CorelDRAW Graphics Suite X4' in detector, 'Fix9 X4/version 14 portable detection is missing')
require('Path.Combine(installRoot, "Draw", "CrlIcons.dll")' in detector and 'FindFileBounded' in detector, 'Fix9 modern CrlIcons bounded fallback discovery is missing')
require('DrawUI.items.xml' in scan_coordinator and 'DrawUI*.xml' in ui_locator, 'Fix9 CorelDRAW 2026 DrawUI.items.xml support is missing')
require('while (directory is not null)' in resolver and 'parentName' in resolver, 'Fix9 nested language-folder detection is missing')

# Fix8: command GUID / icon GUID reuse support.
icon_assoc_model = (ROOT / 'src/CDRIconExtractor.Core/Models/IconAssociation.cs').read_text(encoding='utf-8')
require('string? IconGuid = null' in icon_assoc_model, 'Fix8 IconAssociation.IconGuid field is missing')
icon_guid_util = (ROOT / 'src/CDRIconExtractor.Core/Utilities/IconGuidReference.cs').read_text(encoding='utf-8')
require('guid://' in icon_guid_util and 'FormatIconAttribute' in icon_guid_util, 'Fix8 guid:// formatter is missing')
require('TryDirectIconGuid' in association and 'IconGuidReference.Normalize' in association, 'Fix8 direct DrawUI icon GUID association is missing')
drawui_parser_tests = (ROOT / 'tests/CDRIconExtractor.Core.Tests/Parsing/DrawUiParserTests.cs').read_text(encoding='utf-8')
require('titleSource=\\\"*CT(' in drawui_parser_tests, 'Fix8.1 DrawUiParser test XML attribute quotes must be escaped in C# source')
require('titleSource="*CT(' not in drawui_parser_tests, 'Fix8.1 broken unescaped titleSource test literal detected')
require('CopyCommandGuidCommand' in main_vm and 'CopyIconGuidCommand' in main_vm and 'CopyIconAttributeCommand' in main_vm, 'Fix8 clipboard commands are missing')
require('Header="GUID"' in main_xaml and 'Header="GUID关系"' in main_xaml, 'Fix10 adaptive GUID list columns are missing')
require('复制图标GUID' in main_xaml and '复制 icon=引用' in main_xaml, 'Fix8 icon GUID copy UI is missing')
export_text = (ROOT / 'src/CDRIconExtractor.Core/Export/ExportService.cs').read_text(encoding='utf-8')
require('"IconGuid"' in export_text and '可复用图标GUID' in export_text, 'Fix8 export IconGuid index/report support is missing')

# Fix10: 2026 core resource scan, adaptive GUID presentation and code templates.
require('CoreResourceModuleLocator.LocateCoreModules' in scan_coordinator, 'Fix10 core resource modules are not scanned unconditionally')
require('needFallback = assets.Count' not in scan_coordinator, 'Fix10 still contains the old assets.Count fallback gate')
require('CoreResourceScanMs' in scan_coordinator and 'CrlGenericUI GUID map' in scan_coordinator, 'Fix10 core resource/GUID map diagnostics are missing')
core_locator = (ROOT / 'src/CDRIconExtractor.Windows/Resources/CoreResourceModuleLocator.cs').read_text(encoding='utf-8')
require('CrlGenericUI.dll' in core_locator and 'installation.ProgramPath' in core_locator, 'Fix10 core module locator is incomplete')
require('ResourceEntryGuid' in (ROOT / 'src/CDRIconExtractor.Core/Parsing/DrawUiParser.cs').read_text(encoding='utf-8'), 'Fix10 resEntry id GUID parsing is missing')
require('BuildPreferredGuidMappedAssetIndex' in association and 'BuildPreferredAssetIndex' in association, 'Fix10 resource association indexes are missing')
guid_presentation = (ROOT / 'src/CDRIconExtractor.Core/Utilities/IconGuidPresentation.cs').read_text(encoding='utf-8')
require('GUID（命令/图标共用）' in guid_presentation and 'ShowSeparate' in guid_presentation, 'Fix10 adaptive GUID presentation is missing')
template_generator = (ROOT / 'src/CDRIconExtractor.Core/Utilities/IconRegistrationTemplateGenerator.cs').read_text(encoding='utf-8')
require('GenerateVba' in template_generator and 'GenerateCpp' in template_generator and 'SetIcon2' in template_generator, 'Fix10 VBA/C++ icon registration templates are missing')
require('SelectedItem.ShowCombinedGuid' in main_xaml and 'SelectedItem.ShowSeparateGuids' in main_xaml, 'Fix10 adaptive GUID detail UI is missing')
require('生成 VBA 模板' in main_xaml and '生成 C++/CPG 模板' in main_xaml, 'Fix10 code-template buttons are missing')
require('Width="142"' in main_xaml and 'SelectedItem.ResourceId' in main_xaml and ('SelectedItem.OriginalSize' in main_xaml or 'SelectedItem.AvailableSizes' in main_xaml), 'Fix10 image-left/detail-right layout is missing')
icon_vm = (app_root / 'ViewModels/IconItemViewModel.cs').read_text(encoding='utf-8')
require('Asset.DisplayName' in icon_vm and '$"图标资源 {Asset.ResourceId}"' in icon_vm, 'Fix10/Fix18 raw-resource naming fallback is missing')
require('return FirstReadable(Command.LocalizedCaption, Command.Caption) ?? "未解析名称"' in icon_vm, 'Fix10 command fallback name must be 未解析名称')
require('CrlGenericUI {genericUi}' in main_vm and 'CrlIcons {crlIcons}' in main_vm, 'Fix10 per-source scan summary is missing')

# Fix11: code preview window, workspace shortcuts, live localized names and custom resource diagnostics.
code_window_xaml = (app_root / 'CodeTemplateWindow.xaml').read_text(encoding='utf-8')
code_window_cs = (app_root / 'CodeTemplateWindow.xaml.cs').read_text(encoding='utf-8')
require('复制全部' in code_window_xaml and '另存为' in code_window_xaml and 'CodeTextBox' in code_window_xaml, 'Fix11 code template popup UI is missing')
require('SaveFileDialog' in code_window_cs and 'Clipboard.SetText' in code_window_cs, 'Fix11 code template copy/save behavior is missing')
require('ShowCodeTemplate' in main_vm and 'CodeTemplateWindow' in main_vm, 'Fix11 template buttons still do not open a code window')
workspace_resolver = (ROOT / 'src/CDRIconExtractor.Core/Parsing/WorkspaceShortcutResolver.cs').read_text(encoding='utf-8')
require('content/workspace.xml' in workspace_resolver and 'keySequence' in workspace_resolver, 'Fix11 .cdws workspace shortcut parser is missing')
workspace_locator = (ROOT / 'src/CDRIconExtractor.Windows/Detection/WorkspaceLocator.cs').read_text(encoding='utf-8')
require('*.cdws' in workspace_locator and '_default.cdws' in workspace_locator, 'Fix11 workspace locator is missing')
live_resolver = (ROOT / 'src/CDRIconExtractor.Core/Parsing/LiveLocalizedStringResolver.cs').read_text(encoding='utf-8')
live_provider = (ROOT / 'src/CDRIconExtractor.Windows/Automation/CorelRunningLocalizationProvider.cs').read_text(encoding='utf-8')
require('LoadLocalizedString' in live_resolver and 'maxRequests' in live_resolver, 'Fix11 bounded live localization resolver is missing')
connector_text = (ROOT / 'src/CDRIconExtractor.Windows/Automation/CorelRunningInstanceConnector.cs').read_text(encoding='utf-8')
require('CorelDRAW.Application' in connector_text and 'GetActiveObject' in connector_text and 'LoadLocalizedString' in live_provider, 'Fix11/V1.16 running CorelDRAW localization connection path is missing')
require('WorkspaceShortcutMs' in scan_coordinator and 'LiveLocalizationMs' in scan_coordinator, 'Fix11 scan timings are missing')
require('InspectResourceTypes' in resource_reader and 'EnumResourceTypesW' in (ROOT / 'src/CDRIconExtractor.Windows/Resources/NativeMethods.cs').read_text(encoding='utf-8'), 'Fix11 all-resource-type enumeration is missing')
require('CUSTOM:' in legacy_scanner and 'IWin32ResourceCatalog' in legacy_scanner, 'Fix11 named/custom PE resource image decoding is missing')
require('CrlGenericUI资源类型：' in scan_coordinator and 'DiagnosticSummary' in main_vm, 'Fix11 CorelGenericUI resource diagnostics are not surfaced')
# Fix11.1: never name the EnumResTypeProc lParam as `_` when the callback body uses discard assignments.
require('EnumResTypeProc((hModule, typePtr, _) =>' not in resource_reader, 'Fix11.1 regression: `_` callback parameter shadows discard assignments and causes CS0029')


# Fix12: ID-only PNG export names, multi-icon templates, compact code popup.
require('EnsureUnique($"{resourceId}.png"' in export_text, 'Fix12 exported PNG names must be resource-ID only')
require('exportedAssetPaths' in export_text, 'Fix12 duplicate image export dedupe is missing')
require('GenerateVbaBatch' in template_generator and 'GenerateCppBatch' in template_generator, 'Fix12 batch VBA/C++ template generators are missing')
require('IsMarked' in icon_vm and '批量 VBA 模板' in main_xaml and '批量 C++/CPG 模板' in main_xaml, 'Fix12 multi-select batch-template UI is missing')
require('Background="#0B0F14"' in code_window_xaml and 'Foreground="#F8FAFC"' in code_window_xaml, 'Fix12 code editor must be black with white text')
require('Width="610" Height="445"' in code_window_xaml, 'Fix12 compact code-template window size is missing')
require('CornerRadius="7"' in code_window_xaml and 'RoundCodeButton' in code_window_xaml, 'Fix12 rounded template-window buttons are missing')
require('GenerateBatchVbaTemplateCommand' in main_vm and 'GenerateBatchCppTemplateCommand' in main_vm, 'Fix12 batch template commands are missing')

# Fix13: running CorelDRAW captions, pending Icon GUIDs and SetIcon2 validation.
live_caption = (ROOT / 'src/CDRIconExtractor.Core/Parsing/LiveCaptionResolver.cs').read_text(encoding='utf-8')
icon_validator = (ROOT / 'src/CDRIconExtractor.Windows/Automation/CorelRunningIconValidator.cs').read_text(encoding='utf-8')
require('GetCaptionText' in live_caption and 'IUiCaptionProvider' in live_caption, 'Fix13 live UI caption resolver is missing')
require('GetCaptionText' in live_provider and 'FrameWork' in live_provider and 'Automation' in live_provider, 'Fix13 running CorelDRAW provider does not expose FrameWork.Automation.GetCaptionText')
require('VersionMajor' in connector_text and 'actual.Value != versionMajor' in connector_text, 'Fix13/V1.16 live CorelDRAW connection must reject the wrong running version')
require('SetIcon2' in icon_validator and 'CommandBars' in icon_validator and 'AddCustomButton' in icon_validator and '.Delete()' in icon_validator, 'Fix13 temporary SetIcon2 GUID validation flow is missing')
require('Content="待验证"' in main_xaml and 'IsFilterPending' in main_vm, 'Fix13 pending Icon GUID filter is missing')
require('实机验证GUID' in main_xaml and 'ValidateIconGuidCommand' in main_vm, 'Fix13 live GUID validation UI/command is missing')
require('HasConfirmedPreview' in icon_vm and 'IsPendingIconPreview' in icon_vm, 'Fix13 preview/pending semantics are missing')
require('暂无本地预览' in icon_vm, 'Fix13 validation status must not pretend that SetIcon2 produced a local preview')


# Fix14 layout retained; V1.19 intentionally removes the failed live-preview hydration feature.
require('CorelDRAW 官方图标提取器  / GUID 开发工具' in main_xaml, 'Fix14 subtitle is missing')
require('只读提取' not in main_xaml, 'Fix14 old subtitle remains')
about_xaml = (app_root / 'AboutWindow.xaml').read_text(encoding='utf-8')
require('浏览图标与 GUID' in about_xaml, 'about purpose is missing')
require('CDR补全预览' not in main_xaml and 'HydrateLivePreviewsCommand' not in main_vm, 'V1.19 retired live-preview hydration UI/command returned')
require(not (ROOT / 'src/CDRIconExtractor.Windows/Automation/CorelRunningIconPreviewRenderer.cs').exists(), 'V1.19 retired live preview renderer source still exists')
require('ResourceIdHint' in icon_assoc_model and 'resourceIdHint' in association, 'pending resource-ID hint is missing')

# Fix15: standalone CrlIcons.dll mode and V1.14 Windows compile hotfix.
windows_project = (ROOT / 'src/CDRIconExtractor.Windows/CDRIconExtractor.Windows.csproj').read_text(encoding='utf-8')
external_catalog = (ROOT / 'src/CDRIconExtractor.Core/Association/ExternalCrlIconsCatalogBuilder.cs').read_text(encoding='utf-8')
require('加载CrlIcons.dll' in main_xaml and 'LoadCrlIconsCommand' in main_vm, 'Fix15 external CrlIcons.dll UI/command is missing')
require('ScanCrlIconsOnlyAsync' in scan_coordinator and 'ExternalCrlIconsCatalogBuilder.Build' in scan_coordinator, 'Fix15 standalone CrlIcons scan path is missing')
require('External CrlIcons GUID map' in scan_coordinator and 'IconGuidReference.Normalize' in external_catalog, 'Fix15 external GUID-map catalog is missing')
require('<UseWPF>true</UseWPF>' in windows_project, 'Fix15 Windows project must use WPF for HBITMAP encoding')
# Fix15.1: Windows compile hotfix must not depend on implicit namespace inheritance.
windows_globals = (ROOT / 'src/CDRIconExtractor.Windows/GlobalUsings.cs').read_text(encoding='utf-8')
for namespace in ['System', 'System.Collections.Generic', 'System.IO', 'System.Linq', 'System.Threading', 'System.Threading.Tasks']:
    require(f'global using {namespace};' in windows_globals, f'Fix15.1 Windows global using missing: {namespace}')
external_test = (ROOT / 'tests/CDRIconExtractor.Core.Tests/Association/ExternalCrlIconsCatalogBuilderTests.cs').read_text(encoding='utf-8')
require('using Microsoft.VisualStudio.TestTools.UnitTesting;' in external_test, 'Fix15.1 external catalog test missing MSTest namespace')
require('<ImplicitUsings>enable</ImplicitUsings>' in windows_project, 'Fix15.1 Windows project must explicitly enable implicit usings')
core_test_project = (ROOT / 'tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj').read_text(encoding='utf-8')
require('<ImplicitUsings>enable</ImplicitUsings>' in core_test_project, 'Fix15.1 Core test project must explicitly enable implicit usings')
require('<Version>1.21</Version>' in project_text, 'V1.21 product version is not 1.21')


# V1.16: robust running-instance connection and diagnostics.
require('GetRunningObjectTable' in connector_text and 'EnumRunning' in connector_text and 'ROT' in connector_text, 'V1.16 ROT fallback connection is missing')
require('Process.GetProcessesByName("CorelDRW")' in connector_text, 'V1.16 CorelDRW process diagnostics are missing')
require('Activator.CreateInstance' not in connector_text, 'V1.16 connector must never launch CorelDRAW')
require('Content="连接诊断"' not in main_xaml and 'ConnectionDiagnosticCommand' not in main_vm, 'V1.19 removed connection diagnostic UI/command returned')
require('CorelRunningInstanceConnector.TryConnect' in live_provider, 'V1.16 localization provider must use shared connector')
require('CorelRunningInstanceConnector.TryConnect' in icon_validator, 'V1.16 icon validator must use shared connector')
require('CorelDRAW 连接：' in scan_coordinator and 'connectionDiagnostic.ToCompactText()' in scan_coordinator, 'V1.16 scan connection diagnostics are not surfaced')

# Fix18: CorelDRAW 2026 Modern.crlicons native container support.
modern_reader = (ROOT / 'src/CDRIconExtractor.Core/Parsing/ModernCrlIconsReader.cs').read_text(encoding='utf-8')
icon_asset_model = (ROOT / 'src/CDRIconExtractor.Core/Models/IconAsset.cs').read_text(encoding='utf-8')
require('ZipFile.OpenRead' in modern_reader and '24, 48, 72' in modern_reader, 'Fix18 Modern.crlicons ZIP/multi-size reader is missing')
require('DisplayName' in icon_asset_model and 'ResourcePath' in icon_asset_model and 'Variants' in icon_asset_model, 'Fix18 multi-size icon metadata is missing')
require(('加载Modern.crlicons' in main_xaml or '加载新版图标资源' in main_xaml) and 'LoadModernIconsCommand' in main_vm, 'Fix18+ Modern/new-resource manual load UI is missing')
require('Modern.crlicons' in scan_coordinator and 'ScanModernCrlIconsOnlyAsync' in scan_coordinator, 'Fix18 Modern.crlicons automatic/standalone scan path is missing')
require("Split(new[] { '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)" in connector_text, 'Fix18 V1.17 StringSplitOptions compile fix is missing')


# Fix19: Chinese intelligent aliases + retired unusable top features.
chinese_aliases = (ROOT / 'src/CDRIconExtractor.Core/Search/ChineseSearchAliases.cs').read_text(encoding='utf-8')
search_matcher = (ROOT / 'src/CDRIconExtractor.Core/Search/TextSearchMatcher.cs').read_text(encoding='utf-8')
for token in ['转曲', '解组', '群组', '焊接', '修剪', '相交', '轮廓图', '透明度', '二维码', '水平居中', '垂直居中']:
    require(token in chinese_aliases, f'Fix19 Chinese alias missing: {token}')
for token in ['Convert to Curves', 'Ungroup', 'Group', 'Weld', 'Trim', 'Intersect', 'Contour', 'Transparency', 'QR Code']:
    require(token in chinese_aliases, f'Fix19 English alias target missing: {token}')
require('ChineseSearchAliases.Expand' in search_matcher, 'Fix19 matcher is not wired to ChineseSearchAliases')
require('中文智能搜索' in main_xaml, 'Fix19 Chinese smart-search placeholder is missing')
require('加载CrlIcons.dll' in main_xaml and '加载新版图标资源' in main_xaml and '扫描官方图标' in main_vm, 'Fix19+ must preserve loading/scanning functions')
require('命令 GUID' in main_xaml and '图标 GUID 实机状态' in main_xaml, 'Fix19 advanced GUID details must remain expanded')

if errors:
    print('STATIC AUDIT FAILED')
    for error in errors:
        print(f'- {error}')
    sys.exit(1)

print('STATIC AUDIT PASS')
print('- project/XAML XML parsed')
print('- compact layout retained; retired CDR preview hydration remains removed')
# Fix20: CorelDRAW 2026 Modern.crlicons named-resource/command association.
require('BuildModernAssetIndex' in association and 'TryNamedModernResource' in association, 'Fix20 Modern named-resource association is missing')
require('TryModernCaptionResource' in association and 'IsResourceDefinition(command)' in association, 'Fix20 Modern caption/resourceEntry association is missing')
require('命令关联' in main_vm and '映射GUID' in main_vm, 'Fix20+ Modern mapping status counters are missing')

print('- V1.21 retains Chinese alias dictionary wired to search matcher')
print('- standalone CrlIcons.dll and Modern.crlicons + icons.map.xml loading paths present')
print('- shared ProgID/ROT connector retained internally; diagnostic UI removed')
print('- Fix15.1 explicit Windows/test namespace imports present')
print('- Fix7 branding/search/persistent selected-state/default-wall requirements present')
print('- Chinese nested-string/CT reference parsing, association/UI performance safeguards and X4 path present')
print('- no Corel COM dependency markers')
print('- single-file publish settings present')
print('- required timing names present')


# Fix21: authoritative CorelDRAW 2026 icons.map.xml -> Modern.crlicons GUID mapping.
icon_map_parser = (ROOT / 'src/CDRIconExtractor.Core/Parsing/IconMapXmlParser.cs').read_text(encoding='utf-8')
icon_map_binder = (ROOT / 'src/CDRIconExtractor.Core/Parsing/ModernIconMapBinder.cs').read_text(encoding='utf-8')
template_generator = (ROOT / 'src/CDRIconExtractor.Core/Utilities/IconRegistrationTemplateGenerator.cs').read_text(encoding='utf-8')
require('IconMapEntry' in icon_map_parser and 'icons.map.xml' in icon_map_parser, 'Fix21 icons.map.xml parser is missing')
require('IconGuids' in icon_asset_model and 'IconGuidSource' in icon_asset_model, 'Fix21 IconAsset GUID metadata is missing')
require('ModernIconMapBindResult' in icon_map_binder and 'MatchedReusableGuidEntries' in icon_map_binder, 'Fix21 Modern icon-map binder is missing')
require('TryMappedAssetIconGuid' in association and 'TryMappedAssetCommandGuid' in association, 'Fix21 association does not consume icons.map.xml GUIDs')
require('LocateIconMapXml' in scan_coordinator and 'IconMapXmlParser.Parse' in scan_coordinator and 'ModernIconMapBinder.Bind' in scan_coordinator, 'Fix21 automatic icons.map.xml scan chain is missing')
require('加载新版图标资源' in main_xaml and 'Modern.crlicons + icons.map.xml' in main_vm, 'Fix21 combined manual load UI is missing')
require('映射GUID' in main_vm and 'Modern有GUID' in main_vm, 'Fix21 GUID counters are missing')
require('图标资源:' in template_generator and 'GUID 来源:' in template_generator, 'Fix21 template provenance comments are missing')
require('其他可用图标 GUID' in main_xaml and 'GUID映射来源' in main_xaml, 'Fix21 expanded multi-GUID details are missing')
require('<Version>1.21</Version>' in project_text, 'Fix21 app version is not 1.21')

if errors:
    print('STATIC AUDIT FAILED (Fix21)')
    for error in errors:
        print(f'- {error}')
    sys.exit(1)
print('- Fix21 authoritative icons.map.xml mapping chain present')
