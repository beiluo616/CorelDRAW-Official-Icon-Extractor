using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Core.Search;

public sealed class SearchIndex
{
    public IReadOnlyList<IconAssociation> Filter(IEnumerable<IconAssociation> items, string? query)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (string.IsNullOrWhiteSpace(query))
            return items.ToArray();

        var needle = query.Trim();
        return items.Where(item => Matches(item, needle)).ToArray();
    }

    private static bool Matches(IconAssociation association, string query)
    {
        var command = association.Command;
        return TextSearchMatcher.MatchesAny(EnumerateValues(command, association.Asset), query);
    }

    private static IEnumerable<string?> EnumerateValues(DrawUiCommand command, IconAsset? asset)
    {
        yield return command.LocalizedCaption;
        yield return command.Caption;
        yield return command.Guid;
        yield return command.GuidRef;
        yield return command.Shortcut;
        yield return command.ElementName;
        yield return command.XmlPath;

        foreach (var hint in command.ResourceHints)
        {
            yield return hint.Name;
            yield return hint.Value;
        }

        if (asset is not null)
        {
            yield return asset.ResourceId;
            yield return asset.ResourceType;
            yield return asset.SourceFile;
            yield return Path.GetFileName(asset.SourceFile);
        }
    }
}
