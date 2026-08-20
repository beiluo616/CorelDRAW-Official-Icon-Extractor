using System.Buffers.Binary;
using System.Security.Cryptography;

namespace CDRIconExtractor.Core.Parsing;

public sealed record PngSlice(int Offset, int Length, int Width, int Height, string Sha256);

public static class PngStreamScanner
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static IReadOnlyList<PngSlice> Find(ReadOnlySpan<byte> bytes)
    {
        var result = new List<PngSlice>();
        var offset = 0;

        while (offset <= bytes.Length - Signature.Length)
        {
            var relative = bytes[offset..].IndexOf(Signature);
            if (relative < 0)
                break;

            var start = offset + relative;
            if (TryParse(bytes, start, out var slice))
            {
                result.Add(slice);
                offset = start + slice.Length;
            }
            else
            {
                offset = start + 1;
            }
        }

        return result;
    }

    private static bool TryParse(ReadOnlySpan<byte> bytes, int start, out PngSlice slice)
    {
        slice = default!;
        var cursor = start + Signature.Length;
        var width = 0;
        var height = 0;
        var sawIhdr = false;
        var sawIend = false;

        while (cursor <= bytes.Length - 12)
        {
            var lengthUnsigned = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(cursor, 4));
            if (lengthUnsigned > int.MaxValue)
                return false;
            var length = (int)lengthUnsigned;
            var totalChunkLength = 12L + length;
            if (cursor + totalChunkLength > bytes.Length)
                return false;

            var type = bytes.Slice(cursor + 4, 4);
            if (!IsAsciiChunkType(type))
                return false;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawIhdr || length != 13 || cursor != start + Signature.Length)
                    return false;
                var widthUnsigned = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(cursor + 8, 4));
                var heightUnsigned = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(cursor + 12, 4));
                if (widthUnsigned is 0 or > int.MaxValue || heightUnsigned is 0 or > int.MaxValue)
                    return false;
                width = (int)widthUnsigned;
                height = (int)heightUnsigned;
                sawIhdr = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!sawIhdr || length != 0)
                    return false;
                cursor += (int)totalChunkLength;
                sawIend = true;
                break;
            }

            cursor += (int)totalChunkLength;
        }

        if (!sawIhdr || !sawIend)
            return false;

        var pngLength = cursor - start;
        var hash = Convert.ToHexString(SHA256.HashData(bytes.Slice(start, pngLength))).ToLowerInvariant();
        slice = new PngSlice(start, pngLength, width, height, hash);
        return true;
    }

    private static bool IsAsciiChunkType(ReadOnlySpan<byte> type)
    {
        if (type.Length != 4)
            return false;
        foreach (var b in type)
        {
            var alpha = b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
            if (!alpha)
                return false;
        }
        return true;
    }
}
