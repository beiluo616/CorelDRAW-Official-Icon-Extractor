using CDRIconExtractor.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Models;

[TestClass]
public sealed class DomainModelTests
{
    [TestMethod]
    public void IconAssociation_ExposesConfidenceAndAsset()
    {
        var asset = new IconAsset(
            "CrlIcons.dll",
            "CrlIconsPng",
            "42",
            16,
            16,
            "abc",
            new byte[] { 1 });

        var command = new DrawUiCommand(
            "{11111111-1111-1111-1111-111111111111}",
            null,
            "Convert to Curves",
            "转换为曲线",
            "Ctrl+Q",
            "itemData",
            Array.Empty<ResourceHint>(),
            "/ui/itemData[1]");

        var association = new IconAssociation(
            command,
            asset,
            AssociationConfidence.Exact,
            "GUID map id=42");

        Assert.AreEqual(AssociationConfidence.Exact, association.Confidence);
        Assert.AreSame(asset, association.Asset);
    }
}
