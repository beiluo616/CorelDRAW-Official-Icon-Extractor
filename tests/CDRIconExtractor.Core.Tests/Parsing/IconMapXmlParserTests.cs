using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class IconMapXmlParserTests
{
    [TestMethod]
    public void Parse_ReadsCanonicalGuidAndResourcePath()
    {
        var path = Fixture("IconMap", "icons.map.synthetic.xml");

        var entries = IconMapXmlParser.Parse(path);

        var ungroup = entries.Single(x => x.Guid == "11111111-2222-3333-4444-555555555555");
        Assert.AreEqual("Common/Res/CrlCmnRes/RCBin/Toolbar/TB_Ungroup.ico.png", ungroup.ResourcePath);
        Assert.IsTrue(ungroup.IsReusableGuid);
    }

    [TestMethod]
    public void Parse_PreservesNonCanonicalKeysButDoesNotMarkThemReusable()
    {
        var entries = IconMapXmlParser.Parse(Fixture("IconMap", "icons.map.synthetic.xml"));

        var special = entries.Single(x => x.RawGuid.EndsWith("_clockwise", StringComparison.Ordinal));
        Assert.IsFalse(special.IsReusableGuid);
        Assert.IsNull(special.Guid);
    }

    private static string Fixture(params string[] parts) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", Path.Combine(parts)));
}
