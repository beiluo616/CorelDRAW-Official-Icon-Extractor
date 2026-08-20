from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors = []

win_globals = ROOT / 'src/CDRIconExtractor.Windows/GlobalUsings.cs'
if not win_globals.exists():
    errors.append('Windows GlobalUsings.cs missing')
else:
    text = win_globals.read_text(encoding='utf-8')
    for ns in ['System','System.Collections.Generic','System.IO','System.Linq','System.Threading','System.Threading.Tasks']:
        if f'global using {ns};' not in text:
            errors.append(f'missing Windows global using: {ns}')

external_test = ROOT / 'tests/CDRIconExtractor.Core.Tests/Association/ExternalCrlIconsCatalogBuilderTests.cs'
text = external_test.read_text(encoding='utf-8')
if 'using Microsoft.VisualStudio.TestTools.UnitTesting;' not in text:
    errors.append('ExternalCrlIconsCatalogBuilderTests missing MSTest using')
if 'using System.Collections.Generic;' not in text:
    errors.append('ExternalCrlIconsCatalogBuilderTests missing collections using')

for rel in [
    'src/CDRIconExtractor.Windows/CDRIconExtractor.Windows.csproj',
    'tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj',
]:
    text = (ROOT / rel).read_text(encoding='utf-8')
    if '<ImplicitUsings>enable</ImplicitUsings>' not in text:
        errors.append(f'{rel} does not explicitly enable implicit usings')

if errors:
    print('FIX15.1 COMPILE NAMESPACE AUDIT FAILED')
    for e in errors:
        print('-', e)
    raise SystemExit(1)

print('FIX15.1 COMPILE NAMESPACE AUDIT PASS')
