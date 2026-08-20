using System.Buffers.Binary;
using System.IO.Compression;

namespace CDRIconExtractor.Core.Parsing;

public static class DibToPngConverter
{
    private const uint BiRgb = 0;

    public static bool TryConvert(
        ReadOnlySpan<byte> dib,
        bool iconHeightIsDoubled,
        out byte[] png,
        out int width,
        out int height)
    {
        png = Array.Empty<byte>();
        width = 0;
        height = 0;
        if (!TryDecodeRgba(dib, iconHeightIsDoubled, out var rgba, out width, out height))
            return false;

        png = EncodeRgbaPng(width, height, rgba);
        return true;
    }

    public static bool TryConvertHorizontalStrip(
        ReadOnlySpan<byte> dib,
        int columns,
        out IReadOnlyList<byte[]> cells,
        out int cellWidth,
        out int cellHeight)
    {
        cells = Array.Empty<byte[]>();
        cellWidth = 0;
        cellHeight = 0;
        if (columns <= 1)
            return false;
        if (!TryDecodeRgba(dib, iconHeightIsDoubled: false, out var rgba, out var width, out var height))
            return false;
        if (width % columns != 0)
            return false;

        cellWidth = width / columns;
        cellHeight = height;
        // CorelDRAW X4/X5 uses bmpCol 0..9 against horizontal bitmap strips.
        // Requiring square cells avoids splitting unrelated wide artwork resources.
        if (cellWidth <= 0 || cellHeight <= 0 || cellWidth != cellHeight || cellWidth is < 8 or > 128)
            return false;

        var result = new byte[columns][];
        for (var column = 0; column < columns; column++)
        {
            var cellRgba = new byte[checked(cellWidth * cellHeight * 4)];
            for (var y = 0; y < cellHeight; y++)
            {
                var sourceOffset = (y * width + column * cellWidth) * 4;
                var targetOffset = y * cellWidth * 4;
                rgba.AsSpan(sourceOffset, cellWidth * 4).CopyTo(cellRgba.AsSpan(targetOffset, cellWidth * 4));
            }
            result[column] = EncodeRgbaPng(cellWidth, cellHeight, cellRgba);
        }

        cells = result;
        return true;
    }

    private static bool TryDecodeRgba(
        ReadOnlySpan<byte> dib,
        bool iconHeightIsDoubled,
        out byte[] rgba,
        out int width,
        out int height)
    {
        rgba = Array.Empty<byte>();
        width = 0;
        height = 0;
        if (dib.Length < 40)
            return false;

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib.Slice(0, 4));
        if (headerSize < 40 || headerSize > dib.Length)
            return false;

        var rawWidth = BinaryPrimitives.ReadInt32LittleEndian(dib.Slice(4, 4));
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib.Slice(8, 4));
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(dib.Slice(12, 2));
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.Slice(14, 2));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.Slice(16, 4));

        if (rawWidth <= 0 || rawHeight == 0 || planes != 1 || compression != BiRgb || bitCount is not (1 or 4 or 8 or 24 or 32))
            return false;

        width = rawWidth;
        var topDown = rawHeight < 0;
        var absoluteHeight = Math.Abs(rawHeight);
        height = iconHeightIsDoubled ? absoluteHeight / 2 : absoluteHeight;
        if (height <= 0)
            return false;

        var paletteEntries = 0;
        var pixelOffset = headerSize;
        if (bitCount <= 8)
        {
            var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.Slice(32, 4));
            paletteEntries = colorsUsed == 0 ? 1 << bitCount : checked((int)colorsUsed);
            if (paletteEntries <= 0 || paletteEntries > (1 << bitCount))
                return false;
            pixelOffset = checked(headerSize + paletteEntries * 4);
            if (pixelOffset > dib.Length)
                return false;
        }

        var rowStride = checked(((width * bitCount + 31) / 32) * 4);
        var pixelBytes = checked(rowStride * height);
        if (pixelOffset + pixelBytes > dib.Length)
            return false;

        rgba = new byte[checked(width * height * 4)];
        var alphaNonZero = false;
        for (var y = 0; y < height; y++)
        {
            var sourceY = topDown ? y : (height - 1 - y);
            var sourceRow = pixelOffset + sourceY * rowStride;
            var targetRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                byte r;
                byte g;
                byte b;
                byte a = 255;

                if (bitCount <= 8)
                {
                    var paletteIndex = bitCount switch
                    {
                        8 => dib[sourceRow + x],
                        4 => (byte)((x & 1) == 0 ? dib[sourceRow + (x >> 1)] >> 4 : dib[sourceRow + (x >> 1)] & 0x0F),
                        1 => (byte)((dib[sourceRow + (x >> 3)] >> (7 - (x & 7))) & 0x01),
                        _ => 0
                    };
                    if (paletteIndex >= paletteEntries)
                        return false;
                    var paletteOffset = headerSize + paletteIndex * 4;
                    b = dib[paletteOffset];
                    g = dib[paletteOffset + 1];
                    r = dib[paletteOffset + 2];
                }
                else
                {
                    var bytesPerPixel = bitCount / 8;
                    var source = sourceRow + x * bytesPerPixel;
                    b = dib[source];
                    g = dib[source + 1];
                    r = dib[source + 2];
                    a = bitCount == 32 ? dib[source + 3] : (byte)255;
                    alphaNonZero |= a != 0;
                }

                var target = targetRow + x * 4;
                rgba[target] = r;
                rgba[target + 1] = g;
                rgba[target + 2] = b;
                rgba[target + 3] = a;
            }
        }

        if (bitCount == 32 && !alphaNonZero)
        {
            for (var i = 3; i < rgba.Length; i += 4)
                rgba[i] = 255;
        }

        return true;
    }

    private static byte[] EncodeRgbaPng(int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(output, "IHDR"u8, ihdr);

        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba.Slice(y * width * 4, width * 4));
        }
        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            raw.CopyTo(zlib);
        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }
}
