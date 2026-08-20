from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ENGINE = (ROOT / 'src/CDRIconExtractor.Core/Association/IconAssociationEngine.cs').read_text(encoding='utf-8')
VM = (ROOT / 'src/CDRIconExtractor.App/ViewModels/MainViewModel.cs').read_text(encoding='utf-8')
SCAN = (ROOT / 'src/CDRIconExtractor.App/Services/ScanCoordinator.cs').read_text(encoding='utf-8')
ASSOC_TESTS = (ROOT / 'tests/CDRIconExtractor.Core.Tests/Association/IconAssociationEngineTests.cs').read_text(encoding='utf-8')
SEARCH_TESTS = (ROOT / 'tests/CDRIconExtractor.Core.Tests/Search/SearchIndexTests.cs').read_text(encoding='utf-8')
CSPROJ = (ROOT / 'src/CDRIconExtractor.App/CDRIconExtractor.App.csproj').read_text(encoding='utf-8')
STATUS = (ROOT / 'SOURCE_STATUS.txt').read_text(encoding='utf-8')

checks = []
def require(condition, message):
    checks.append((bool(condition), message))

require('BuildModernAssetIndex' in ENGINE, 'Modern resource index exists')
require('TryNamedModernResource' in ENGINE, 'named Modern resource association exists')
require('TryModernCaptionResource' in ENGINE, 'caption-to-Modern fallback exists')
require('ResourcePath' in ENGINE and 'DisplayName' in ENGINE and 'ResourceId' in ENGINE, 'Modern index uses resource id/name/path')
require('IsResourceDefinition(command)' in ENGINE, 'resourceEntry special handling exists')
require('IconGuidReference.Normalize(command.Guid)' in ENGINE, 'resourceEntry GUID can become icon GUID')
require('TryImageGuid(command, exactByGuid)' in ENGINE, 'existing guid:// second-hop remains')
require('AssociationConfidence.Exact' in ENGINE and 'AssociationConfidence.Strong' in ENGINE, 'confidence levels retained')
require('命令关联' in VM and '映射GUID' in VM, 'UI status exposes Modern mapping counts')
require('加载新版图标资源' in VM and 'icons.map.xml' in VM, 'manual Modern load is upgraded to combined resource loading')
require('Modern 官方 GUID' in SCAN, 'scan diagnostics include Modern official GUID/association count')
require('Associate_ModernNamedResourceEntry_MapsAssetAndUsesResourceEntryGuidAsIconGuid' in ASSOC_TESTS, 'resourceEntry regression test exists')
require('Associate_CommandIconGuid_ResolvesThroughNamedModernResourceEntry' in ASSOC_TESTS, 'two-hop command/icon GUID regression test exists')
require('Associate_ModernCaptionSlug_MapsAssetWhenNoExplicitResourceHintExists' in ASSOC_TESTS, 'caption slug regression test exists')
require('Filter_ChineseAliasFindsModernAssetAfterCaptionAssociation' in SEARCH_TESTS, 'Chinese search regression test exists')
require('<Version>1.21</Version>' in CSPROJ, 'app version is 1.21')
require('V1.21 Fix21' in STATUS, 'SOURCE_STATUS identifies V1.21 Fix21')

failed = [m for ok, m in checks if not ok]
for ok, message in checks:
    print(('PASS' if ok else 'FAIL') + ': ' + message)
if failed:
    raise SystemExit(f'{len(failed)} Fix20 audit checks failed')
print(f'Fix20 audit PASS: {len(checks)} checks')
