using CDRIconExtractor.Windows.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Windows.Tests.Resources;

[TestClass]
public sealed class Win32ResourceReaderTests
{
    [TestMethod]
    public void ReadResources_MissingFile_ThrowsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.dll");
        Assert.ThrowsException<FileNotFoundException>(() => new Win32ResourceReader().ReadResources(path, 10));
    }
}
