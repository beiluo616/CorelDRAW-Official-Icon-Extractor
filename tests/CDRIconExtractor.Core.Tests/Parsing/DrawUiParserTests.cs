using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class DrawUiParserTests
{
    private readonly DrawUiParser _parser = new();

    [TestMethod]
    public void Parse_ReadsGuidCaptionShortcutAndResourceHints()
    {
        var commands = _parser.Parse(Fixture("modern.xml"));
        var curve = commands.Single(x => x.Caption == "Convert to Curves");

        Assert.AreEqual("Ctrl+Q", curve.Shortcut);
        Assert.AreEqual("{11111111-1111-1111-1111-111111111111}", curve.Guid);
        Assert.IsTrue(curve.ResourceHints.Any(x => x.Name == "bmpRow" && x.Value == "23"));
        Assert.AreEqual("转换为曲线", curve.LocalizedCaption);
    }

    [TestMethod]
    public void Parse_MissingAttributes_DoesNotAbortWholeDocument()
    {
        var commands = _parser.Parse(Fixture("malformed-partial.xml"));
        Assert.IsTrue(commands.Count >= 1);
    }

    [TestMethod]
    public void Parse_FindsCaptionResourceExpressionOutsideKnownCaptionAttributes()
    {
        var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "DrawUI.xml");
        File.WriteAllText(file,
            "<ui><itemData guid='{11111111-1111-1111-1111-111111111111}' titleSource=\"*CT('{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}')\" /></ui>");
        try
        {
            var command = _parser.Parse(file).Single();
            Assert.AreEqual("*CT('{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}')", command.Caption);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "DrawUi", name);

    [TestMethod]
    public void Parse_ResourceEntryIdGuid_IsCapturedAsGuid()
    {
        var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "resources.xml");
        File.WriteAllText(path, "<ui><resEntry id=\"{22222222-2222-2222-2222-222222222222}\" icon=\"77\" /></ui>");
        try
        {
            var item = new DrawUiParser().Parse(path).Single();
            Assert.AreEqual("{22222222-2222-2222-2222-222222222222}", item.Guid);
            Assert.IsTrue(item.ResourceHints.Any(x => x.Name == "icon" && x.Value == "77"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
