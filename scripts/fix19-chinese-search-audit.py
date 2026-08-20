from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
XAML = (ROOT / 'src/CDRIconExtractor.App/MainWindow.xaml').read_text(encoding='utf-8-sig')
VM = (ROOT / 'src/CDRIconExtractor.App/ViewModels/MainViewModel.cs').read_text(encoding='utf-8-sig')
MATCHER = (ROOT / 'src/CDRIconExtractor.Core/Search/TextSearchMatcher.cs').read_text(encoding='utf-8-sig')
ALIASES_PATH = ROOT / 'src/CDRIconExtractor.Core/Search/ChineseSearchAliases.cs'
ALIASES = ALIASES_PATH.read_text(encoding='utf-8-sig') if ALIASES_PATH.exists() else ''
TESTS = (ROOT / 'tests/CDRIconExtractor.Core.Tests/Search/SearchIndexTests.cs').read_text(encoding='utf-8-sig')
CSPROJ = (ROOT / 'src/CDRIconExtractor.App/CDRIconExtractor.App.csproj').read_text(encoding='utf-8-sig')
STATUS = (ROOT / 'SOURCE_STATUS.txt').read_text(encoding='utf-8-sig')

checks = []
def require(condition, message):
    checks.append((condition, message))

# Removed user-facing features.
require('CDR补全预览' not in XAML, 'top CDR补全预览 button must be removed')
require('HydrateLivePreviewsCommand' not in VM, 'CDR preview hydration command must be removed from MainViewModel')
require('ConnectionDiagnosticCommand' not in VM, 'connection diagnostic command must be removed from MainViewModel')
require('Content="连接诊断"' not in XAML, 'top 连接诊断 button must be removed')
require('请点击顶部“连接诊断”查看详情' not in VM, 'validation failure text must not point to deleted diagnostic UI')

# Existing loading and advanced GUID UI must remain.
require('Content="加载CrlIcons.dll"' in XAML, '加载CrlIcons.dll must remain')
require('Content="加载新版图标资源"' in XAML, 'new-resource manual load must remain')
require('命令 GUID' in XAML and '图标 GUID 实机状态' in XAML, 'advanced GUID information must remain visible')

# Chinese designer-language search aliases.
for alias in ['转曲', '解组', '取消群组', '解散群组', '群组', '焊接', '修剪', '相交', '轮廓图', '透明度', '二维码', '水平居中', '垂直居中']:
    require(alias in ALIASES, f'Chinese search alias missing: {alias}')
for english in ['Convert to Curves', 'Ungroup', 'Group', 'Weld', 'Trim', 'Intersect', 'Contour', 'Transparency', 'QR Code', 'Center Horizontally', 'Center Vertically']:
    require(english in ALIASES, f'English search target missing: {english}')
require('中文' in XAML and '转曲' in XAML and '焊接' in XAML, 'search placeholder should advertise Chinese smart search')
require('Filter_MatchesChineseDesignerAliasAgainstEnglishCaption' in TESTS, 'Chinese alias behavior tests are missing')

# Version/status bump.
require('<Version>1.21</Version>' in CSPROJ, 'app version must be 1.21')
require('V1.21 Fix21' in STATUS, 'SOURCE_STATUS must identify V1.21 Fix21')

failed = [message for ok, message in checks if not ok]
if failed:
    print('Fix19 audit: FAIL')
    for message in failed:
        print(' -', message)
    sys.exit(1)

print(f'Fix19 audit: PASS ({len(checks)} checks)')
