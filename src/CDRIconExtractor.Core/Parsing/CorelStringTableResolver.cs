using System.Text.RegularExpressions;
using System.Xml.Linq;
using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Core.Parsing;

public sealed class CorelStringTableResolver
{
    private static readonly Regex GuidRegex = new(
        @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CaptionTokenRegex = new(
        @"(?:^|[\u0001|;])\s*CT\s*=\s*(?<value>[^\u0001|;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // DrawUI frequently stores a caption as a resource expression such as
    // *CT('{GUID}') instead of storing the human-readable text on the item itself.
    private static readonly Regex StringReferenceRegex = new(
        @"\*(?:CT|TT|ST)\s*\(\s*['""]?\{?(?<guid>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}?['""]?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private sealed record LocalizedValue(string Language, string Text);

    public IReadOnlyList<DrawUiCommand> Enrich(
        IEnumerable<DrawUiCommand> commands,
        string? stringsMapPath,
        IEnumerable<string> languageStringFiles)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(languageStringFiles);

        var commandList = commands.ToArray();
        if (commandList.Length == 0)
            return commandList;

        var mapPairs = ReadMapPairs(stringsMapPath);
        var strings = ReadLocalizedStrings(languageStringFiles);
        if (strings.Count == 0)
            return commandList;

        var forward = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in mapPairs)
        {
            forward.TryAdd(pair.Source, pair.Target);
            reverse.TryAdd(pair.Target, pair.Source);
        }

        var result = new DrawUiCommand[commandList.Length];
        for (var i = 0; i < commandList.Length; i++)
        {
            var command = commandList[i];
            var stringGuid = ResolveStringGuid(command, strings, forward, reverse);
            if (stringGuid is null || !strings.TryGetValue(stringGuid, out var values) || values.Count == 0)
            {
                result[i] = command;
                continue;
            }

            var english = values.FirstOrDefault(x => IsEnglishLanguage(x.Language))?.Text
                          ?? values.FirstOrDefault(x => IsMostlyLatin(x.Text))?.Text;
            var cjk = values.FirstOrDefault(x => IsChineseLanguage(x.Language) && ContainsCjk(x.Text))?.Text
                      ?? values.FirstOrDefault(x => ContainsCjk(x.Text))?.Text;
            var fallback = values[0].Text;

            // Do not expose resource expressions such as *CT('{GUID}') as a command name.
            var existingCaption = IsDisplayCaption(command.Caption) ? command.Caption : null;
            var existingLocalized = IsDisplayCaption(command.LocalizedCaption) ? command.LocalizedCaption : null;

            result[i] = command with
            {
                Caption = existingCaption ?? english ?? fallback,
                LocalizedCaption = existingLocalized ?? cjk
            };
        }

        return result;
    }

    private static string? ResolveStringGuid(
        DrawUiCommand command,
        IReadOnlyDictionary<string, List<LocalizedValue>> strings,
        IReadOnlyDictionary<string, string> forward,
        IReadOnlyDictionary<string, string> reverse)
    {
        // Most reliable path: a caption/tool-tip expression already names the string GUID.
        foreach (var raw in new[] { command.Caption, command.LocalizedCaption })
        {
            foreach (var referenced in ExtractStringReferenceGuids(raw))
            {
                if (strings.ContainsKey(referenced))
                    return referenced;
                if (forward.TryGetValue(referenced, out var target) && strings.ContainsKey(target))
                    return target;
                if (reverse.TryGetValue(referenced, out var source) && strings.ContainsKey(source))
                    return source;
            }
        }

        // Fallback used by releases where strings.map.xml maps the DrawUI command GUID
        // to a separate localization GUID.
        foreach (var raw in new[] { command.Guid, command.GuidRef })
        {
            var normalized = CrlIconGuidMapParser.NormalizeGuid(raw);
            if (normalized is null)
                continue;

            if (strings.ContainsKey(normalized))
                return normalized;

            if (forward.TryGetValue(normalized, out var target) && strings.ContainsKey(target))
                return target;

            if (reverse.TryGetValue(normalized, out var source) && strings.ContainsKey(source))
                return source;
        }

        return null;
    }

    private static IReadOnlyList<(string Source, string Target)> ReadMapPairs(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Array.Empty<(string, string)>();

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var document = XDocument.Load(stream, LoadOptions.None);
            var result = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddPair(string? source, string? target)
            {
                if (source is null || target is null || source.Equals(target, StringComparison.OrdinalIgnoreCase))
                    return;
                var key = source + "|" + target;
                if (seen.Add(key))
                    result.Add((source, target));
            }

            foreach (var element in document.Descendants())
            {
                var own = ExtractGuids(element).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
                if (own.Length >= 2)
                {
                    AddPair(own[0], own[1]);
                    continue;
                }

                if (own.Length == 1 && element.Parent is not null)
                {
                    var parent = ExtractGuids(element.Parent).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    AddPair(parent, own[0]);
                }
            }

            // Some releases nest the two GUIDs across XML levels in a way that doesn't
            // survive element-local parsing. Fall back to adjacent GUIDs in document order.
            if (result.Count == 0)
            {
                var ordered = GuidRegex.Matches(document.ToString(SaveOptions.DisableFormatting))
                    .Select(x => CrlIconGuidMapParser.NormalizeGuid(x.Value))
                    .Where(x => x is not null)
                    .Cast<string>()
                    .ToArray();
                for (var i = 0; i + 1 < ordered.Length; i += 2)
                    AddPair(ordered[i], ordered[i + 1]);
            }

            return result;
        }
        catch
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static Dictionary<string, List<LocalizedValue>> ReadLocalizedStrings(IEnumerable<string> files)
    {
        var result = new Dictionary<string, List<LocalizedValue>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var language = DetectLanguage(path);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                if (document.Root is null)
                    continue;

                foreach (var element in document.Root.DescendantsAndSelf())
                {
                    var ownGuid = ExtractGuids(element).FirstOrDefault();
                    var ownText = ExtractHumanText(element);

                    if (ownGuid is not null && !string.IsNullOrWhiteSpace(ownText))
                        AddLocalizedValue(result, ownGuid, language, ownText);

                    // Real Corel language packs are not consistent across releases. In
                    // several schemas the GUID is on the parent <string>/<entry> while
                    // the text lives in a nested <text>/<value> element. Associate such
                    // leaf text with the nearest ancestor carrying a GUID.
                    if (ownGuid is null && !string.IsNullOrWhiteSpace(ownText))
                    {
                        var ownerGuid = element.Ancestors()
                            .Select(x => ExtractGuids(x).FirstOrDefault())
                            .FirstOrDefault(x => x is not null);
                        if (ownerGuid is not null)
                            AddLocalizedValue(result, ownerGuid, language, ownText);
                    }
                }
            }
            catch
            {
                // Individual language packs may be absent, locked, or use an unsupported schema.
                // Enrichment is best-effort and must never make the core icon scan fail.
            }
        }

        return result;
    }

    private static void AddLocalizedValue(
        IDictionary<string, List<LocalizedValue>> result,
        string guid,
        string language,
        string text)
    {
        var normalized = CrlIconGuidMapParser.NormalizeGuid(guid);
        if (normalized is null)
            return;

        var value = ExtractCaptionToken(text) ?? text.Trim();
        if (!IsHumanText(value))
            return;

        if (!result.TryGetValue(normalized, out var values))
        {
            values = [];
            result[normalized] = values;
        }

        if (!values.Any(x => string.Equals(x.Language, language, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(x.Text, value, StringComparison.Ordinal)))
            values.Add(new LocalizedValue(language, value));
    }

    private static IEnumerable<string> ExtractGuids(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            foreach (Match match in GuidRegex.Matches(attribute.Value))
            {
                var normalized = CrlIconGuidMapParser.NormalizeGuid(match.Value);
                if (normalized is not null)
                    yield return normalized;
            }
        }

        if (!element.HasElements)
        {
            foreach (Match match in GuidRegex.Matches(element.Value))
            {
                var normalized = CrlIconGuidMapParser.NormalizeGuid(match.Value);
                if (normalized is not null)
                    yield return normalized;
            }
        }
    }

    private static IEnumerable<string> ExtractStringReferenceGuids(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (Match match in StringReferenceRegex.Matches(value))
        {
            var normalized = CrlIconGuidMapParser.NormalizeGuid(match.Groups["guid"].Value);
            if (normalized is not null)
                yield return normalized;
        }
    }

    private static string? ExtractHumanText(XElement element)
    {
        var preferredNames = new[] { "value", "text", "caption", "string", "content", "displayName", "name" };
        foreach (var name in preferredNames)
        {
            var value = element.Attributes().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            value = ExtractCaptionToken(value) ?? value;
            if (IsHumanText(value))
                return value;
        }

        foreach (var attribute in element.Attributes())
        {
            var value = attribute.Value.Trim();
            value = ExtractCaptionToken(value) ?? value;
            if (IsHumanText(value))
                return value;
        }

        // Use only direct text nodes. Descendant text is handled by the nearest-GUID
        // ancestor pass above so that one entry cannot steal another entry's caption.
        foreach (var textNode in element.Nodes().OfType<XText>())
        {
            var value = textNode.Value.Trim();
            value = ExtractCaptionToken(value) ?? value;
            if (IsHumanText(value))
                return value;
        }

        return null;
    }

    private static string? ExtractCaptionToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var match = CaptionTokenRegex.Match(value);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static bool IsDisplayCaption(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !StringReferenceRegex.IsMatch(value);

    private static bool IsHumanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || GuidRegex.IsMatch(value) || StringReferenceRegex.IsMatch(value))
            return false;
        if (value.Length > 512)
            return false;
        return value.Any(char.IsLetter);
    }

    private static string DetectLanguage(string path)
    {
        var directory = new FileInfo(path).Directory;
        while (directory is not null)
        {
            var name = directory.Name;
            if (IsChineseLanguage(name) || IsEnglishLanguage(name) ||
                (name.Length is >= 2 and <= 8 && name.All(ch => char.IsLetter(ch) || ch is '-' or '_')))
            {
                var parentName = directory.Parent?.Name;
                if (IsChineseLanguage(name) || IsEnglishLanguage(name) ||
                    string.Equals(parentName, "Languages", StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            directory = directory.Parent;
        }
        return string.Empty;
    }

    private static bool IsEnglishLanguage(string language) =>
        language.StartsWith("EN", StringComparison.OrdinalIgnoreCase);

    private static bool IsChineseLanguage(string language) =>
        language.Equals("CS", StringComparison.OrdinalIgnoreCase) ||
        language.Equals("CT", StringComparison.OrdinalIgnoreCase) ||
        language.Equals("CHS", StringComparison.OrdinalIgnoreCase) ||
        language.Equals("CHT", StringComparison.OrdinalIgnoreCase) ||
        language.StartsWith("ZH", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCjk(string value) => value.Any(ch =>
        ch is >= '\u3400' and <= '\u9FFF' || ch is >= '\uF900' and <= '\uFAFF');

    private static bool IsMostlyLatin(string value)
    {
        var letters = value.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
            return false;
        return letters.Count(ch => ch <= 0x024F) * 2 >= letters.Length;
    }
}
