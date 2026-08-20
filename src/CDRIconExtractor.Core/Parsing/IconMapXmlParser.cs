using System.Xml.Linq;
using CDRIconExtractor.Core.Utilities;

namespace CDRIconExtractor.Core.Parsing;

public sealed record IconMapEntry(
    string RawGuid,
    string? Guid,
    string ResourcePath)
{
    public bool IsReusableGuid => Guid is not null;
}

/// <summary>
/// Reads CorelDRAW 2026+ icons.map.xml. The file is the authoritative bridge
/// between Corel's icon GUID keys and paths stored in Modern.crlicons.
/// </summary>
public static class IconMapXmlParser
{
    public static IReadOnlyList<IconMapEntry> Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("icons.map.xml not found.", path);

        using var stream = File.OpenRead(path);
        var document = XDocument.Load(stream, LoadOptions.None);
        return Parse(document);
    }

    internal static IReadOnlyList<IconMapEntry> Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<IconMapEntry>();
        foreach (var element in document.Descendants().Where(x => x.Name.LocalName.Equals("map", StringComparison.OrdinalIgnoreCase)))
        {
            var rawGuid = element.Attributes()
                .FirstOrDefault(x => x.Name.LocalName.Equals("guid", StringComparison.OrdinalIgnoreCase))
                ?.Value.Trim();
            var resourcePath = element.Value.Trim();
            if (string.IsNullOrWhiteSpace(rawGuid) || string.IsNullOrWhiteSpace(resourcePath))
                continue;

            result.Add(new IconMapEntry(
                rawGuid,
                IconGuidReference.Normalize(rawGuid),
                NormalizeResourcePath(resourcePath)));
        }
        return result;
    }

    internal static string NormalizeResourcePath(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        // A small number of official map entries contain a duplicated .png suffix.
        // Repair only this known deterministic quirk; do not use fuzzy path matching.
        while (normalized.EndsWith(".png.png", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];
        return normalized;
    }
}
