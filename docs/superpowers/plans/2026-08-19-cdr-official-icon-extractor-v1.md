# CDR 官方图标提取器 V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个“北落果制作”的单文件 Windows EXE，自动检测本机 CorelDRAW，优先解析 `CrlIcons.dll` 与 DrawUI 定义，建立命令 GUID ↔ 官方图标映射，支持搜索、图标墙预览和 PNG/CSV/JSON 导出。

**Architecture:** 解决方案拆成 `Core`（纯模型/解析/导出）、`Windows`（注册表与 Win32 资源）、`App`（WPF UI）三个项目，测试项目只依赖 Core/Windows 的可测试边界。图标提取优先走已经从公开 CorelDRAW 开发工具研究确认的 `CrlIcons.dll` 适配器：PNG 图像从 DLL 二进制资源区识别，GUID→图标序号从 `RT_RCDATA` 中读取；通用 PE 资源扫描作为后备路径，DrawUI XML 提供 Caption/Shortcut/GUID 与资源提示。

**Tech Stack:** C# 12、.NET 8、WPF、win-x64、P/Invoke (`LoadLibraryExW`, `EnumResourceNamesW`, `FindResourceExW`, `LoadResource`, `SizeofResource`)、`System.Xml.Linq`、`System.Text.Json`、MSTest、PowerShell 发布脚本。

**Spec:** `docs/superpowers/specs/2026-08-19-cdr-official-icon-extractor-design.md`

## Global Constraints

- 独立 Windows 工具，不集成 F10AI。
- 单文件 EXE，自包含运行时，用户无需安装 .NET。
- 双击即可运行，不需要安装程序。
- 工具界面和“关于”窗口包含“北落果制作”。
- 数据源仅来自用户本机已安装的 CorelDRAW。
- 不内置、不重新分发 CorelDRAW 官方图标库。
- 只读 CorelDRAW 文件；默认不要求管理员权限。
- 不要求 CorelDRAW 正在运行。
- 目标版本优先覆盖 CorelDRAW X8～2027；实际能力按本机版本结构适配。
- 不修改 DrawUI.xml、workspace、CorelDRAW 注册表配置或安装目录。
- 不上传网络；V1 完全离线。
- 关联置信度必须显式区分 `Exact / Strong / Heuristic / Unmapped`。
- 首要实机目标命令：转换为曲线（Ctrl+Q）、导入（Ctrl+I）、导出（Ctrl+E）。

## File Structure

```text
CDR_Official_Icon_Extractor/
├─ CDRIconExtractor.sln
├─ Directory.Build.props
├─ src/
│  ├─ CDRIconExtractor.Core/
│  │  ├─ CDRIconExtractor.Core.csproj
│  │  ├─ Models/
│  │  │  ├─ CorelInstallation.cs
│  │  │  ├─ DrawUiCommand.cs
│  │  │  ├─ ResourceHint.cs
│  │  │  ├─ IconAsset.cs
│  │  │  ├─ IconAssociation.cs
│  │  │  └─ ScanResult.cs
│  │  ├─ Parsing/
│  │  │  ├─ DrawUiParser.cs
│  │  │  ├─ CrlIconGuidMapParser.cs
│  │  │  └─ PngStreamScanner.cs
│  │  ├─ Search/SearchIndex.cs
│  │  ├─ Association/IconAssociationEngine.cs
│  │  ├─ Export/ExportService.cs
│  │  └─ Utilities/FileNameSanitizer.cs
│  ├─ CDRIconExtractor.Windows/
│  │  ├─ CDRIconExtractor.Windows.csproj
│  │  ├─ Detection/CorelInstallDetector.cs
│  │  ├─ Detection/UiDefinitionLocator.cs
│  │  ├─ Resources/NativeMethods.cs
│  │  ├─ Resources/Win32ResourceReader.cs
│  │  ├─ Resources/CrlIconsReader.cs
│  │  └─ Resources/GenericPeIconScanner.cs
│  └─ CDRIconExtractor.App/
│     ├─ CDRIconExtractor.App.csproj
│     ├─ App.xaml
│     ├─ App.xaml.cs
│     ├─ MainWindow.xaml
│     ├─ MainWindow.xaml.cs
│     ├─ ViewModels/MainViewModel.cs
│     ├─ ViewModels/IconItemViewModel.cs
│     ├─ Infrastructure/ObservableObject.cs
│     ├─ Infrastructure/RelayCommand.cs
│     ├─ Infrastructure/AsyncRelayCommand.cs
│     ├─ Services/ScanCoordinator.cs
│     ├─ Services/PreviewImageService.cs
│     ├─ Services/AppLogger.cs
│     └─ AboutWindow.xaml
├─ tests/
│  ├─ CDRIconExtractor.Core.Tests/
│  │  ├─ Parsing/DrawUiParserTests.cs
│  │  ├─ Parsing/CrlIconGuidMapParserTests.cs
│  │  ├─ Parsing/PngStreamScannerTests.cs
│  │  ├─ Association/IconAssociationEngineTests.cs
│  │  ├─ Search/SearchIndexTests.cs
│  │  └─ Export/ExportServiceTests.cs
│  └─ CDRIconExtractor.Windows.Tests/
│     ├─ Detection/CorelInstallDetectorTests.cs
│     └─ Resources/Win32ResourceReaderTests.cs
├─ fixtures/
│  ├─ DrawUi/
│  │  ├─ modern.xml
│  │  ├─ legacy.xml
│  │  └─ malformed-partial.xml
│  └─ CrlIcons/
│     ├─ guid-map-modern.bin
│     ├─ guid-map-legacy.bin
│     └─ embedded-png-stream.bin
├─ scripts/publish-win-x64.ps1
├─ README.md
└─ docs/superpowers/...
```

---

### Task 1: Solution skeleton, domain contracts, and single-file publish settings

**Files:**
- Create: `CDRIconExtractor.sln`
- Create: `Directory.Build.props`
- Create: `src/CDRIconExtractor.Core/CDRIconExtractor.Core.csproj`
- Create: `src/CDRIconExtractor.Windows/CDRIconExtractor.Windows.csproj`
- Create: `src/CDRIconExtractor.App/CDRIconExtractor.App.csproj`
- Create: `tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj`
- Create: `tests/CDRIconExtractor.Windows.Tests/CDRIconExtractor.Windows.Tests.csproj`
- Create: `src/CDRIconExtractor.Core/Models/*.cs`
- Test: `tests/CDRIconExtractor.Core.Tests/Models/DomainModelTests.cs`

**Interfaces:**
- Produces: `CorelInstallation`, `DrawUiCommand`, `ResourceHint`, `IconAsset`, `IconAssociation`, `AssociationConfidence`, `ScanResult` used by every later task.

- [ ] **Step 1: Write the failing domain-model test**

```csharp
[TestMethod]
public void IconAssociation_ExposesConfidenceAndAsset()
{
    var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "42", 16, 16, "abc", new byte[] { 1 });
    var command = new DrawUiCommand("{11111111-1111-1111-1111-111111111111}", null, "Convert to Curves", "转换为曲线", "Ctrl+Q", "itemData", [], "/ui/itemData[1]");
    var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "GUID map id=42");

    Assert.AreEqual(AssociationConfidence.Exact, association.Confidence);
    Assert.AreSame(asset, association.Asset);
}
```

- [ ] **Step 2: Run the test and verify it fails because the types do not exist**

Run on Windows/dev machine:

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj --filter IconAssociation_ExposesConfidenceAndAsset
```

Expected: compile failure for missing domain types.

- [ ] **Step 3: Create the domain records and publish configuration**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

`CDRIconExtractor.App.csproj` must include:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <UseWPF>true</UseWPF>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  <AssemblyName>CDR官方图标提取器</AssemblyName>
  <Product>CDR 官方图标提取器</Product>
  <Authors>北落果</Authors>
  <Version>1.0.0</Version>
</PropertyGroup>
```

`Models/IconAssociation.cs`:

```csharp
public enum AssociationConfidence { Exact, Strong, Heuristic, Unmapped }

public sealed record IconAssociation(
    DrawUiCommand Command,
    IconAsset? Asset,
    AssociationConfidence Confidence,
    string Reason);
```

- [ ] **Step 4: Run all Core model tests**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CDRIconExtractor.sln Directory.Build.props src tests
git commit -m "build: scaffold CDR icon extractor solution"
```

---

### Task 2: CorelDRAW installation detection and UI definition location

**Files:**
- Create: `src/CDRIconExtractor.Windows/Detection/CorelInstallDetector.cs`
- Create: `src/CDRIconExtractor.Windows/Detection/UiDefinitionLocator.cs`
- Test: `tests/CDRIconExtractor.Windows.Tests/Detection/CorelInstallDetectorTests.cs`
- Test: `tests/CDRIconExtractor.Windows.Tests/Detection/UiDefinitionLocatorTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<CorelInstallation> Detect()` and `IReadOnlyList<string> Locate(CorelInstallation installation)`.
- `CorelInstallation` includes `DisplayName`, `VersionMajor`, `FileVersion`, `ProgramPath`, `InstallRoot`, `CrlIconsPath`.

- [ ] **Step 1: Write failing tests for candidate validation and deduplication**

```csharp
[TestMethod]
public void ValidateCandidate_RequiresCorelDrwExe()
{
    using var temp = new TempDirectory();
    var detector = new CorelInstallDetector(new FakeRegistrySource(), new[] { temp.Path });
    Assert.IsFalse(detector.TryCreateInstallation(temp.Path, out _));
}

[TestMethod]
public void TryCreateInstallation_DetectsCrlIconsNextToProgram()
{
    using var temp = TestCorelLayout.Create("26.0.0.101", includeCrlIcons: true);
    var detector = new CorelInstallDetector(new FakeRegistrySource(), []);
    Assert.IsTrue(detector.TryCreateInstallation(temp.ProgramFolder, out var install));
    StringAssert.EndsWith(install!.CrlIconsPath!, "CrlIcons.dll");
}
```

- [ ] **Step 2: Verify failure**

```powershell
dotnet test tests/CDRIconExtractor.Windows.Tests/CDRIconExtractor.Windows.Tests.csproj --filter "ValidateCandidate|TryCreateInstallation"
```

- [ ] **Step 3: Implement detector with two sources**

Implementation rules:

```text
1. Registry uninstall views: HKLM 64-bit + 32-bit.
2. Known Corel product/install keys if present.
3. Program Files candidates under Corel/CorelDRAW Graphics Suite*.
4. Validate by locating CorelDRW.exe.
5. Read FileVersionInfo from CorelDRW.exe.
6. Deduplicate by normalized CorelDRW.exe full path.
7. Sort newest VersionMajor first.
```

`UiDefinitionLocator` searches only bounded paths:

```csharp
string[] relativeCandidates =
[
    @"Draw\UIConfig\DrawUI.xml",
    @"Programs64\Draw\UIConfig\DrawUI.xml",
    @"Programs\Draw\UIConfig\DrawUI.xml",
    @"UIConfig\DrawUI.xml"
];
```

If direct candidates fail, recurse at most 4 levels below the verified install root for `DrawUI.xml`, never scan the whole drive.

- [ ] **Step 4: Run Windows detection tests**

```powershell
dotnet test tests/CDRIconExtractor.Windows.Tests/CDRIconExtractor.Windows.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CDRIconExtractor.Windows tests/CDRIconExtractor.Windows.Tests
git commit -m "feat: detect installed CorelDRAW versions"
```

---

### Task 3: DrawUI parser and searchable command index

**Files:**
- Create: `src/CDRIconExtractor.Core/Parsing/DrawUiParser.cs`
- Create: `src/CDRIconExtractor.Core/Search/SearchIndex.cs`
- Create: `fixtures/DrawUi/modern.xml`
- Create: `fixtures/DrawUi/legacy.xml`
- Create: `fixtures/DrawUi/malformed-partial.xml`
- Test: `tests/CDRIconExtractor.Core.Tests/Parsing/DrawUiParserTests.cs`
- Test: `tests/CDRIconExtractor.Core.Tests/Search/SearchIndexTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<DrawUiCommand> DrawUiParser.Parse(string path)`.
- Produces: `IReadOnlyList<IconAssociation> SearchIndex.Filter(IEnumerable<IconAssociation>, string query)`.

- [ ] **Step 1: Write failing parser tests**

```csharp
[TestMethod]
public void Parse_ReadsGuidCaptionShortcutAndResourceHints()
{
    var commands = _parser.Parse(Fixture("modern.xml"));
    var curve = commands.Single(x => x.Caption == "Convert to Curves");

    Assert.AreEqual("Ctrl+Q", curve.Shortcut);
    Assert.AreEqual("{11111111-1111-1111-1111-111111111111}", curve.Guid);
    Assert.IsTrue(curve.ResourceHints.Any(x => x.Name == "bmpRow" && x.Value == "23"));
}

[TestMethod]
public void Parse_MissingAttributes_DoesNotAbortWholeDocument()
{
    var commands = _parser.Parse(Fixture("malformed-partial.xml"));
    Assert.IsTrue(commands.Count >= 1);
}
```

Fixture must contain literal synthetic data only; no Corel binary or copied official icon data.

- [ ] **Step 2: Verify parser tests fail**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj --filter DrawUiParserTests
```

- [ ] **Step 3: Implement tolerant XML extraction**

Use `XDocument.Load(path, LoadOptions.SetLineInfo)` and normalize case-insensitive attribute names:

```csharp
private static string? Attr(XElement e, params string[] names) =>
    e.Attributes()
     .FirstOrDefault(a => names.Contains(a.Name.LocalName, StringComparer.OrdinalIgnoreCase))
     ?.Value?.Trim();
```

Collect resource hint attributes whose local names match:

```text
bmpRow, bmpCol, image, imageGuid, icon, iconGuid, resource, resourceId
```

Build `XmlPath` from element ancestry and sibling index for diagnostics.

- [ ] **Step 4: Implement search matching**

Normalize query and candidate fields with `StringComparison.OrdinalIgnoreCase`; match:

```text
LocalizedCaption, Caption, Guid, GuidRef, Shortcut, resource hint value, asset resource id, source filename.
```

- [ ] **Step 5: Run parser/search tests and commit**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj
git add src/CDRIconExtractor.Core fixtures/DrawUi tests/CDRIconExtractor.Core.Tests
git commit -m "feat: parse DrawUI commands and search index"
```

---

### Task 4: `CrlIcons.dll` PNG stream extraction

**Files:**
- Create: `src/CDRIconExtractor.Core/Parsing/PngStreamScanner.cs`
- Create: `src/CDRIconExtractor.Windows/Resources/CrlIconsReader.cs`
- Create: `fixtures/CrlIcons/embedded-png-stream.bin`
- Test: `tests/CDRIconExtractor.Core.Tests/Parsing/PngStreamScannerTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<PngSlice> PngStreamScanner.Find(ReadOnlySpan<byte> bytes)` where `PngSlice` contains `Offset`, `Length`, `Width`, `Height`, `Sha256`.
- Produces: `Task<IReadOnlyList<IconAsset>> CrlIconsReader.ReadPngAssetsAsync(string crlIconsPath, CancellationToken token)`.

- [ ] **Step 1: Write a failing scanner test using synthetic PNG bytes**

```csharp
[TestMethod]
public void Find_ReturnsTwoPngsAndTheirDimensions()
{
    var bytes = File.ReadAllBytes(Fixture("embedded-png-stream.bin"));
    var slices = PngStreamScanner.Find(bytes);

    Assert.AreEqual(2, slices.Count);
    Assert.AreEqual(16, slices[0].Width);
    Assert.AreEqual(16, slices[0].Height);
    Assert.AreEqual(32, slices[1].Width);
}
```

The fixture is generated from two tiny original test PNGs created for this repository; it must not contain any Corel asset.

- [ ] **Step 2: Verify the test fails**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj --filter PngStreamScannerTests
```

- [ ] **Step 3: Implement a chunk-aware PNG scanner, not only a naive IEND byte search**

Algorithm:

```text
1. Scan for the 8-byte PNG signature.
2. From each signature, parse chunks as: length(4 big-endian), type(4), data(length), crc(4).
3. Read dimensions from IHDR.
4. Stop only on a structurally valid IEND chunk.
5. Reject chunk length > remaining bytes and continue looking for next signature.
6. Hash exact PNG bytes with SHA-256.
```

This intentionally improves on a simple signature/end-marker extraction and avoids false IEND matches inside arbitrary data.

- [ ] **Step 4: Implement `CrlIconsReader`**

Read the DLL with `FileStreamOptions { Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete, Options = FileOptions.SequentialScan }`; do not copy or modify the installed DLL. Each slice becomes:

```csharp
new IconAsset(
    crlIconsPath,
    "CrlIconsPng",
    (index + 1).ToString(CultureInfo.InvariantCulture),
    slice.Width,
    slice.Height,
    slice.Sha256,
    bytes[slice.Offset..(slice.Offset + slice.Length)].ToArray());
```

The sequential ID starts at `1` because the researched Corel icon extractor maps GUID resource IDs to PNG files using 1-based numbering.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj --filter PngStreamScannerTests
git add src fixtures/CrlIcons tests
git commit -m "feat: extract PNG assets from CrlIcons"
```

---

### Task 5: Read `CrlIcons.dll` RT_RCDATA and build GUID → icon ID map

**Files:**
- Create: `src/CDRIconExtractor.Windows/Resources/NativeMethods.cs`
- Create: `src/CDRIconExtractor.Windows/Resources/Win32ResourceReader.cs`
- Create: `src/CDRIconExtractor.Core/Parsing/CrlIconGuidMapParser.cs`
- Create: `fixtures/CrlIcons/guid-map-modern.bin`
- Create: `fixtures/CrlIcons/guid-map-legacy.bin`
- Test: `tests/CDRIconExtractor.Core.Tests/Parsing/CrlIconGuidMapParserTests.cs`
- Test: `tests/CDRIconExtractor.Windows.Tests/Resources/Win32ResourceReaderTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<Win32ResourceBlob> ReadResources(string modulePath, ushort typeId)`.
- Produces: `IReadOnlyDictionary<ushort, IReadOnlyList<string>> CrlIconGuidMapParser.Parse(IEnumerable<ReadOnlyMemory<byte>> blobs)`.

- [ ] **Step 1: Write failing parser tests for both known record layouts**

```csharp
[TestMethod]
public void Parse_Modern76ByteRecord_MapsGuidToUInt16IconId()
{
    var map = CrlIconGuidMapParser.Parse([File.ReadAllBytes(Fixture("guid-map-modern.bin"))]);
    CollectionAssert.Contains(map[42].ToList(), "11111111-1111-1111-1111-111111111111");
}

[TestMethod]
public void Parse_LegacyNullSeparatedRecords_MapsMultipleGuidsToSameId()
{
    var map = CrlIconGuidMapParser.Parse([File.ReadAllBytes(Fixture("guid-map-legacy.bin"))]);
    Assert.AreEqual(2, map[7].Count);
}
```

- [ ] **Step 2: Verify failure**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj --filter CrlIconGuidMapParserTests
```

- [ ] **Step 3: Implement safe Win32 resource loading**

Use `LoadLibraryExW` with `LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE`, then enumerate `RT_RCDATA = 10`. Required declarations:

```csharp
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
internal static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

[DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", CharSet = CharSet.Unicode, SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static extern bool EnumResourceNamesW(IntPtr hModule, IntPtr lpType, EnumResNameProc callback, IntPtr lParam);
```

For every resource name: `FindResourceExW` → `SizeofResource` → `LoadResource` → `LockResource` → `Marshal.Copy`. Always call `FreeLibrary` in `finally`.

- [ ] **Step 4: Implement the two GUID-map parsers defensively**

Rules based on independently reimplemented observed format:

```text
A. 76-byte record: bytes 2..73 are UTF-16LE 36-character GUID; bytes 74..75 are UInt16 icon id.
B. Legacy/variable record: split UTF-16LE segments on NUL; accept only strings whose first 36 chars parse as Guid and whose trailing UTF-16 code unit is a UInt16 id.
C. Ignore malformed entries; never throw because one record is bad.
D. Normalize GUIDs to lower-case "D" format without braces for internal matching.
```

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj
dotnet test tests/CDRIconExtractor.Windows.Tests/CDRIconExtractor.Windows.Tests.csproj
git add src fixtures/CrlIcons tests
git commit -m "feat: map Corel command GUIDs to CrlIcons ids"
```

---

### Task 6: Generic PE resource fallback and association engine

**Files:**
- Create: `src/CDRIconExtractor.Windows/Resources/GenericPeIconScanner.cs`
- Create: `src/CDRIconExtractor.Core/Association/IconAssociationEngine.cs`
- Test: `tests/CDRIconExtractor.Core.Tests/Association/IconAssociationEngineTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<IconAsset> GenericPeIconScanner.Scan(string path, CancellationToken token)` for RT_ICON, RT_GROUP_ICON, RT_BITMAP and PNG-like RCDATA.
- Produces: `IReadOnlyList<IconAssociation> Associate(commands, assets, guidMap)`.

- [ ] **Step 1: Write failing confidence tests**

```csharp
[TestMethod]
public void Associate_GuidMapMatch_IsExact()
{
    var result = _engine.Associate([Command(Guid1)], [Asset(id: "42")], Map((42, Guid1)));
    Assert.AreEqual(AssociationConfidence.Exact, result.Single().Confidence);
}

[TestMethod]
public void Associate_NoReliableRule_IsUnmapped()
{
    var result = _engine.Associate([Command(Guid1)], [Asset(id: "99")], EmptyMap());
    Assert.AreEqual(AssociationConfidence.Unmapped, result.Single().Confidence);
    Assert.IsNull(result.Single().Asset);
}
```

- [ ] **Step 2: Verify failure**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj --filter IconAssociationEngineTests
```

- [ ] **Step 3: Implement association precedence**

Exact ordering:

```text
1. CrlIcons GUID map: normalized command Guid/GuidRef appears under icon id and exactly one PNG asset has that id => Exact.
2. Explicit numeric resourceId/icon attribute uniquely identifies a scanned PE asset => Exact.
3. Explicit imageGuid/iconGuid resolves through another DrawUI item whose GUID has Exact mapping => Strong.
4. bmpRow/bmpCol with a documented per-version mapper (none in V1 initially) => Heuristic only.
5. Anything else => Unmapped.
```

Never assign the “closest” icon just to reduce unmapped count.

- [ ] **Step 4: Implement generic scanner as fallback only**

The generic scanner must not override a `CrlIcons` Exact result. Preserve all decoded sizes as separate `IconAsset` records grouped by source/group ID. PNG RCDATA is accepted only if the blob starts with PNG signature or contains a structurally valid PNG found by `PngStreamScanner`.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj
git add src tests
git commit -m "feat: associate Corel commands with official icon resources"
```

---

### Task 7: Export service, safe filenames, reports, and offline logging

**Files:**
- Create: `src/CDRIconExtractor.Core/Utilities/FileNameSanitizer.cs`
- Create: `src/CDRIconExtractor.Core/Export/ExportService.cs`
- Create: `src/CDRIconExtractor.App/Services/AppLogger.cs`
- Test: `tests/CDRIconExtractor.Core.Tests/Export/ExportServiceTests.cs`

**Interfaces:**
- Produces: `Task<ExportSummary> ExportAsync(IEnumerable<IconAssociation> items, string preferredRoot, string corelVersion, CancellationToken token)`.

- [ ] **Step 1: Write failing export tests**

```csharp
[TestMethod]
public async Task ExportAsync_WritesPngJsonCsvAndReportWithoutOverwrite()
{
    using var temp = new TempDirectory();
    var summary = await _service.ExportAsync([ExactAssociation()], temp.Path, "2026", CancellationToken.None);

    Assert.IsTrue(File.Exists(Path.Combine(summary.OutputRoot, "icon_index.csv")));
    Assert.IsTrue(File.Exists(Path.Combine(summary.OutputRoot, "icon_index.json")));
    Assert.IsTrue(File.Exists(Path.Combine(summary.OutputRoot, "extraction_report.txt")));
    Assert.AreEqual(1, Directory.GetFiles(Path.Combine(summary.OutputRoot, "Icons"), "*.png", SearchOption.AllDirectories).Length);
}
```

- [ ] **Step 2: Verify failure**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj --filter ExportServiceTests
```

- [ ] **Step 3: Implement export layout and CSV escaping**

CSV columns exactly:

```text
LocalizedCaption,Caption,Shortcut,Guid,GuidRef,Confidence,Reason,ResourceId,Width,Height,Sha256,SourceFile,ExportedFile
```

JSON uses indented UTF-8. PNG bytes are written exactly from `IconAsset.PngBytes`; never resample before export.

Output directory fallback:

```csharp
var first = Path.Combine(preferredRoot, "CDR_Icons_Output", $"CorelDRAW_{corelVersion}");
var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CDR_Icons_Output", $"CorelDRAW_{corelVersion}");
```

- [ ] **Step 4: Implement local logger**

Log root:

```csharp
Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
             "Beiluoguo", "CDRIconExtractor", "Logs")
```

One UTF-8 log per day. Log phase timings and file errors only; do not log file contents and do not implement any HTTP client/telemetry.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/CDRIconExtractor.Core.Tests/CDRIconExtractor.Core.Tests.csproj
git add src tests
git commit -m "feat: export extracted icons and reports"
```

---

### Task 8: Scan coordinator with cancellation and progressive results

**Files:**
- Create: `src/CDRIconExtractor.App/Services/ScanCoordinator.cs`
- Create: `src/CDRIconExtractor.Core/Models/ScanResult.cs`
- Test: `tests/CDRIconExtractor.Core.Tests/Models/ScanResultTests.cs`

**Interfaces:**
- Produces: `Task<ScanResult> ScanAsync(CorelInstallation installation, bool deepScan, IProgress<ScanProgress> progress, CancellationToken token)`.

- [ ] **Step 1: Add failing scan-result state tests**

```csharp
[TestMethod]
public void ScanResult_Cancelled_PreservesPartialItems()
{
    var result = ScanResult.Cancelled([ExactAssociation()], scannedFiles: 1, elapsed: TimeSpan.FromSeconds(2));
    Assert.IsTrue(result.IsCancelled);
    Assert.AreEqual(1, result.Associations.Count);
}
```

- [ ] **Step 2: Implement phase pipeline**

Phases and progress weights:

```text
Detect/validate paths   5%
Locate DrawUI          10%
Parse DrawUI           20%
Read CrlIcons PNGs     45%
Read CrlIcons GUID map 60%
Fallback PE scan       80%
Associate/search index 95%
Finalize              100%
```

Use `Task.Run` only for file/CPU scanning; UI objects must never be created inside `ScanCoordinator`. Call `token.ThrowIfCancellationRequested()` between files and inside long binary loops.

- [ ] **Step 3: Implement error isolation**

Each file failure becomes `ScanDiagnostic` with severity/path/message and scanning continues. Missing DrawUI produces resource-only mode. Missing `CrlIcons.dll` skips to generic fallback with an explicit diagnostic.

- [ ] **Step 4: Verify no background phase touches CorelDRAW COM or starts CorelDRAW**

Run repository check:

```powershell
Get-ChildItem src -Recurse -Filter *.cs | Select-String -Pattern 'VGCore|Corel\.Interop|Process\.Start\(.+CorelDRW'
```

Expected: no matches.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: orchestrate cancellable icon scans"
```

---

### Task 9: WPF main UI, list/icon-wall views, details, branding, and about dialog

**Files:**
- Create: `src/CDRIconExtractor.App/App.xaml`
- Create: `src/CDRIconExtractor.App/App.xaml.cs`
- Create: `src/CDRIconExtractor.App/MainWindow.xaml`
- Create: `src/CDRIconExtractor.App/MainWindow.xaml.cs`
- Create: `src/CDRIconExtractor.App/AboutWindow.xaml`
- Create: `src/CDRIconExtractor.App/ViewModels/MainViewModel.cs`
- Create: `src/CDRIconExtractor.App/ViewModels/IconItemViewModel.cs`
- Create: `src/CDRIconExtractor.App/Infrastructure/ObservableObject.cs`
- Create: `src/CDRIconExtractor.App/Infrastructure/RelayCommand.cs`
- Create: `src/CDRIconExtractor.App/Infrastructure/AsyncRelayCommand.cs`
- Create: `src/CDRIconExtractor.App/Services/PreviewImageService.cs`

**Interfaces:**
- Consumes: detector, scan coordinator, search index, export service.
- Produces: final end-user workflow without requiring CorelDRAW to be running.

- [ ] **Step 1: Implement minimal MVVM infrastructure without external MVVM packages**

`ObservableObject` implements `INotifyPropertyChanged`; `AsyncRelayCommand` prevents double execution and exposes `CanExecute` while running.

- [ ] **Step 2: Build the top-level layout exactly around the approved controls**

Required visible strings:

```text
CDR 官方图标提取器
北落果制作
CorelDRAW版本
刷新版本
扫描官方图标
全部 / 已关联 / 未关联 / 图标资源
列表 / 图标墙
导出当前
批量导出
打开输出目录
```

Window minimum size: `980x640`; default: `1180x760`. Use standard WPF controls, no WebView2.

- [ ] **Step 3: Implement list and icon-wall views bound to the same collection**

List columns:

```text
Preview | LocalizedCaption | Caption | Shortcut | Guid | Confidence
```

Icon wall uses `ItemsControl` + `WrapPanel`; each tile contains a lazily decoded thumbnail and short caption. Switching views must not rerun scanning.

- [ ] **Step 4: Implement details and preview behavior**

Preview uses `BitmapImage` with `CacheOption=OnLoad`, `Freeze()` before returning from background-safe cache. Detail panel shows:

```text
名称 / 英文名称 / GUID / GuidRef / 快捷键 / 关联状态 / 原因 /
来源文件 / 资源ID / 原始尺寸 / SHA-256
```

- [ ] **Step 5: Implement scan/cancel/search/export commands**

While scanning:

```text
扫描官方图标 -> 取消扫描
```

Cancellation retains current results and status text says `扫描已取消（保留已完成结果）`.

If no CorelDRAW is found, show a file picker restricted to `CorelDRW.exe`; validate before adding a temporary installation entry.

- [ ] **Step 6: Add About window**

Exact copy:

```text
CDR 官方图标提取器
Version 1.0
制作：北落果

本工具仅从用户本机已安装的 CorelDRAW
程序资源中读取并导出图标。
```

- [ ] **Step 7: Build and manually smoke-test the UI on Windows**

```powershell
dotnet build src/CDRIconExtractor.App/CDRIconExtractor.App.csproj -c Release
```

Expected: 0 warnings, 0 errors; app starts without CorelDRAW running.

- [ ] **Step 8: Commit**

```bash
git add src/CDRIconExtractor.App
git commit -m "feat: add CDR icon extractor desktop UI"
```

---

### Task 10: Single-file release script, README, and Windows acceptance package

**Files:**
- Create: `scripts/publish-win-x64.ps1`
- Create: `README.md`
- Modify: `src/CDRIconExtractor.App/CDRIconExtractor.App.csproj`
- Create: `docs/WINDOWS_ACCEPTANCE.md`

**Interfaces:**
- Produces: `artifacts/win-x64/CDR官方图标提取器.exe` as the only required runtime file.

- [ ] **Step 1: Create deterministic publish script**

`scripts/publish-win-x64.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\win-x64'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

dotnet test (Join-Path $root 'CDRIconExtractor.sln') -c Release

dotnet publish (Join-Path $root 'src\CDRIconExtractor.App\CDRIconExtractor.App.csproj') `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $out

$files = Get-ChildItem $out -File
if ($files.Count -ne 1 -or $files[0].Extension -ne '.exe') {
    throw "Expected exactly one EXE, got: $($files.Name -join ', ')"
}
Write-Host "Published: $($files[0].FullName)"
```

- [ ] **Step 2: Document usage and legal/technical boundary**

README must state:

```text
- 工具不附带 CorelDRAW 官方图标。
- 图标只从用户本机安装读取。
- 不修改 CorelDRAW。
- 不需要启动 CorelDRAW。
- 输出图标的使用仍受原资源权利人的许可范围约束。
```

- [ ] **Step 3: Run release build on Windows**

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

Expected: exactly one file under `artifacts/win-x64/`: `CDR官方图标提取器.exe`.

- [ ] **Step 4: Perform three-version real-machine acceptance**

Use `docs/WINDOWS_ACCEPTANCE.md` checklist for:

```text
A. 老版本：X8 或 2019
B. 中间版本：任一可用版本
C. CorelDRAW 2026
```

For each installed version verify:

```text
[ ] 自动检测版本与 CorelDRW.exe 路径
[ ] 扫描期间 CorelDRAW 未被启动
[ ] CrlIcons.dll 存在时优先使用 CrlIcons reader
[ ] 搜索“转换为曲线”或 “Convert to Curves” 或 Ctrl+Q
[ ] 搜索 Import / Ctrl+I
[ ] 搜索 Export / Ctrl+E
[ ] Exact 对应图标与 CDR 菜单/工具栏视觉一致
[ ] 无可靠映射时显示 Heuristic/Unmapped，而非错误 Exact
[ ] 导出的 PNG 保持原始像素尺寸
[ ] 批量导出生成 CSV + JSON + report
[ ] CorelDRAW 安装目录文件时间戳未变化
```

- [ ] **Step 5: Inspect logs and performance timings**

Required phase timing names:

```text
InstallDetectionMs
UiLocateMs
DrawUiParseMs
CrlIconsPngScanMs
CrlIconsGuidMapMs
FallbackPeScanMs
AssociationMs
SearchIndexMs
TotalMs
```

If ordinary scan exceeds 30 seconds, retain correctness and record the bottleneck; do not add unsafe parallel reads until the measured phase is known.

- [ ] **Step 6: Commit the V1 release preparation**

```bash
git add scripts README.md docs src/CDRIconExtractor.App/CDRIconExtractor.App.csproj
git commit -m "release: prepare CDR icon extractor v1"
```

---

## Final Verification Gate

Before claiming V1 complete, run all of the following on Windows:

```powershell
dotnet test .\CDRIconExtractor.sln -c Release
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
Get-ChildItem .\artifacts\win-x64 -File | Format-Table Name,Length
```

Then verify the published EXE on a machine where .NET 8 Desktop Runtime is not preinstalled, because the deliverable is required to be self-contained. Confirm the tool launches by double-clicking the EXE, shows `北落果制作`, detects at least one installed CorelDRAW, and does not require administrator elevation.

## Research Notes Incorporated Into This Plan

- Microsoft documents `PublishSingleFile=true` for single-file deployment, and native runtime files can be bundled with `IncludeNativeLibrariesForSelfExtract=true`; .NET 8 no longer makes RID builds self-contained implicitly, so `SelfContained=true` remains explicit.
- Microsoft documents `LoadLibraryEx` data-file loading and `EnumResourceNames` for enumerating binary resources without executing the target DLL.
- The public Bonus630DevToolsBar source confirms its DrawUIExplorer icon path specifically reads `CrlIcons.dll`, extracts embedded PNG sequences, reads `RT_RCDATA`, and maps command GUIDs to numeric icon IDs. This plan independently reimplements that file-format behavior and does not ship or copy that project's binaries.
