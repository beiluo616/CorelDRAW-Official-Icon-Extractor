using System.Text.RegularExpressions;
using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Core.Parsing;

public interface IUiCaptionProvider
{
    string? GetCaptionText(string guid);
}

public sealed record LiveCaptionResult(
    IReadOnlyList<DrawUiCommand> Commands,
    int RequestCount,
    int ResolvedCount);

public sealed class LiveCaptionResolver
{
    private static readonly Regex TrailingMnemonicRegex = new(@"\s*\(&.\)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public LiveCaptionResult Enrich(
        IEnumerable<DrawUiCommand> commands,
        IUiCaptionProvider provider,
        int maxRequests,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(provider);
        if (maxRequests < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRequests));

        var result = commands.ToArray();
        if (result.Length == 0 || maxRequests == 0)
            return new LiveCaptionResult(result, 0, 0);

        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var requestCount = 0;
        var resolvedCount = 0;
        var order = Enumerable.Range(0, result.Length)
            .OrderByDescending(index => NeedsChineseCaption(result[index]))
            .ThenByDescending(index => result[index].ResourceHints.Count > 0)
            .ThenByDescending(index => !string.IsNullOrWhiteSpace(result[index].Shortcut))
            .ToArray();

        foreach (var index in order)
        {
            token.ThrowIfCancellationRequested();
            var command = result[index];
            if (!NeedsAnyCaption(command))
                continue;

            string? caption = null;
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
                        value = NormalizeCaption(provider.GetCaptionText(guid));
                    }
                    catch
                    {
                        value = null;
                    }
                    cache[guid] = value;
                }

                if (!IsHumanText(value))
                    continue;
                caption = value;
                break;
            }

            if (caption is null)
            {
                if (requestCount >= maxRequests)
                    break;
                continue;
            }

            DrawUiCommand updated;
            if (HasCjk(caption))
            {
                updated = command with { LocalizedCaption = caption };
            }
            else if (!HasReadable(command.Caption))
            {
                updated = command with { Caption = caption };
            }
            else
            {
                continue;
            }

            if (!Equals(updated, command))
            {
                result[index] = updated;
                resolvedCount++;
            }
        }

        return new LiveCaptionResult(result, requestCount, resolvedCount);
    }

    private static IEnumerable<string> CandidateGuids(DrawUiCommand command)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[] { command.Guid, command.GuidRef })
        {
            var normalized = CrlIconGuidMapParser.NormalizeGuid(raw);
            if (normalized is not null && seen.Add(normalized))
                yield return normalized;
        }
    }

    private static bool NeedsAnyCaption(DrawUiCommand command) =>
        NeedsChineseCaption(command) || !HasReadable(command.Caption);

    private static bool NeedsChineseCaption(DrawUiCommand command) => !HasCjk(command.LocalizedCaption);

    private static bool HasReadable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        return !trimmed.StartsWith("*CT(", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.StartsWith("*TT(", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.StartsWith("*ST(", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeCaption(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = TrailingMnemonicRegex.Replace(value.Trim(), string.Empty);
        const char placeholder = '\u0001';
        text = text.Replace("&&", placeholder.ToString(), StringComparison.Ordinal)
                   .Replace("&", string.Empty, StringComparison.Ordinal)
                   .Replace(placeholder.ToString(), "&", StringComparison.Ordinal)
                   .Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool IsHumanText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 512 && value.Any(char.IsLetter) && !Guid.TryParse(value.Trim('{', '}'), out _);

    private static bool HasCjk(string? value) => value?.Any(ch =>
        ch is >= '\u3400' and <= '\u9FFF' || ch is >= '\uF900' and <= '\uFAFF') == true;
}
