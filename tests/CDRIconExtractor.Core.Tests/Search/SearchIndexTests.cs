using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Search;

[TestClass]
public sealed class SearchIndexTests
{
    [TestMethod]
    public void Filter_MatchesLocalizedCaptionShortcutAndResourceId()
    {
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "42", 16, 16, "hash", new byte[] { 1 });
        var command = new DrawUiCommand(
            "{11111111-1111-1111-1111-111111111111}", null, "Convert to Curves", "转换为曲线", "Ctrl+Q",
            "itemData", new[] { new ResourceHint("resourceId", "42") }, "/ui/itemData[1]");
        var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "mapped");
        var index = new SearchIndex();

        Assert.AreEqual(1, index.Filter(new[] { association }, "转为曲线").Count);
        Assert.AreEqual(1, index.Filter(new[] { association }, "ctrl+q").Count);
        Assert.AreEqual(1, index.Filter(new[] { association }, "42").Count);
    }
    [TestMethod]
    public void Filter_DoesNotFuzzyMatchVeryShortUnrelatedQuery()
    {
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "42", 16, 16, "hash", new byte[] { 1 });
        var command = new DrawUiCommand(
            null, null, "Convert to Curves", "转换为曲线", "Ctrl+Q",
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData[1]");
        var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "mapped");
        var index = new SearchIndex();

        Assert.AreEqual(0, index.Filter(new[] { association }, "转线").Count);
    }

    [TestMethod]
    public void Filter_MatchesCommonChineseAliasAgainstEnglishCaption()
    {
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "77", 16, 16, "hash", new byte[] { 1 });
        var command = new DrawUiCommand(
            "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", null, "Convert to Curves", null, "Ctrl+Q",
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData[2]");
        var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "mapped");
        var index = new SearchIndex();

        Assert.AreEqual(1, index.Filter(new[] { association }, "转为曲线").Count);
        Assert.AreEqual(1, index.Filter(new[] { association }, "转曲").Count);
    }

    [TestMethod]
    public void Filter_IgnoresWhitespaceAndCommonPunctuation()
    {
        var asset = new IconAsset(@"C:\Program Files\Corel\CrlIcons.dll", "CrlIconsPng", "77", 16, 16, "hash", new byte[] { 1 });
        var command = new DrawUiCommand(
            null, null, "Convert to Curves", null, "Ctrl+Q",
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData[2]");
        var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "mapped");
        var index = new SearchIndex();

        Assert.AreEqual(1, index.Filter(new[] { association }, "Convertto Curves").Count);
        Assert.AreEqual(1, index.Filter(new[] { association }, "ctrl q").Count);
        Assert.AreEqual(1, index.Filter(new[] { association }, "CrlIcons.dll").Count);
    }

    [TestMethod]
    public void Filter_CtrlQAliasMatchesConvertToCurvesWithoutShortcutMetadata()
    {
        var asset = new IconAsset("CrlIcons.dll", "CrlIconsPng", "78", 16, 16, "hash", new byte[] { 1 });
        var command = new DrawUiCommand(
            null, null, "Convert to Curves", null, null,
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData[3]");
        var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "mapped");
        var index = new SearchIndex();

        Assert.AreEqual(1, index.Filter(new[] { association }, "Ctrl+Q").Count);
    }

    [DataTestMethod]
    [DataRow("解组", "Ungroup")]
    [DataRow("取消群组", "Ungroup")]
    [DataRow("解散群组", "Ungroup")]
    [DataRow("群组", "Group")]
    [DataRow("焊接", "Weld")]
    [DataRow("修剪", "Trim")]
    [DataRow("相交", "Intersect")]
    [DataRow("轮廓图", "Contour")]
    [DataRow("透明度", "Transparency")]
    [DataRow("二维码", "QR Code")]
    [DataRow("水平居中", "Center Horizontally")]
    [DataRow("垂直居中", "Center Vertically")]
    public void Filter_MatchesChineseDesignerAliasAgainstEnglishCaption(string query, string englishCaption)
    {
        var command = new DrawUiCommand(
            null, null, englishCaption, null, null,
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData[alias]");
        var association = new IconAssociation(command, null, AssociationConfidence.Unmapped, "alias-test");
        var index = new SearchIndex();

        Assert.AreEqual(1, index.Filter(new[] { association }, query).Count, $"Alias '{query}' should find '{englishCaption}'.");
    }

    [TestMethod]
    public void Filter_PreservesEnglishGuidShortcutAndResourceSearchWhileUsingChineseAliases()
    {
        var asset = new IconAsset(@"C:\Corel\Data\Icons\Modern.crlicons", "ModernPng", "QRcode", 72, 72, "hash", new byte[] { 1 });
        var command = new DrawUiCommand(
            "{12345678-1234-1234-1234-123456789ABC}", null, "QR Code", "二维码", "Ctrl+Shift+Q",
            "itemData", new[] { new ResourceHint("resourceId", "QRcode") }, "/ui/itemData[qr]");
        var association = new IconAssociation(command, asset, AssociationConfidence.Exact, "mapped");
        var index = new SearchIndex();

        Assert.AreEqual(1, index.Filter(new[] { association }, "QR Code").Count);
        Assert.AreEqual(1, index.Filter(new[] { association }, "12345678").Count);
        Assert.AreEqual(1, index.Filter(new[] { association }, "Ctrl Shift Q").Count);
        Assert.AreEqual(1, index.Filter(new[] { association }, "Modern.crlicons").Count);
    }


    [TestMethod]
    public void Filter_ChineseAliasFindsModernAssetAfterCaptionAssociation()
    {
        var asset = new IconAsset(@"C:\Corel\Data\Icons\Modern.crlicons", "ModernCrlIcons", "convert_to_curves", 72, 72, "hash", new byte[] { 1 })
        {
            DisplayName = "convert_to_curves",
            ResourcePath = "icons/convert_to_curves"
        };
        var command = new DrawUiCommand(
            "{11111111-1111-1111-1111-111111111111}", null, "Convert to Curves", null, null,
            "itemData", Array.Empty<ResourceHint>(), "/ui/itemData[modern]");
        var association = new CDRIconExtractor.Core.Association.IconAssociationEngine()
            .Associate(new[] { command }, new[] { asset }, new Dictionary<ushort, IReadOnlyList<string>>())
            .Single();
        var index = new SearchIndex();

        Assert.AreSame(asset, association.Asset);
        Assert.AreEqual(1, index.Filter(new[] { association }, "转曲").Count);
    }

}
