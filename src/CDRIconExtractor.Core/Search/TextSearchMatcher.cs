using System.Text;

namespace CDRIconExtractor.Core.Search;

public static class TextSearchMatcher
{
    private static readonly IReadOnlyDictionary<string, string[]> QueryAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Normalize("Ctrl+Q")] = ["Convert to Curves", "转换为曲线", "转为曲线", "转曲"],
            [Normalize("Ctrl+Shift+Q")] = ["Convert Outline to Object", "轮廓转对象"],
            [Normalize("Ctrl+G")] = ["Group", "群组"],
            [Normalize("Ctrl+U")] = ["Ungroup", "解组", "取消群组"],
            [Normalize("Ctrl+L")] = ["Combine", "合并"],
            [Normalize("Ctrl+K")] = ["Break Apart", "打散"],
            [Normalize("Ctrl+I")] = ["Import", "导入"],
            [Normalize("Ctrl+E")] = ["Export", "导出"]
        };


    public static string CreateSearchDocument(IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join("\u001F", values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Normalize(x!))
            .Where(x => x.Length > 0));
    }

    public static bool MatchesDocument(string searchDocument, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        if (string.IsNullOrEmpty(searchDocument))
            return false;

        foreach (var candidate in ExpandQuery(query))
        {
            var normalized = Normalize(candidate);
            if (normalized.Length > 0 && searchDocument.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool MatchesAny(IEnumerable<string?> values, string? query)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var candidates = ExpandQuery(query).ToArray();
        foreach (var value in values)
        {
            foreach (var candidate in candidates)
            {
                if (MatchesCore(value, candidate))
                    return true;
            }
        }
        return false;
    }

    public static bool Matches(string? value, string query)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query))
            return false;

        foreach (var candidate in ExpandQuery(query))
        {
            if (MatchesCore(value, candidate))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> ExpandQuery(string query)
    {
        var trimmed = query.Trim();
        yield return trimmed;

        var normalized = Normalize(trimmed);
        if (QueryAliases.TryGetValue(normalized, out var aliases))
        {
            foreach (var alias in aliases)
                yield return alias;
        }

        foreach (var alias in ChineseSearchAliases.Expand(normalized))
            yield return alias;
    }

    private static bool MatchesCore(string? value, string query)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query))
            return false;

        if (value.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedValue = Normalize(value);
        var normalizedQuery = Normalize(query);
        if (normalizedValue.Length == 0 || normalizedQuery.Length == 0)
            return false;

        if (normalizedValue.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            return true;

        // Descriptive abbreviations (3+ characters) may omit intermediate characters.
        // Very short 2-character fuzzy matching stays disabled to avoid noisy results;
        // common CorelDRAW terms such as “转曲” are handled by the Chinese alias dictionary.
        return normalizedQuery.Length >= 3 && IsOrderedSubsequence(normalizedValue, normalizedQuery);
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch > 127)
                builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString();
    }

    private static bool IsOrderedSubsequence(string value, string query)
    {
        var queryIndex = 0;
        foreach (var valueChar in value)
        {
            if (CharsEqual(valueChar, query[queryIndex]))
            {
                queryIndex++;
                if (queryIndex == query.Length)
                    return true;
            }
        }
        return false;
    }

    private static bool CharsEqual(char left, char right) =>
        char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
