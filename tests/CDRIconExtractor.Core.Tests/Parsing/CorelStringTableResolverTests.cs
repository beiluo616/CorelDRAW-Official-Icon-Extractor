using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class CorelStringTableResolverTests
{
    [TestMethod]
    public void Enrich_MapsDrawUiGuidToEnglishAndChineseCaptions()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "Strings");
        var map = Path.Combine(root, "Programs64", "strings.map.xml");
        var languageFiles = Directory.EnumerateFiles(root, "strings.xml", SearchOption.AllDirectories).ToArray();
        var command = new DrawUiCommand(
            "{11111111-1111-1111-1111-111111111111}", null, null, null, null,
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData");

        var resolver = new CorelStringTableResolver();
        var enriched = resolver.Enrich(new[] { command }, map, languageFiles).Single();

        Assert.AreEqual("Convert to Curves", enriched.Caption);
        Assert.AreEqual("转换为曲线", enriched.LocalizedCaption);
    }

    [TestMethod]
    public void Enrich_MapsNestedStringMapAndExtractsChineseCaptionToken()
    {
        var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Programs"));
        Directory.CreateDirectory(Path.Combine(root, "Languages", "CS", "Data"));
        var map = Path.Combine(root, "Programs", "strings.map.xml");
        var strings = Path.Combine(root, "Languages", "CS", "Data", "strings.xml");
        File.WriteAllText(map,
            "<root><entry id='{11111111-1111-1111-1111-111111111111}'><resource guid='{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}'/></entry></root>");
        File.WriteAllText(strings,
            "<strings><string guid='{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}' value='CT=转换为曲线|TT=将对象转换为曲线'/></strings>");
        try
        {
            var command = new DrawUiCommand(
                "{11111111-1111-1111-1111-111111111111}", null, null, null, null,
                "itemData", Array.Empty<ResourceHint>(), "/ui/itemData");

            var enriched = new CorelStringTableResolver().Enrich(new[] { command }, map, new[] { strings }).Single();

            Assert.AreEqual("转换为曲线", enriched.LocalizedCaption);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }


        [TestMethod]
        public void Enrich_ResolvesNestedTextElementFromNearestGuidOwner()
        {
            var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Languages", "CS", "Data"));
            var strings = Path.Combine(root, "Languages", "CS", "Data", "strings.xml");
            File.WriteAllText(strings,
                "<strings><string guid='{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}'><text>转换为曲线</text></string></strings>");
            try
            {
                var command = new DrawUiCommand(
                    "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", null, null, null, null,
                    "itemData", Array.Empty<ResourceHint>(), "/ui/itemData");

                var enriched = new CorelStringTableResolver().Enrich(new[] { command }, null, new[] { strings }).Single();

                Assert.AreEqual("转换为曲线", enriched.LocalizedCaption);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [TestMethod]
        public void Enrich_ResolvesDirectCtGuidExpressionAndReplacesExpressionCaption()
        {
            var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Languages", "CS", "Data"));
            var strings = Path.Combine(root, "Languages", "CS", "Data", "strings.xml");
            File.WriteAllText(strings,
                "<strings><entry guid='{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}' value='转换为曲线'/></strings>");
            try
            {
                var command = new DrawUiCommand(
                    "{11111111-1111-1111-1111-111111111111}", null,
                    "*CT('{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}')", null, null,
                    "itemData", Array.Empty<ResourceHint>(), "/ui/itemData");

                var enriched = new CorelStringTableResolver().Enrich(new[] { command }, null, new[] { strings }).Single();

                Assert.AreEqual("转换为曲线", enriched.LocalizedCaption);
                Assert.AreEqual("转换为曲线", enriched.Caption);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    [TestMethod]
    public void Enrich_DetectsChineseLanguageCodeAboveNestedDataFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var guid = "11111111-2222-3333-4444-555555555555";
            var csData = Path.Combine(root, "Languages", "CS", "Programs64", "Data");
            Directory.CreateDirectory(csData);
            var strings = Path.Combine(csData, "strings.xml");
            File.WriteAllText(strings, $"<root><string guid=\"{{{guid}}}\" value=\"转换为曲线\" /></root>");
            var command = new DrawUiCommand(guid, null, null, null, null, "item", Array.Empty<ResourceHint>(), "/item");

            var result = new CorelStringTableResolver().Enrich(new[] { command }, null, new[] { strings });

            Assert.AreEqual("转换为曲线", result[0].LocalizedCaption);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

}
