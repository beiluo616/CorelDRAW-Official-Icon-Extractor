using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Core.Parsing;

public sealed record ModernIconMapBindResult(
    IReadOnlyList<IconAsset> Assets,
    int TotalMapEntries,
    int ReusableGuidEntries,
    int MatchedMapEntries,
    int MatchedReusableGuidEntries,
    int MatchedResourceCount,
    int UnmatchedResourceCount);

/// <summary>
/// Applies icons.map.xml GUID metadata to already extracted Modern.crlicons assets.
/// Matching is exact and case-insensitive after slash normalization and the official
/// duplicated-.png quirk repair.
/// </summary>
public static class ModernIconMapBinder
{
    public static ModernIconMapBindResult Bind(
        IEnumerable<IconAsset> assets,
        IEnumerable<IconMapEntry> entries,
        string iconMapSource)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconMapSource);

        var assetList = assets.ToArray();
        var entryList = entries.ToArray();
        var byPath = entryList
            .Where(x => !string.IsNullOrWhiteSpace(x.ResourcePath))
            .GroupBy(x => IconMapXmlParser.NormalizeResourcePath(x.ResourcePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);

        var assetPaths = assetList
            .Select(x => IconMapXmlParser.NormalizeResourcePath(x.ResourcePath ?? string.Empty))
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<IconAsset>(assetList.Length);
        var matchedEntryKeys = new HashSet<(string RawGuid, string Path)>();
        var matchedResources = 0;

        foreach (var asset in assetList)
        {
            var path = IconMapXmlParser.NormalizeResourcePath(asset.ResourcePath ?? string.Empty);
            if (path.Length == 0 || !byPath.TryGetValue(path, out var mapped))
            {
                result.Add(asset);
                continue;
            }

            matchedResources++;
            foreach (var entry in mapped)
                matchedEntryKeys.Add((entry.RawGuid, entry.ResourcePath));

            var guids = mapped
                .Select(x => x.Guid)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            result.Add(asset with
            {
                IconGuids = guids,
                IconGuidSource = iconMapSource
            });
        }

        var matchedReusable = entryList.Count(x =>
            x.Guid is not null && matchedEntryKeys.Contains((x.RawGuid, x.ResourcePath)));
        var unmatchedResourceCount = byPath.Keys.Count(path => !assetPaths.Contains(path));

        return new ModernIconMapBindResult(
            result,
            entryList.Length,
            entryList.Count(x => x.IsReusableGuid),
            matchedEntryKeys.Count,
            matchedReusable,
            matchedResources,
            unmatchedResourceCount);
    }
}
