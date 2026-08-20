using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;

namespace CDRIconExtractor.Windows.Resources;

public sealed class GenericPeIconScanner
{
    private const ushort RtBitmap = 2;
    private const ushort RtIcon = 3;
    private const ushort RtRcData = 10;
    private const ushort RtGroupIcon = 14;
    private readonly IWin32ResourceReader _reader;

    public GenericPeIconScanner() : this(new Win32ResourceReader()) { }
    public GenericPeIconScanner(IWin32ResourceReader reader) => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public IReadOnlyList<IconAsset> Scan(string path, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        token.ThrowIfCancellationRequested();
        var result = new List<IconAsset>();

        var iconResources = ReadSafely(path, RtIcon);
        var referencedIconNames = AddGroupIcons(path, ReadSafely(path, RtGroupIcon), iconResources, result, token);

        foreach (var resource in iconResources)
        {
            token.ThrowIfCancellationRequested();
            if (referencedIconNames.Contains(resource.Name))
                continue;
            AddImageResource(path, "RT_ICON", resource.Name, resource.Bytes, iconHeightIsDoubled: true, result);
        }

        foreach (var resource in ReadSafely(path, RtBitmap))
        {
            token.ThrowIfCancellationRequested();
            if (DibToPngConverter.TryConvertHorizontalStrip(resource.Bytes, 10, out var stripCells, out var cellWidth, out var cellHeight))
            {
                for (var column = 0; column < stripCells.Count; column++)
                    AddAsset(path, "RT_BITMAP_STRIP_CELL", $"{resource.Name}:{column}", stripCells[column], cellWidth, cellHeight, result);
                continue;
            }

            AddImageResource(path, "RT_BITMAP", resource.Name, resource.Bytes, iconHeightIsDoubled: false, result);
        }

        foreach (var resource in ReadSafely(path, RtRcData))
        {
            token.ThrowIfCancellationRequested();
            AddPngSlices(path, resource, result);
        }

        // Newer CorelDRAW releases can store UI images in named/custom PE resource
        // types instead of the standard RT_ICON/RT_BITMAP buckets. Enumerate those
        // resource types and decode PNG/DIB payloads while preserving the resource
        // name as the ID so a GUID->resource-id map can still resolve them.
        if (_reader is IWin32ResourceCatalog catalog)
        {
            IReadOnlyList<Win32ResourceTypeSummary> summaries;
            try { summaries = catalog.InspectResourceTypes(path); }
            catch { summaries = Array.Empty<Win32ResourceTypeSummary>(); }

            foreach (var summary in summaries.Where(x => x.TypeId is null && x.ResourceCount is > 0 and <= 12000).Take(32))
            {
                token.ThrowIfCancellationRequested();
                IReadOnlyList<Win32ResourceBlob> resources;
                try { resources = catalog.ReadResources(path, summary.TypeName); }
                catch { continue; }

                foreach (var resource in resources)
                {
                    token.ThrowIfCancellationRequested();
                    var slices = PngStreamScanner.Find(resource.Bytes);
                    if (slices.Count > 0)
                    {
                        for (var i = 0; i < slices.Count; i++)
                        {
                            var slice = slices[i];
                            var bytes = resource.Bytes.AsSpan(slice.Offset, slice.Length).ToArray();
                            var id = slices.Count == 1 ? resource.Name : $"{resource.Name}:{i + 1}";
                            AddAsset(path, $"CUSTOM:{summary.TypeName}:PNG", id, bytes, slice.Width, slice.Height, result);
                        }
                        continue;
                    }

                    _ = AddImageResource(path, $"CUSTOM:{summary.TypeName}", resource.Name, resource.Bytes, iconHeightIsDoubled: false, result);
                }
            }
        }

        return result;
    }

    private HashSet<string> AddGroupIcons(
        string path,
        IReadOnlyList<Win32ResourceBlob> groups,
        IReadOnlyList<Win32ResourceBlob> icons,
        ICollection<IconAsset> result,
        CancellationToken token)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var iconsByName = icons
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in ParseGroupIconEntries(group.Bytes))
            {
                var iconName = entry.IconResourceId.ToString(CultureInfo.InvariantCulture);
                if (!iconsByName.TryGetValue(iconName, out var icon))
                    continue;

                var outputId = $"{group.Name}/{iconName}";
                if (AddImageResource(path, "RT_GROUP_ICON", outputId, icon.Bytes, iconHeightIsDoubled: true, result))
                    referenced.Add(iconName);
            }
        }

        return referenced;
    }

    private IReadOnlyList<Win32ResourceBlob> ReadSafely(string path, ushort type)
    {
        try
        {
            return _reader.ReadResources(path, type);
        }
        catch (PlatformNotSupportedException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<GroupIconEntry> ParseGroupIconEntries(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 6)
            return [];

        var reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(0, 2));
        var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2, 2));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4, 2));
        if (reserved != 0 || type != 1 || count == 0)
            return [];

        const int entrySize = 14;
        if (6L + (long)count * entrySize > bytes.Length)
            return [];

        var entries = new List<GroupIconEntry>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = 6 + index * entrySize;
            var widthByte = bytes[offset];
            var heightByte = bytes[offset + 1];
            var resourceId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset + 12, 2));
            if (resourceId == 0)
                continue;
            entries.Add(new GroupIconEntry(
                resourceId,
                widthByte == 0 ? 256 : widthByte,
                heightByte == 0 ? 256 : heightByte));
        }
        return entries;
    }

    private static bool AddImageResource(
        string path,
        string resourceType,
        string resourceId,
        byte[] bytes,
        bool iconHeightIsDoubled,
        ICollection<IconAsset> result)
    {
        if (StartsWithPng(bytes))
        {
            var slices = PngStreamScanner.Find(bytes);
            if (slices.Count == 1 && slices[0].Offset == 0)
            {
                AddAsset(path, resourceType, resourceId, bytes, slices[0].Width, slices[0].Height, result);
                return true;
            }
            return false;
        }

        if (!DibToPngConverter.TryConvert(bytes, iconHeightIsDoubled, out var png, out var width, out var height))
            return false;

        AddAsset(path, resourceType, resourceId, png, width, height, result);
        return true;
    }

    private static void AddPngSlices(string path, Win32ResourceBlob resource, ICollection<IconAsset> result)
    {
        var slices = PngStreamScanner.Find(resource.Bytes);
        for (var i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            var bytes = resource.Bytes.AsSpan(slice.Offset, slice.Length).ToArray();
            var id = slices.Count == 1 ? resource.Name : $"{resource.Name}:{i + 1}";
            result.Add(new IconAsset(path, "RT_RCDATA_PNG", id, slice.Width, slice.Height, slice.Sha256, bytes));
        }
    }

    private static void AddAsset(string path, string type, string id, byte[] png, int width, int height, ICollection<IconAsset> result)
    {
        var hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        result.Add(new IconAsset(path, type, id, width, height, hash, png));
    }

    private static bool StartsWithPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private sealed record GroupIconEntry(ushort IconResourceId, int Width, int Height);
}
