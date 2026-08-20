using System.Collections.Generic;
using CDRIconExtractor.Core.Association;
using CDRIconExtractor.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Association;

[TestClass]
public sealed class ExternalCrlIconsCatalogBuilderTests
{
    [TestMethod]
    public void Build_MapsResourceIdToIconGuid()
    {
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "25", 32, 32, "sha", [1, 2, 3]);
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> map = new Dictionary<ushort, IReadOnlyList<string>>
        {
            [25] = ["{48428211-5DAB-4A01-810D-5CA8BFB7619B}"]
        };

        var catalog = ExternalCrlIconsCatalogBuilder.Build([asset], map);

        Assert.AreEqual(1, catalog.Associations.Count);
        Assert.AreEqual("48428211-5dab-4a01-810d-5ca8bfb7619b", catalog.Associations[0].IconGuid);
        Assert.AreEqual("25", catalog.Associations[0].ResourceIdHint);
        Assert.AreSame(asset, catalog.Associations[0].Asset);
    }

    [TestMethod]
    public void Build_KeepsPreviewWhenGuidMapIsMissing()
    {
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "693", 256, 256, "sha", [1]);

        var catalog = ExternalCrlIconsCatalogBuilder.Build(
            [asset],
            new Dictionary<ushort, IReadOnlyList<string>>());

        Assert.AreEqual(AssociationConfidence.Unmapped, catalog.Associations[0].Confidence);
        Assert.IsNull(catalog.Associations[0].IconGuid);
        Assert.AreSame(asset, catalog.Associations[0].Asset);
    }
}
