using System.Buffers.Binary;
using System.Text;

namespace CDRIconExtractor.Core.Parsing;

public static class CrlIconGuidMapParser
{
    public static IReadOnlyDictionary<ushort, IReadOnlyList<string>> Parse(IEnumerable<ReadOnlyMemory<byte>> blobs)
    {
        ArgumentNullException.ThrowIfNull(blobs);
        var map = new Dictionary<ushort, HashSet<string>>();

        foreach (var memory in blobs)
        {
            var span = memory.Span;
            if (TryParseDelimitedResource(span, map))
                continue;

            // Synthetic fixtures and older variants do not always expose the delimiter
            // envelope. Keep bounded fallbacks for those layouts.
            ParseFixed76(span, map);
            ParseGuidNeighborhoods(span, map);
        }

        return map.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<string>)x.Value.OrderBy(v => v, StringComparer.Ordinal).ToArray());
    }

    public static string? NormalizeGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Guid.TryParse(value.Trim(), out var guid) ? guid.ToString("D").ToLowerInvariant() : null;
    }

    private static bool TryParseDelimitedResource(ReadOnlySpan<byte> span, Dictionary<ushort, HashSet<string>> map)
    {
        // Corel's CrlIcons RT_RCDATA GUID map starts with two metadata bytes followed by
        // a four-byte record delimiter. A record is the byte range between delimiter
        // occurrences; bytes after the final delimiter are not part of a completed record.
        if (span.Length < 10)
            return false;

        var delimiter = span.Slice(2, 4);
        // Real CrlIcons delimiter data begins with a UTF-16 NUL marker followed by
        // a marker character (the reference implementation commonly sees "\0$").
        // A standalone 76-byte record starts directly with GUID text at offset 2;
        // treating the first four GUID bytes as a delimiter breaks repeating GUIDs.
        if (delimiter[0] != 0 || delimiter[1] != 0 || delimiter[2] == 0 || delimiter[3] != 0)
            return false;

        var firstDelimiter = Find(span, delimiter, 0);
        if (firstDelimiter != 2)
            return false;

        var nextDelimiter = Find(span, delimiter, firstDelimiter + delimiter.Length);
        if (nextDelimiter < 0)
            return false;

        var cursor = firstDelimiter + delimiter.Length;
        while (nextDelimiter >= 0)
        {
            ParseDelimitedSegment(span.Slice(cursor, nextDelimiter - cursor), map);
            cursor = nextDelimiter + delimiter.Length;
            nextDelimiter = Find(span, delimiter, cursor);
        }

        return true;
    }

    private static void ParseDelimitedSegment(ReadOnlySpan<byte> segment, Dictionary<ushort, HashSet<string>> map)
    {
        if (segment.Length == 76)
        {
            var guidText = DecodeUtf16(segment.Slice(2, 72));
            var normalized = NormalizeGuid(guidText);
            if (normalized is null)
                return;

            var id = BinaryPrimitives.ReadUInt16LittleEndian(segment.Slice(74, 2));
            if (id != 0)
                Add(map, id, normalized);
            return;
        }

        if (segment.Length <= 2 || (segment.Length & 1) != 0)
            return;

        string decoded;
        try
        {
            decoded = Encoding.Unicode.GetString(segment);
        }
        catch
        {
            return;
        }

        foreach (var slice in decoded.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (slice.Length < 37)
                continue;

            var normalized = NormalizeGuid(slice[..^1]);
            if (normalized is null)
                continue;

            var id = (ushort)slice[^1];
            if (id != 0)
                Add(map, id, normalized);
        }
    }

    private static int Find(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, int start)
    {
        if (needle.IsEmpty || start < 0 || start > haystack.Length - needle.Length)
            return -1;

        for (var index = start; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.Slice(index, needle.Length).SequenceEqual(needle))
                return index;
        }
        return -1;
    }

    private static void ParseFixed76(ReadOnlySpan<byte> span, Dictionary<ushort, HashSet<string>> map)
    {
        for (var offset = 0; offset + 76 <= span.Length; offset += 76)
        {
            var guidText = DecodeUtf16(span.Slice(offset + 2, 72));
            var normalized = NormalizeGuid(guidText);
            if (normalized is null)
                continue;
            var id = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset + 74, 2));
            if (id == 0)
                continue;
            Add(map, id, normalized);
        }
    }

    private static void ParseGuidNeighborhoods(ReadOnlySpan<byte> span, Dictionary<ushort, HashSet<string>> map)
    {
        const int GuidCharCount = 36;
        const int GuidByteCount = GuidCharCount * 2;
        for (var offset = 0; offset + GuidByteCount <= span.Length; offset += 2)
        {
            var candidate = DecodeUtf16(span.Slice(offset, GuidByteCount));
            var normalized = NormalizeGuid(candidate);
            if (normalized is null)
                continue;

            foreach (var idOffset in CandidateIdOffsets(offset + GuidByteCount, span.Length))
            {
                var id = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(idOffset, 2));
                if (id == 0)
                    continue;
                Add(map, id, normalized);
                break;
            }

            offset += GuidByteCount - 2;
        }
    }

    private static IEnumerable<int> CandidateIdOffsets(int afterGuid, int totalLength)
    {
        // Known fallback layouts place the UInt16 id directly after the GUID or after
        // one UTF-16 NUL separator.
        if (afterGuid + 2 <= totalLength)
            yield return afterGuid;
        if (afterGuid + 4 <= totalLength)
            yield return afterGuid + 2;
    }

    private static string DecodeUtf16(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void Add(Dictionary<ushort, HashSet<string>> map, ushort id, string guid)
    {
        if (!map.TryGetValue(id, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            map[id] = set;
        }
        set.Add(guid);
    }
}
