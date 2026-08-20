namespace CDRIconExtractor.Core.Utilities;

public static class IconGuidReference
{
    private const string GuidScheme = "guid://";

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var candidate = value.Trim();
        if (candidate.StartsWith(GuidScheme, StringComparison.OrdinalIgnoreCase))
            candidate = candidate[GuidScheme.Length..].Trim();

        candidate = candidate.Trim('"', '\'', ' ', '\t', '\r', '\n');
        return Guid.TryParse(candidate, out var guid)
            ? guid.ToString("D").ToLowerInvariant()
            : null;
    }

    public static string? FormatUri(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null ? null : $"{GuidScheme}{normalized}";
    }

    public static string? FormatIconAttribute(string? value)
    {
        var uri = FormatUri(value);
        return uri is null ? null : $"icon=\"{uri}\"";
    }
}
