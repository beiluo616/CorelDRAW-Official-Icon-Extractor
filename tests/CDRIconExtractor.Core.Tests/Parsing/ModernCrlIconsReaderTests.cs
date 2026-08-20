using System.IO.Compression;
using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class ModernCrlIconsReaderTests
{
    [TestMethod]
    public void Read_Groups24_48_72AsOneAssetAndUsesLargestPreview()
    {
        var path = CreateArchive(
            ("Common/Res/CrlCmnRes/RCBin/Toolbar/TB_Group.ico.png/24.png", Png(24, 24)),
            ("Common/Res/CrlCmnRes/RCBin/Toolbar/TB_Group.ico.png/48.png", Png(48, 48)),
            ("Common/Res/CrlCmnRes/RCBin/Toolbar/TB_Group.ico.png/72.png", Png(72, 72)));
        try
        {
            var assets = ModernCrlIconsReader.Read(path);

            Assert.AreEqual(1, assets.Count);
            var asset = assets[0];
            Assert.AreEqual("TB_Group", asset.DisplayName);
            Assert.AreEqual(72, asset.Width);
            Assert.AreEqual(72, asset.Height);
            Assert.AreEqual(3, asset.Variants.Count);
            CollectionAssert.AreEqual(new[] { 24, 48, 72 }, asset.Variants.Select(x => x.Width).OrderBy(x => x).ToArray());
            StringAssert.Contains(asset.ResourcePath ?? string.Empty, "Toolbar/TB_Group.ico.png");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Read_KeepsStandalonePngAsSingleAsset()
    {
        var path = CreateArchive(("Apps/Public/Res/VGCore/AppFunctionalCore/RCBin/custom_preset_button.png", Png(31, 17)));
        try
        {
            var assets = ModernCrlIconsReader.Read(path);

            Assert.AreEqual(1, assets.Count);
            Assert.AreEqual("custom_preset_button", assets[0].DisplayName);
            Assert.AreEqual(31, assets[0].Width);
            Assert.AreEqual(17, assets[0].Height);
            Assert.AreEqual(1, assets[0].Variants.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateArchive(params (string Name, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"modern-{Guid.NewGuid():N}.crlicons");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = zip.CreateEntry(item.Name, CompressionLevel.NoCompression);
            using var target = entry.Open();
            target.Write(item.Bytes);
        }
        return path;
    }

    private static byte[] Png(int width, int height)
    {
        // PngStreamScanner only needs a structurally valid PNG stream with IHDR and IEND.
        var bytes = new byte[45];
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        sig.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        bytes[24] = 8;
        bytes[25] = 6;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(33, 4), 0);
        "IEND"u8.CopyTo(bytes.AsSpan(37, 4));
        return bytes;
    }
}
