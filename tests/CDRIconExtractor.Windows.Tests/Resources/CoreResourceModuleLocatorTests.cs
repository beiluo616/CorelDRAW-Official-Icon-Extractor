using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Windows.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Windows.Tests.Resources;

[TestClass]
public sealed class CoreResourceModuleLocatorTests
{
    [TestMethod]
    public void LocateCoreModules_IncludesCrlGenericUiAndCorelDrw()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdr-icon-core-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Programs64"));
        var program = Path.Combine(root, "Programs64", "CorelDRW.exe");
        var generic = Path.Combine(root, "Programs64", "CrlGenericUI.dll");
        File.WriteAllBytes(program, new byte[] { 1 });
        File.WriteAllBytes(generic, new byte[] { 2 });

        try
        {
            var installation = new CorelInstallation("CorelDRAW Graphics Suite 2026", 27, "27.0.0.0", program, root, null);
            var result = CoreResourceModuleLocator.LocateCoreModules(installation);

            CollectionAssert.Contains(result.ToList(), Path.GetFullPath(generic));
            CollectionAssert.Contains(result.ToList(), Path.GetFullPath(program));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
