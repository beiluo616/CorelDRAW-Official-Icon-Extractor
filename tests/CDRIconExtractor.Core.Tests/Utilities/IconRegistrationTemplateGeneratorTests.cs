using CDRIconExtractor.Core.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Utilities;

[TestClass]
public sealed class IconRegistrationTemplateGeneratorTests
{
    private const string IconGuid = "22222222-2222-2222-2222-222222222222";

    [TestMethod]
    public void GenerateVba_UsesGuidUriWithSetIcon2()
    {
        var text = IconRegistrationTemplateGenerator.GenerateVba(IconGuid, "MyMacro.MyModule.MyCommand", "我的功能");

        StringAssert.Contains(text, "SetIcon2 \"guid://22222222-2222-2222-2222-222222222222\"");
        StringAssert.Contains(text, "MyMacro.MyModule.MyCommand");
    }

    [TestMethod]
    public void GenerateVba_IncludesOfficialResourceProvenanceWhenProvided()
    {
        var text = IconRegistrationTemplateGenerator.GenerateVba(
            IconGuid,
            "MyMacro.MyModule.MyCommand",
            "我的功能",
            "Common/Res/CrlCmnRes/RCBin/Toolbar/TB_Ungroup.ico.png",
            "icons.map.xml + Modern.crlicons");

        StringAssert.Contains(text, "图标资源: Common/Res/CrlCmnRes/RCBin/Toolbar/TB_Ungroup.ico.png");
        StringAssert.Contains(text, "GUID 来源: icons.map.xml + Modern.crlicons");
    }

    [TestMethod]
    public void GenerateCpp_UsesGuidUriWithSetIcon2()
    {
        var text = IconRegistrationTemplateGenerator.GenerateCpp(IconGuid, "MyCommand", "我的功能");

        StringAssert.Contains(text, "SetIcon2");
        StringAssert.Contains(text, "guid://22222222-2222-2222-2222-222222222222");
        StringAssert.Contains(text, "AddCustomButton");
    }
}

[TestClass]
public sealed class IconRegistrationBatchTemplateGeneratorTests
{
    [TestMethod]
    public void GenerateVbaBatch_CreatesOneControlAndIconAssignmentPerItem()
    {
        var items = new[]
        {
            new IconRegistrationTemplateItem("11111111-1111-1111-1111-111111111111", "MyMacro.Module.Command1", "功能1"),
            new IconRegistrationTemplateItem("22222222-2222-2222-2222-222222222222", "MyMacro.Module.Command2", "功能2")
        };

        var text = IconRegistrationTemplateGenerator.GenerateVbaBatch(items, "F10AI Tools");

        StringAssert.Contains(text, "Dim ctl1 As CommandBarControl");
        StringAssert.Contains(text, "Dim ctl2 As CommandBarControl");
        StringAssert.Contains(text, "ctl1.SetIcon2 \"guid://11111111-1111-1111-1111-111111111111\"");
        StringAssert.Contains(text, "ctl2.SetIcon2 \"guid://22222222-2222-2222-2222-222222222222\"");
    }

    [TestMethod]
    public void GenerateCppBatch_CreatesOneControlAndIconAssignmentPerItem()
    {
        var items = new[]
        {
            new IconRegistrationTemplateItem("11111111-1111-1111-1111-111111111111", "Command1", "功能1"),
            new IconRegistrationTemplateItem("22222222-2222-2222-2222-222222222222", "Command2", "功能2")
        };

        var text = IconRegistrationTemplateGenerator.GenerateCppBatch(items, "F10AI Tools");

        StringAssert.Contains(text, "ctl1->SetIcon2");
        StringAssert.Contains(text, "ctl2->SetIcon2");
        StringAssert.Contains(text, "guid://11111111-1111-1111-1111-111111111111");
        StringAssert.Contains(text, "guid://22222222-2222-2222-2222-222222222222");
    }
}
