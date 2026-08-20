using System.Text.RegularExpressions;
using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Core.Parsing;

public interface ILocalizedStringProvider
{
    string? LoadLocalizedString(string guid);
}

public sealed record LiveLocalizationResult(
    IReadOnlyList<DrawUiCommand> Commands,
    int RequestCount,
    int ResolvedCount);

public sealed class LiveLocalizedStringResolver
{
    private static readonly Regex StringReferenceRegex = new(
        @"\*(?:CT|TT|ST)\s*\(\s*['""]?\{?(?<guid>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}?['""]?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public LiveLocalizationResult Enrich(
        IEnumerable<DrawUiCommand> commands,
        ILocalizedStringProvider provider,
        int maxRequests,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(provider);
        if (maxRequests < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRequests));

        var result = commands.ToArray();
        if (result.Length == 0 || maxRequests == 0)
            return new LiveLocalizationResult(result, 0, 0);

        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var requestCount = 0;
        var resolvedCount = 0;

        var order = Enumerable.Range(0, result.Length)
            .OrderByDescending(index => HasStringReference(result[index]))
            .ThenByDescending(index => result[index].ResourceHints.Count > 0)
            .ThenByDescending(index => !string.IsNullOrWhiteSpace(result[index].Shortcut))
            .ToArray();

        foreach (var index in order)
        {
            token.ThrowIfCancellationRequested();
            var command = result[index];
            if (HasCjk(command.LocalizedCaption))
                continue;

            string? localized = null;
            foreach (var guid in CandidateGuids(command))
            {
                token.ThrowIfCancellationRequested();
                if (!cache.TryGetValue(guid, out var value))
                {
                    if (requestCount >= maxRequests)
                        break;
                    requestCount++;
                    try
                    {
                        value = provider.LoadLocalizedString(guid)?.Trim();
                    }
                    catch
                    {
                        value = null;
                    }
                    cache[guid] = value;
                }

                if (!IsHumanText(value))
                    continue;

                localized = value;
                if (HasCjk(value))
                    break;
            }

            if (localized is null)
            {
                if (requestCount >= maxRequests)
                    break;
                continue;
            }

            var updated = HasCjk(localized)
                ? command with { LocalizedCaption = localized }
                : string.IsNullOrWhiteSpace(command.Caption) || HasStringReference(command)
                    ? command with { Caption = localized }
                    : command;
            if (!Equals(updated, command))
            {
                result[index] = updated;
                resolvedCount++;
            }
        }

        return new LiveLocalizationResult(result, requestCount, resolvedCount);
    }

    private static IEnumerable<string> CandidateGuids(DrawUiCommand command)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[] { command.Caption, command.LocalizedCaption })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            foreach (Match match in StringReferenceRegex.Matches(raw))
            {
                var normalized = CrlIconGuidMapParser.NormalizeGuid(match.Groups["guid"].Value);
                if (normalized is not null && seen.Add(normalized))
                    yield return normalized;
            }
        }

        foreach (var raw in new[] { command.Guid, command.GuidRef })
        {
            var normalized = CrlIconGuidMapParser.NormalizeGuid(raw);
            if (normalized is not null && seen.Add(normalized))
                yield return normalized;
        }
    }

    private static bool HasStringReference(DrawUiCommand command) =>
        (!string.IsNullOrWhiteSpace(command.Caption) && StringReferenceRegex.IsMatch(command.Caption)) ||
        (!string.IsNullOrWhiteSpace(command.LocalizedCaption) && StringReferenceRegex.IsMatch(command.LocalizedCaption));

    private static bool IsHumanText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 512 && value.Any(char.IsLetter) && !Guid.TryParse(value.Trim('{', '}'), out _);

    private static bool HasCjk(string? value) => value?.Any(ch =>
        ch is >= '\u3400' and <= '\u9FFF' || ch is >= '\uF900' and <= '\uFAFF') == true;
}
