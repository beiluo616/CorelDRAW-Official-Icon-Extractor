using CDRIconExtractor.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Models;

[TestClass]
public sealed class ScanResultTests
{
    [TestMethod]
    public void ScanResult_Cancelled_PreservesPartialItems()
    {
        var command = new DrawUiCommand("{11111111-1111-1111-1111-111111111111}", null, "Command", null, null, "itemData", Array.Empty<ResourceHint>(), "/ui/itemData");
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "1", 16, 16, "hash", new byte[] { 1 });
        var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "mapped");

        var result = ScanResult.Cancelled(new[] { association }, scannedFiles: 1, elapsed: TimeSpan.FromSeconds(2));

        Assert.IsTrue(result.IsCancelled);
        Assert.AreEqual(1, result.Associations.Count);
        Assert.AreEqual(1, result.ScannedFiles);
    }
}
