namespace CDRIconExtractor.Core.Utilities;

public static class FileNameSanitizer
{
    private static readonly HashSet<char> InvalidChars = new("<>:\"/\\|?*".ToCharArray());
    private static readonly HashSet<string> ReservedNames = new(
        new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" },
        StringComparer.OrdinalIgnoreCase);

    public static string Sanitize(string? value, string fallback = "icon", int maxLength = 120)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var chars = text.Select(c => c < 32 || InvalidChars.Contains(c) ? '_' : c).ToArray();
        text = new string(chars).TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(text))
            text = fallback;

        var stem = Path.GetFileNameWithoutExtension(text);
        if (ReservedNames.Contains(stem))
            text = "_" + text;

        if (text.Length > maxLength)
            text = text[..maxLength].TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
}
