using CDRIconExtractor.Core.Association;
using CDRIconExtractor.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Association;

[TestClass]
public sealed class IconAssociationEngineTests
{
    private const string Guid1 = "11111111-1111-1111-1111-111111111111";
    private readonly IconAssociationEngine _engine = new();

    [TestMethod]
    public void Associate_GuidMapMatch_IsExact()
    {
        var result = _engine.Associate(
            new[] { Command(Guid1) },
            new[] { Asset("42") },
            new Dictionary<ushort, IReadOnlyList<string>> { [42] = new[] { Guid1 } });

        Assert.AreEqual(AssociationConfidence.Exact, result.Single().Confidence);
        Assert.AreEqual("42", result.Single().Asset?.ResourceId);
    }

    [TestMethod]
    public void Associate_NoReliableRule_IsUnmapped()
    {
        var result = _engine.Associate(
            new[] { Command(Guid1) },
            new[] { Asset("99") },
            new Dictionary<ushort, IReadOnlyList<string>>());

        Assert.AreEqual(AssociationConfidence.Unmapped, result.Single().Confidence);
        Assert.IsNull(result.Single().Asset);
    }

    [TestMethod]
    public void Associate_ExplicitResourceId_IsExact()
    {
        var command = Command(Guid1) with { ResourceHints = new[] { new ResourceHint("resourceId", "9") } };
        var result = _engine.Associate(new[] { command }, new[] { Asset("9") }, new Dictionary<ushort, IReadOnlyList<string>>());
        Assert.AreEqual(AssociationConfidence.Exact, result.Single().Confidence);
    }


    [TestMethod]
    public void Associate_LegacyBmpRowBmpCol_MapsStripCell()
    {
        var command = Command(Guid1) with
        {
            ResourceHints = new[]
            {
                new ResourceHint("bmpRow", "321"),
                new ResourceHint("bmpCol", "7")
            }
        };
        var asset = new IconAsset("CrlGenericUI.dll", "RT_BITMAP_STRIP_CELL", "321:7", 16, 16, "hash", new byte[] { 1 });

        var result = _engine.Associate(new[] { command }, new[] { asset }, new Dictionary<ushort, IReadOnlyList<string>>());

        Assert.AreEqual(AssociationConfidence.Exact, result.Single().Confidence);
        Assert.AreSame(asset, result.Single().Asset);
        StringAssert.Contains(result.Single().Reason, "bmpRow=321");
        StringAssert.Contains(result.Single().Reason, "bmpCol=7");
    }


    [TestMethod]
    public void Associate_DirectIconGuidHint_UsesSeparateIconGuid()
    {
        const string iconGuid = "22222222-2222-2222-2222-222222222222";
        var command = Command(Guid1) with
        {
            ResourceHints = new[] { new ResourceHint("icon", $"guid://{iconGuid}") }
        };

        var result = _engine.Associate(
            new[] { command },
            new[] { Asset("42") },
            new Dictionary<ushort, IReadOnlyList<string>> { [42] = new[] { iconGuid } });

        var association = result.Single();
        Assert.AreEqual(AssociationConfidence.Exact, association.Confidence);
        Assert.AreEqual("42", association.Asset?.ResourceId);
        Assert.AreEqual(iconGuid, association.IconGuid);
        Assert.AreEqual(Guid1, association.Command.Guid);
    }

    [TestMethod]
    public void Associate_CommandGuidMapMatch_ExposesMatchedGuidAsIconGuid()
    {
        var result = _engine.Associate(
            new[] { Command(Guid1) },
            new[] { Asset("42") },
            new Dictionary<ushort, IReadOnlyList<string>> { [42] = new[] { Guid1 } });

        Assert.AreEqual(Guid1, result.Single().IconGuid);
    }

    private static DrawUiCommand Command(string guid) =>
        new(guid, null, "Command", null, null, "itemData", Array.Empty<ResourceHint>(), "/ui/itemData");

    private static IconAsset Asset(string id) =>
        new("CrlIcons.dll", "CrlIconsPng", id, 16, 16, id.PadLeft(64, '0'), new byte[] { 1 });

    [TestMethod]
    public void Associate_CommandIconGuid_ResolvesThroughResourceEntryGuid()
    {
        const string iconGuid = "22222222-2222-2222-2222-222222222222";
        var resourceEntry = new DrawUiCommand(
            iconGuid, null, null, null, null, "resEntry",
            new[] { new ResourceHint("icon", "77") }, "/ui/resEntry");
        var command = Command(Guid1) with
        {
            ResourceHints = new[] { new ResourceHint("icon", $"guid://{iconGuid}") }
        };

        var result = _engine.Associate(
            new[] { resourceEntry, command },
            new[] { Asset("77") },
            new Dictionary<ushort, IReadOnlyList<string>>());

        var association = result.Single(x => x.Command == command);
        Assert.AreEqual("77", association.Asset?.ResourceId);
        Assert.AreEqual(iconGuid, association.IconGuid);
        Assert.AreEqual(AssociationConfidence.Strong, association.Confidence);
    }

    [TestMethod]
    public void Associate_GuidMapCanUseCrlGenericUiResourceWhenCrlIconsImageIsMissing()
    {
        var genericAsset = new IconAsset("CrlGenericUI.dll", "RT_ICON", "42", 32, 32, "hash", new byte[] { 1 });
        var result = _engine.Associate(
            new[] { Command(Guid1) },
            new[] { genericAsset },
            new Dictionary<ushort, IReadOnlyList<string>> { [42] = new[] { Guid1 } });

        Assert.AreSame(genericAsset, result.Single().Asset);
        Assert.AreEqual(Guid1, result.Single().IconGuid);
    }
    [TestMethod]
    public void Associate_IconGuidMapWithoutExtractableImage_RetainsResourceIdHint()
    {
        const string iconGuid = "22222222-2222-2222-2222-222222222222";
        var command = Command(Guid1) with
        {
            ResourceHints = new[] { new ResourceHint("icon", $"guid://{iconGuid}") }
        };

        var result = _engine.Associate(
            new[] { command },
            Array.Empty<IconAsset>(),
            new Dictionary<ushort, IReadOnlyList<string>> { [820] = new[] { iconGuid } });

        var association = result.Single();
        Assert.IsNull(association.Asset);
        Assert.AreEqual(iconGuid, association.IconGuid);
        Assert.AreEqual("820", association.ResourceIdHint);
        StringAssert.Contains(association.Reason, "id=820");
    }


    [TestMethod]
    public void Associate_ModernNamedResourceEntry_MapsAssetAndUsesResourceEntryGuidAsIconGuid()
    {
        const string iconGuid = "33333333-3333-3333-3333-333333333333";
        var resourceEntry = new DrawUiCommand(
            iconGuid, null, null, null, null, "resEntry",
            new[] { new ResourceHint("icon", "parallel_lines_right") }, "/ui/resEntry[modern]");
        var modernAsset = new IconAsset(
            @"C:\Corel\Data\Icons\Modern.crlicons", "ModernCrlIcons", "parallel_lines_right",
            72, 72, "hash", new byte[] { 1 })
        {
            DisplayName = "parallel_lines_right",
            ResourcePath = "icons/parallel_lines_right"
        };

        var association = _engine.Associate(
            new[] { resourceEntry },
            new[] { modernAsset },
            new Dictionary<ushort, IReadOnlyList<string>>()).Single();

        Assert.AreSame(modernAsset, association.Asset);
        Assert.AreEqual(AssociationConfidence.Exact, association.Confidence);
        Assert.AreEqual(iconGuid, association.IconGuid);
        StringAssert.Contains(association.Reason, "Modern");
    }

    [TestMethod]
    public void Associate_CommandIconGuid_ResolvesThroughNamedModernResourceEntry()
    {
        const string iconGuid = "33333333-3333-3333-3333-333333333333";
        var resourceEntry = new DrawUiCommand(
            iconGuid, null, null, null, null, "resEntry",
            new[] { new ResourceHint("icon", "convert_to_curves") }, "/ui/resEntry[modern]");
        var command = Command(Guid1) with
        {
            Caption = "Convert to Curves",
            ResourceHints = new[] { new ResourceHint("icon", $"guid://{iconGuid}") }
        };
        var modernAsset = new IconAsset(
            @"C:\Corel\Data\Icons\Modern.crlicons", "ModernCrlIcons", "convert_to_curves",
            72, 72, "hash", new byte[] { 1 })
        {
            DisplayName = "convert_to_curves",
            ResourcePath = "icons/convert_to_curves"
        };

        var result = _engine.Associate(
            new[] { resourceEntry, command },
            new[] { modernAsset },
            new Dictionary<ushort, IReadOnlyList<string>>());
        var association = result.Single(x => x.Command == command);

        Assert.AreSame(modernAsset, association.Asset);
        Assert.AreEqual(AssociationConfidence.Strong, association.Confidence);
        Assert.AreEqual(iconGuid, association.IconGuid);
    }

    [TestMethod]
    public void Associate_ModernCaptionSlug_MapsAssetWhenNoExplicitResourceHintExists()
    {
        var command = Command(Guid1) with { Caption = "Convert to Curves" };
        var modernAsset = new IconAsset(
            @"C:\Corel\Data\Icons\Modern.crlicons", "ModernCrlIcons", "convert_to_curves",
            72, 72, "hash", new byte[] { 1 })
        {
            DisplayName = "convert_to_curves",
            ResourcePath = "icons/convert_to_curves"
        };

        var association = _engine.Associate(
            new[] { command },
            new[] { modernAsset },
            new Dictionary<ushort, IReadOnlyList<string>>()).Single();

        Assert.AreSame(modernAsset, association.Asset);
        Assert.AreEqual(AssociationConfidence.Strong, association.Confidence);
        Assert.AreEqual(Guid1, association.Command.Guid);
        Assert.IsNull(association.IconGuid);
    }
    [TestMethod]
    public void Associate_DeclaredIconGuid_UsesIconsMapGuidOnModernAsset()
    {
        const string iconGuid = "11111111-2222-3333-4444-555555555555";
        var command = Command(Guid1) with
        {
            Caption = "Ungroup",
            ResourceHints = new[] { new ResourceHint("icon", $"guid://{iconGuid}") }
        };
        var modernAsset = new IconAsset(
            @"C:\Corel\Data\Icons\Modern.crlicons", "ModernCrlIcons", "TB_Ungroup",
            72, 72, "hash", new byte[] { 1 })
        {
            DisplayName = "TB_Ungroup",
            ResourcePath = "Common/Res/CrlCmnRes/RCBin/Toolbar/TB_Ungroup.ico.png",
            IconGuids = new[] { iconGuid },
            IconGuidSource = @"C:\Corel\Data\Icons\icons.map.xml"
        };

        var association = _engine.Associate(
            new[] { command },
            new[] { modernAsset },
            new Dictionary<ushort, IReadOnlyList<string>>()).Single();

        Assert.AreSame(modernAsset, association.Asset);
        Assert.AreEqual(AssociationConfidence.Exact, association.Confidence);
        Assert.AreEqual(iconGuid, association.IconGuid);
        StringAssert.Contains(association.Reason, "icons.map.xml");
    }

}

// Fix6 regression: legacy CorelDRAW X4/X5 UI uses bmpRow/bmpCol coordinates.

// Fix8 regression: CorelDRAW UI may use a dedicated icon GUID separate from the command GUID.
// The association must retain that icon GUID so callers can reuse CorelDRAW's internal icon.
