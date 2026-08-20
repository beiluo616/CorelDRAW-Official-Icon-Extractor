using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class ModernIconMapBinderTests
{
    [TestMethod]
    public void Bind_AssignsAllReusableGuidsForSameModernResource()
    {
        var asset = Asset("Common/Res/CrlCmnRes/RCBin/Toolbar/TB_Ungroup.ico.png");
        var entries = IconMapXmlParser.Parse(Fixture("IconMap", "icons.map.synthetic.xml"));

        var result = ModernIconMapBinder.Bind([asset], entries, "C:/Corel/Data/Icons/icons.map.xml");

        Assert.AreEqual(1, result.MatchedResourceCount);
        Assert.AreEqual(2, result.Assets[0].IconGuids.Count);
        CollectionAssert.Contains(result.Assets[0].IconGuids.ToArray(), "11111111-2222-3333-4444-555555555555");
        CollectionAssert.Contains(result.Assets[0].IconGuids.ToArray(), "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Assert.AreEqual("C:/Corel/Data/Icons/icons.map.xml", result.Assets[0].IconGuidSource);
    }

    [TestMethod]
    public void Bind_RepairsOnlyKnownDoublePngSuffixQuirk()
    {
        var asset = Asset("Common/Res/CrlCmnRes/RCBin/missing.ico.png");
        var entries = IconMapXmlParser.Parse(Fixture("IconMap", "icons.map.synthetic.xml"));

        var result = ModernIconMapBinder.Bind([asset], entries, "icons.map.xml");

        Assert.AreEqual(1, result.MatchedResourceCount);
        Assert.AreEqual("bbbbbbbb-cccc-dddd-eeee-ffffffffffff", result.Assets[0].IconGuids.Single());
    }

    private static IconAsset Asset(string resourcePath) => new(
        "Modern.crlicons", "ModernCrlIcons", Path.GetFileNameWithoutExtension(resourcePath), 72, 72, "sha", [1, 2, 3])
    {
        ResourcePath = resourcePath,
        DisplayName = Path.GetFileNameWithoutExtension(resourcePath)
    };

    private static string Fixture(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", Path.Combine(parts)));
}
