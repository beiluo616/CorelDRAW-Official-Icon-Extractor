from pathlib import Path
root=Path(__file__).resolve().parents[1]
checks={
    'reader': root/'src/CDRIconExtractor.Core/Parsing/ModernCrlIconsReader.cs',
}
errors=[]
if not checks['reader'].exists(): errors.append('ModernCrlIconsReader.cs missing')
asset=(root/'src/CDRIconExtractor.Core/Models/IconAsset.cs').read_text(encoding='utf-8')
for token in ['DisplayName','ResourcePath','Variants']:
    if token not in asset: errors.append(f'IconAsset missing {token}')
xaml=(root/'src/CDRIconExtractor.App/MainWindow.xaml').read_text(encoding='utf-8')
if '加载Modern.crlicons' not in xaml and '加载新版图标资源' not in xaml: errors.append('Modern/new-resource load button missing')
scan=(root/'src/CDRIconExtractor.App/Services/ScanCoordinator.cs').read_text(encoding='utf-8')
if 'Modern.crlicons' not in scan: errors.append('automatic Modern.crlicons scan missing')
connector=(root/'src/CDRIconExtractor.Windows/Automation/CorelRunningInstanceConnector.cs').read_text(encoding='utf-8')
if "Split('.', '-', ' ', StringSplitOptions.RemoveEmptyEntries)" in connector: errors.append('invalid Split overload still present')
if errors:
    print('FIX18 AUDIT FAIL')
    for e in errors: print('-',e)
    raise SystemExit(1)
print('FIX18 AUDIT PASS')
