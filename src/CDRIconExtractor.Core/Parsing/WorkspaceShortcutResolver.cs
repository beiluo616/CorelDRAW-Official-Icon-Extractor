using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Core.Parsing;

public sealed class WorkspaceShortcutResolver
{
    private static readonly string[] ShortcutAttributeNames = ["key", "value", "sequence", "keys", "shortcut", "keySequence"];
    private static readonly string[] GuidAttributeNames = ["guid", "guidRef", "commandGuid", "commandGuidRef", "refGuid", "idGuid", "id"];

    public IReadOnlyList<DrawUiCommand> Enrich(IEnumerable<DrawUiCommand> commands, IEnumerable<string> workspacePaths)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(workspacePaths);

        var commandList = commands.ToArray();
        if (commandList.Length == 0)
            return commandList;

        var shortcuts = ReadShortcuts(workspacePaths);
        if (shortcuts.Count == 0)
            return commandList;

        var result = new DrawUiCommand[commandList.Length];
        for (var i = 0; i < commandList.Length; i++)
        {
            var command = commandList[i];
            if (!string.IsNullOrWhiteSpace(command.Shortcut))
            {
                result[i] = command;
                continue;
            }

            var shortcut = CandidateGuids(command)
                .Select(guid => shortcuts.TryGetValue(guid, out var value) ? value : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            result[i] = shortcut is null ? command : command with { Shortcut = shortcut };
        }

        return result;
    }

    public IReadOnlyDictionary<string, string> ReadShortcuts(IEnumerable<string> workspacePaths)
    {
        ArgumentNullException.ThrowIfNull(workspacePaths);
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in workspacePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
                var entry = archive.Entries.FirstOrDefault(x =>
                    x.FullName.Replace('\\', '/').Equals("content/workspace.xml", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                    continue;

                using var stream = entry.Open();
                var document = XDocument.Load(stream, LoadOptions.None);
                foreach (var keySequence in document.Descendants().Where(x => x.Name.LocalName.Equals("keySequence", StringComparison.OrdinalIgnoreCase)))
                {
                    var shortcut = ExtractShortcut(keySequence);
                    if (string.IsNullOrWhiteSpace(shortcut))
                        continue;

                    var guid = ExtractGuid(keySequence) ?? keySequence.Ancestors().Select(ExtractGuid).FirstOrDefault(x => x is not null);
                    if (guid is null)
                        continue;

                    if (!values.TryGetValue(guid, out var list))
                    {
                        list = [];
                        values[guid] = list;
                    }
                    if (!list.Contains(shortcut, StringComparer.OrdinalIgnoreCase))
                        list.Add(shortcut);
                }
            }
            catch
            {
                // A workspace can be locked, partially written, or use a schema that this
                // release does not understand. Shortcut enrichment is best-effort only.
            }
        }

        return values.ToDictionary(
            pair => pair.Key,
            pair => string.Join(" / ", pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CandidateGuids(DrawUiCommand command)
    {
        foreach (var raw in new[] { command.Guid, command.GuidRef })
        {
            var normalized = CrlIconGuidMapParser.NormalizeGuid(raw);
            if (normalized is not null)
                yield return normalized;
        }
    }

    private static string? ExtractGuid(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            if (!GuidAttributeNames.Contains(attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase))
                continue;
            var normalized = CrlIconGuidMapParser.NormalizeGuid(attribute.Value);
            if (normalized is not null)
                return normalized;
        }

        foreach (var attribute in element.Attributes())
        {
            var match = Regex.Match(attribute.Value, @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}");
            if (match.Success)
                return CrlIconGuidMapParser.NormalizeGuid(match.Value);
        }
        return null;
    }

    private static string? ExtractShortcut(XElement element)
    {
        foreach (var name in ShortcutAttributeNames)
        {
            var value = element.Attributes().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (IsShortcut(value))
                return value;
        }

        var text = element.Value.Trim();
        return IsShortcut(text) ? text : null;
    }

    private static bool IsShortcut(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            return false;
        return value.Any(char.IsLetterOrDigit) && !Guid.TryParse(value.Trim('{', '}'), out _);
    }
}
