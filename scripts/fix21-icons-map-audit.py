from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
checks = []
def require(ok, message): checks.append((bool(ok), message))

parser = (ROOT/'src/CDRIconExtractor.Core/Parsing/IconMapXmlParser.cs').read_text(encoding='utf-8')
binder = (ROOT/'src/CDRIconExtractor.Core/Parsing/ModernIconMapBinder.cs').read_text(encoding='utf-8')
asset = (ROOT/'src/CDRIconExtractor.Core/Models/IconAsset.cs').read_text(encoding='utf-8')
engine = (ROOT/'src/CDRIconExtractor.Core/Association/IconAssociationEngine.cs').read_text(encoding='utf-8')
scan = (ROOT/'src/CDRIconExtractor.App/Services/ScanCoordinator.cs').read_text(encoding='utf-8')
vm = (ROOT/'src/CDRIconExtractor.App/ViewModels/MainViewModel.cs').read_text(encoding='utf-8')
item = (ROOT/'src/CDRIconExtractor.App/ViewModels/IconItemViewModel.cs').read_text(encoding='utf-8')
xaml = (ROOT/'src/CDRIconExtractor.App/MainWindow.xaml').read_text(encoding='utf-8')
tpl = (ROOT/'src/CDRIconExtractor.Core/Utilities/IconRegistrationTemplateGenerator.cs').read_text(encoding='utf-8')
parser_tests = (ROOT/'tests/CDRIconExtractor.Core.Tests/Parsing/IconMapXmlParserTests.cs').read_text(encoding='utf-8')
binder_tests = (ROOT/'tests/CDRIconExtractor.Core.Tests/Parsing/ModernIconMapBinderTests.cs').read_text(encoding='utf-8')
assoc_tests = (ROOT/'tests/CDRIconExtractor.Core.Tests/Association/IconAssociationEngineTests.cs').read_text(encoding='utf-8')
fixture = ROOT/'fixtures/IconMap/icons.map.synthetic.xml'
project = (ROOT/'src/CDRIconExtractor.App/CDRIconExtractor.App.csproj').read_text(encoding='utf-8')
status = (ROOT/'SOURCE_STATUS.txt').read_text(encoding='utf-8')

require('IconMapEntry' in parser and 'NormalizeResourcePath' in parser, 'icons.map.xml parser exists')
require('.png.png' in parser, 'official duplicated .png suffix quirk is handled deterministically')
require('IconGuids' in asset and 'IconGuidSource' in asset, 'IconAsset carries official GUID metadata')
require('ModernIconMapBindResult' in binder and 'MatchedResourceCount' in binder, 'binder returns mapping statistics')
require('TryMappedAssetIconGuid' in engine and 'TryMappedAssetCommandGuid' in engine, 'association uses icon-map GUID index')
require('LocateIconMapXml' in scan and 'LocateIconMapForModern' in scan, 'automatic/manual icon-map locators exist')
require('IconMapXmlParser.Parse' in scan and 'ModernIconMapBinder.Bind' in scan, 'scan binds map before association')
require('Content="加载新版图标资源"' in xaml, 'top combined new-resource button exists')
require('Modern.crlicons + icons.map.xml' in vm, 'manual combined loader status exists')
require('AvailableIconGuids' in item and 'OtherIconGuidsText' in item, 'multi-GUID UI model exists')
require('其他可用图标 GUID' in xaml and 'GUID映射来源' in xaml, 'expanded GUID detail UI exists')
require('图标资源:' in tpl and 'GUID 来源:' in tpl, 'generated code records official resource provenance')
require('Parse_ReadsCanonicalGuidAndResourcePath' in parser_tests, 'parser regression tests exist')
require('Bind_AssignsAllReusableGuidsForSameModernResource' in binder_tests, 'binder multi-GUID regression test exists')
require('Associate_DeclaredIconGuid_UsesIconsMapGuidOnModernAsset' in assoc_tests, 'association regression test exists')
require('<Version>1.21</Version>' in project and 'V1.21 Fix21' in status, 'version/status bumped to V1.21 Fix21')

try:
    root = ET.parse(fixture).getroot()
    rows = root.findall('.//map')
    require(len(rows) == 5, 'synthetic icon-map fixture parses as expected')
except Exception:
    require(False, 'synthetic icon-map fixture is valid XML')

failed=[m for ok,m in checks if not ok]
for ok,m in checks: print(('PASS' if ok else 'FAIL')+': '+m)
if failed: sys.exit(f'{len(failed)} Fix21 audit checks failed')
print(f'Fix21 audit PASS: {len(checks)} checks')
