using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Windows.Detection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Windows.Tests.Detection;

[TestClass]
public sealed class UiDefinitionLocatorTests
{
    [TestMethod]
    public void Locate_FindsBoundedDrawUiCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var uiDir = Path.Combine(root, "Draw", "UIConfig");
            Directory.CreateDirectory(uiDir);
            var drawUi = Path.Combine(uiDir, "DrawUI.xml");
            File.WriteAllText(drawUi, "<ui />");
            var program = Path.Combine(root, "CorelDRW.exe");
            File.WriteAllBytes(program, new byte[] { 0x4D, 0x5A });
            var install = new CorelInstallation("CorelDRAW", 26, null, program, root, null);

            var result = new UiDefinitionLocator().Locate(install);

            CollectionAssert.Contains(result.ToList(), Path.GetFullPath(drawUi));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [TestMethod]
    public void Locate_FindsCorelDrawX4ProgramsUiConfigCorelDrawPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var programDir = Path.Combine(root, "Programs");
            var uiDir = Path.Combine(programDir, "UIConfig", "CorelDRAW");
            Directory.CreateDirectory(uiDir);
            var drawUi = Path.Combine(uiDir, "DrawUI.xml");
            File.WriteAllText(drawUi, "<ui />");
            var program = Path.Combine(programDir, "CorelDRW.exe");
            File.WriteAllBytes(program, new byte[] { 0x4D, 0x5A });
            var install = new CorelInstallation("CorelDRAW Graphics Suite X4", 14, "14.0", program, root, null);

            var result = new UiDefinitionLocator().Locate(install);

            CollectionAssert.Contains(result.ToList(), Path.GetFullPath(drawUi));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [TestMethod]
    public void Locate_FindsModernDrawUiItemsAndOtherDrawUiFragments()
    {
        var root = Path.Combine(Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var uiDir = Path.Combine(root, "Draw", "UIConfig");
            Directory.CreateDirectory(uiDir);
            var drawUi = Path.Combine(uiDir, "DrawUI.xml");
            var items = Path.Combine(uiDir, "DrawUI.items.xml");
            var commands = Path.Combine(uiDir, "DrawUI.commands.xml");
            File.WriteAllText(drawUi, "<ui />");
            File.WriteAllText(items, "<ui />");
            File.WriteAllText(commands, "<ui />");
            var program = Path.Combine(root, "CorelDRW.exe");
            File.WriteAllBytes(program, new byte[] { 0x4D, 0x5A });
            var install = new CorelInstallation("CorelDRAW Graphics Suite 2026", 27, null, program, root, null);

            var result = new UiDefinitionLocator().Locate(install);

            CollectionAssert.Contains(result.ToList(), Path.GetFullPath(drawUi));
            CollectionAssert.Contains(result.ToList(), Path.GetFullPath(items));
            CollectionAssert.Contains(result.ToList(), Path.GetFullPath(commands));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

}
