using System.IO.Compression;
using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Core.Parsing;

/// <summary>
/// Reads CorelDRAW's modern .crlicons container. Current v27 archives are ZIP files
/// containing PNG resources, commonly grouped as /24.png, /48.png and /72.png.
/// </summary>
public static class ModernCrlIconsReader
{
    private static readonly int[] KnownVariantSizes = [24, 48, 72];

    public static IReadOnlyList<IconAsset> Read(string path, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Modern.crlicons not found.", path);

        using var archive = ZipFile.OpenRead(path);
        var groups = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Length <= 0 || !entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                continue;

            var key = LogicalResourcePath(entry.FullName);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups.Add(key, list);
            }
            list.Add(entry);
        }

        var result = new List<IconAsset>(groups.Count);
        foreach (var pair in groups.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var variants = new List<IconAssetVariant>();
            foreach (var entry in pair.Value)
            {
                token.ThrowIfCancellationRequested();
                using var stream = entry.Open();
                using var memory = new MemoryStream(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
                stream.CopyTo(memory);
                var bytes = memory.ToArray();
                var slice = PngStreamScanner.Find(bytes).FirstOrDefault(x => x.Offset == 0 && x.Length == bytes.Length);
                if (slice is null)
                    continue;

                variants.Add(new IconAssetVariant(entry.FullName, slice.Width, slice.Height, slice.Sha256, bytes));
            }

            if (variants.Count == 0)
                continue;

            variants = variants
                .OrderBy(x => x.Width)
                .ThenBy(x => x.Height)
                .ThenBy(x => x.ArchiveEntry, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var primary = variants
                .OrderByDescending(x => (long)x.Width * x.Height)
                .ThenByDescending(x => x.Width)
                .First();
            var displayName = DisplayNameFromResourcePath(pair.Key);

            result.Add(new IconAsset(
                path,
                "ModernCrlIcons",
                displayName,
                primary.Width,
                primary.Height,
                primary.Sha256,
                primary.PngBytes)
            {
                DisplayName = displayName,
                ResourcePath = pair.Key,
                Variants = variants
            });
        }

        return result;
    }

    private static string LogicalResourcePath(string fullName)
    {
        var normalized = fullName.Replace('\\', '/').Trim('/');
        foreach (var size in KnownVariantSizes)
        {
            var suffix = $"/{size}.png";
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return normalized[..^suffix.Length];
        }
        return normalized;
    }

    private static string DisplayNameFromResourcePath(string resourcePath)
    {
        var normalized = resourcePath.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        var name = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        foreach (var suffix in new[] { ".ico.png", ".png", ".ico" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }
        return string.IsNullOrWhiteSpace(name) ? "ModernIcon" : name;
    }
}
