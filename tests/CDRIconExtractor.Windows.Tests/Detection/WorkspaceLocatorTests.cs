using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Windows.Detection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Windows.Tests.Detection;

[TestClass]
public sealed class WorkspaceLocatorTests
{
    [TestMethod]
    public void Locate_PrefersWorkspaceMatchingSelectedVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdr-workspace-locator-" + Guid.NewGuid().ToString("N"));
        var current = Path.Combine(root, "Corel", "CorelDRAW Graphics Suite 2026", "Draw", "Workspace", "_default.cdws");
        var old = Path.Combine(root, "Corel", "CorelDRAW Graphics Suite X8", "Draw", "Workspace", "_default.cdws");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllBytes(current, Array.Empty<byte>());
        File.WriteAllBytes(old, Array.Empty<byte>());
        try
        {
            var installation = new CorelInstallation("CorelDRAW Graphics Suite 2026", 27, null, "C:\\Corel\\CorelDRW.exe", "C:\\Corel", null);
            var result = WorkspaceLocator.Locate(installation, root);

            Assert.IsTrue(result.Count >= 2);
            Assert.AreEqual(Path.GetFullPath(current), result[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
