namespace CDRIconExtractor.Windows.Resources;

public static class ResourceTypeDiagnostics
{
    public static string Format(IEnumerable<Win32ResourceTypeSummary> summaries, int maxTypes = 12)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        var parts = summaries
            .OrderByDescending(x => x.ResourceCount)
            .ThenBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxTypes))
            .Select(x => x.TypeId is ushort id ? $"#{id}={x.ResourceCount}" : $"{x.TypeName}={x.ResourceCount}")
            .ToArray();
        return parts.Length == 0 ? "无资源类型" : string.Join(", ", parts);
    }
}
