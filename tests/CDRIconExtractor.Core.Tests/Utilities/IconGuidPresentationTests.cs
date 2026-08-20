using CDRIconExtractor.Core.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Utilities;

[TestClass]
public sealed class IconGuidPresentationTests
{
    [TestMethod]
    public void Create_SameCommandAndIconGuid_UsesSingleSharedGuid()
    {
        const string guid = "11111111-1111-1111-1111-111111111111";
        var result = IconGuidPresentation.Create(guid, guid);

        Assert.IsTrue(result.ShowCombined);
        Assert.IsFalse(result.ShowSeparate);
        Assert.AreEqual(guid, result.PrimaryGuid);
        Assert.AreEqual("GUID（命令/图标共用）", result.PrimaryLabel);
    }

    [TestMethod]
    public void Create_DifferentGuids_ShowsSeparateValues()
    {
        const string commandGuid = "11111111-1111-1111-1111-111111111111";
        const string iconGuid = "22222222-2222-2222-2222-222222222222";
        var result = IconGuidPresentation.Create(commandGuid, iconGuid);

        Assert.IsFalse(result.ShowCombined);
        Assert.IsTrue(result.ShowSeparate);
        Assert.AreEqual(commandGuid, result.CommandGuid);
        Assert.AreEqual(iconGuid, result.IconGuid);
    }

    [TestMethod]
    public void Create_OnlyIconGuid_UsesSingleIconGuid()
    {
        const string iconGuid = "22222222-2222-2222-2222-222222222222";
        var result = IconGuidPresentation.Create(null, iconGuid);

        Assert.IsTrue(result.ShowCombined);
        Assert.AreEqual(iconGuid, result.PrimaryGuid);
        Assert.AreEqual("图标 GUID", result.PrimaryLabel);
    }
}
