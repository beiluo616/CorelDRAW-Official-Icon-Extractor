using System.Buffers.Binary;
using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class DibToPngConverterTests
{
    [TestMethod]
    public void Convert32BitBiRgb_ProducesValidPng()
    {
        var dib = new byte[40 + 2 * 2 * 4];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8, 4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14, 2), 32);
        // BGRA pixels, bottom-up.
        dib[40] = 0; dib[41] = 0; dib[42] = 255; dib[43] = 255;
        dib[44] = 0; dib[45] = 255; dib[46] = 0; dib[47] = 255;
        dib[48] = 255; dib[49] = 0; dib[50] = 0; dib[51] = 255;
        dib[52] = 255; dib[53] = 255; dib[54] = 255; dib[55] = 255;

        Assert.IsTrue(DibToPngConverter.TryConvert(dib, iconHeightIsDoubled: false, out var png, out var width, out var height));
        Assert.AreEqual(2, width);
        Assert.AreEqual(2, height);
        Assert.AreEqual(1, PngStreamScanner.Find(png).Count);
    }

    [TestMethod]
    public void TryConvertHorizontalStrip_Indexed8BitTenColumns_ReturnsTenCells()
    {
        const int columns = 10;
        const int cell = 8;
        var width = columns * cell;
        var height = cell;
        var rowStride = ((width * 8 + 31) / 32) * 4;
        var paletteBytes = 256 * 4;
        var dib = new byte[40 + paletteBytes + rowStride * height];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0, 4), 40);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8, 4), height);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12, 2), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14, 2), 8);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16, 4), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(32, 4), 256);
        for (var i = 0; i < 256; i++)
        {
            var offset = 40 + i * 4;
            dib[offset] = (byte)i;
            dib[offset + 1] = (byte)i;
            dib[offset + 2] = (byte)i;
        }
        var pixels = 40 + paletteBytes;
        for (var x = 0; x < width; x++)
            dib[pixels + x] = (byte)(x + 1);
        for (var x = 0; x < width; x++)
            dib[pixels + rowStride + x] = (byte)(x + 1);

        var ok = DibToPngConverter.TryConvertHorizontalStrip(dib, columns, out var cells, out var cellWidth, out var cellHeight);

        Assert.IsTrue(ok);
        Assert.AreEqual(10, cells.Count);
        Assert.AreEqual(8, cellWidth);
        Assert.AreEqual(8, cellHeight);
        Assert.IsTrue(cells.All(x => x.Length > 8 && x[0] == 0x89 && x[1] == 0x50));
    }

}
