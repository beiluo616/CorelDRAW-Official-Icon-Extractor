using CDRIconExtractor.Core.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Utilities;

[TestClass]
public sealed class IconGuidReferenceTests
{
    [TestMethod]
    public void Normalize_AcceptsGuidUriAndBraces()
    {
        var normalized = IconGuidReference.Normalize("guid://{496ea244-5a15-4d15-bb7c-4cb1ee27db96}");
        Assert.AreEqual("496ea244-5a15-4d15-bb7c-4cb1ee27db96", normalized);
    }

    [TestMethod]
    public void FormatIconAttribute_UsesCorelGuidUriSyntax()
    {
        var value = IconGuidReference.FormatIconAttribute("496EA244-5A15-4D15-BB7C-4CB1EE27DB96");
        Assert.AreEqual("icon=\"guid://496ea244-5a15-4d15-bb7c-4cb1ee27db96\"", value);
    }
}
