using CDRIconExtractor.Core.Export;
using CDRIconExtractor.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Export;

[TestClass]
public sealed class ExportServiceTests
{
    private readonly ExportService _service = new();

    [TestMethod]
    public async Task ExportAsync_WritesPngJsonCsvAndReportWithoutOverwrite()
    {
        using var temp = new TempDirectory();
        var summary = await _service.ExportAsync(new[] { ExactAssociation() }, temp.Path, "2026", CancellationToken.None);

        Assert.IsTrue(File.Exists(Path.Combine(summary.OutputRoot, "icon_index.csv")));
        Assert.IsTrue(File.Exists(Path.Combine(summary.OutputRoot, "icon_index.json")));
        Assert.IsTrue(File.Exists(Path.Combine(summary.OutputRoot, "extraction_report.txt")));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(summary.OutputRoot, "Icons"), "*.png", SearchOption.AllDirectories).Length);

        var summary2 = await _service.ExportAsync(new[] { ExactAssociation() }, temp.Path, "2026", CancellationToken.None);
        Assert.AreNotEqual(summary.OutputRoot, summary2.OutputRoot);
    }

    [TestMethod]
    public async Task ExportAsync_AllowsNullableMetadataFieldsInCsv()
    {
        using var temp = new TempDirectory();
        var command = new DrawUiCommand(
            null, null, null, null, null,
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData[1]");
        var association = new IconAssociation(command, null, AssociationConfidence.Unmapped, "nullable metadata");

        var summary = await _service.ExportAsync([association], temp.Path, "2026", CancellationToken.None);
        var csv = await File.ReadAllTextAsync(Path.Combine(summary.OutputRoot, "icon_index.csv"));

        StringAssert.Contains(csv, "Unmapped");
        StringAssert.Contains(csv, "nullable metadata");
    }

    [TestMethod]
    public void FileNameSanitizer_ReplacesInvalidCharactersAndReservedNames()
    {
        Assert.AreEqual("A_B_C", CDRIconExtractor.Core.Utilities.FileNameSanitizer.Sanitize("A/B:C"));
        Assert.AreEqual("_CON", CDRIconExtractor.Core.Utilities.FileNameSanitizer.Sanitize("CON"));
    }

    private static IconAssociation ExactAssociation()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "42", 16, 16, "abc", png);
        var command = new DrawUiCommand(
            "{11111111-1111-1111-1111-111111111111}", null, "Convert to Curves", "转换为曲线", "Ctrl+Q",
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData[1]");
        return new IconAssociation(command, asset, AssociationConfidence.Exact, "GUID map id=42");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
    [TestMethod]
    public async Task ExportAsync_IncludesIconGuidInCsvIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var command = new DrawUiCommand(
                "11111111-1111-1111-1111-111111111111", null, "Command", "命令", null,
                "itemData", Array.Empty<ResourceHint>(), "/ui/itemData");
            var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "42", 16, 16, new string('a', 64), new byte[] { 1, 2, 3 });
            var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "test", "22222222-2222-2222-2222-222222222222");

            var summary = await _service.ExportAsync(new[] { association }, root, "X8", CancellationToken.None);
            var csv = await File.ReadAllTextAsync(Path.Combine(summary.OutputRoot, "icon_index.csv"));

            StringAssert.Contains(csv, "IconGuid");
            StringAssert.Contains(csv, "22222222-2222-2222-2222-222222222222");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

}

// V1.12 export naming regression tests are kept in a separate partial-style class to avoid
// changing the existing test fixture layout.
[TestClass]
public sealed class ExportServiceIdNamingTests
{
    [TestMethod]
    public async Task ExportAsync_UsesOnlyResourceIdForPngFileName()
    {
        using var temp = new TempDirectory();
        var service = new ExportService();
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "693", 256, 256, "sha-693", [1, 2, 3]);
        var command = new DrawUiCommand(
            "646f726a-e6d3-eea4-49ba-db1e5f30caa4", null, "Pick Tool", "选择工具", null,
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData");
        var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "test");

        var summary = await service.ExportAsync([association], temp.Path, "X8", CancellationToken.None);
        var files = Directory.GetFiles(Path.Combine(summary.OutputRoot, "Icons"), "*.png");

        Assert.AreEqual(1, files.Length);
        Assert.AreEqual("693.png", Path.GetFileName(files[0]));
    }

    [TestMethod]
    public async Task ExportAsync_DoesNotWriteDuplicatePngForSameResourceImage()
    {
        using var temp = new TempDirectory();
        var service = new ExportService();
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "25", 256, 256, "same-sha", [1, 2, 3]);
        var first = new IconAssociation(
            new DrawUiCommand("11111111-1111-1111-1111-111111111111", null, "A", "A", null, "itemData", [], "/a"),
            asset, AssociationConfidence.Exact, "first");
        var second = new IconAssociation(
            new DrawUiCommand("22222222-2222-2222-2222-222222222222", null, "B", "B", null, "itemData", [], "/b"),
            asset, AssociationConfidence.Exact, "second");

        var summary = await service.ExportAsync([first, second], temp.Path, "X8", CancellationToken.None);
        var files = Directory.GetFiles(Path.Combine(summary.OutputRoot, "Icons"), "*.png");

        Assert.AreEqual(1, files.Length);
        Assert.AreEqual("25.png", Path.GetFileName(files[0]));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
