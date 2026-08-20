using System.Xml.Linq;
using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Core.Parsing;

public sealed class DrawUiParser
{
    private static readonly string[] GuidNames = ["guid", "commandGuid", "idGuid"];
    private static readonly string[] GuidRefNames = ["guidRef", "commandGuidRef", "refGuid"];
    private static readonly string[] CaptionNames = ["caption", "userCaption", "captionText", "text"];
    private static readonly string[] LocalizedCaptionNames = ["localizedCaption", "localCaption", "captionLocalization", "displayName"];
    private static readonly string[] ShortcutNames = ["shortcut", "key", "keySequence", "accelerator", "shortcutText"];
    private static readonly HashSet<string> ResourceHintNames = new(
        ["bmpRow", "bmpCol", "image", "imageGuid", "icon", "iconGuid", "resource", "resourceId"],
        StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DrawUiCommand> Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var document = XDocument.Load(stream, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        if (document.Root is null)
            return Array.Empty<DrawUiCommand>();

        var result = new List<DrawUiCommand>();
        foreach (var element in document.Root.DescendantsAndSelf())
        {
            var guid = Attr(element, GuidNames) ?? ResourceEntryGuid(element);
            var guidRef = Attr(element, GuidRefNames);
            var caption = Attr(element, CaptionNames) ?? FindCaptionResourceExpression(element);
            var localizedCaption = Attr(element, LocalizedCaptionNames);
            var shortcut = Attr(element, ShortcutNames);
            var hints = element.Attributes()
                .Where(x => ResourceHintNames.Contains(x.Name.LocalName))
                .Select(x => new ResourceHint(x.Name.LocalName, x.Value.Trim()))
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToArray();

            if (guid is null && guidRef is null && caption is null && localizedCaption is null && shortcut is null && hints.Length == 0)
                continue;

            result.Add(new DrawUiCommand(
                guid,
                guidRef,
                caption,
                localizedCaption,
                shortcut,
                element.Name.LocalName,
                hints,
                BuildXmlPath(element)));
        }

        return result;
    }


    private static string? ResourceEntryGuid(XElement element)
    {
        if (!element.Name.LocalName.Equals("resEntry", StringComparison.OrdinalIgnoreCase) &&
            !element.Name.LocalName.Equals("resourceEntry", StringComparison.OrdinalIgnoreCase))
            return null;

        var value = Attr(element, "id");
        return CrlIconGuidMapParser.NormalizeGuid(value) is null ? null : value;
    }

    private static string? FindCaptionResourceExpression(XElement element) =>
        element.Attributes()
            .Select(attribute => attribute.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
                                     value.Contains("*CT(", StringComparison.OrdinalIgnoreCase));

    private static string? Attr(XElement element, params string[] names) =>
        element.Attributes()
            .FirstOrDefault(a => names.Contains(a.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            ?.Value
            ?.Trim() is { Length: > 0 } value ? value : null;

    private static string BuildXmlPath(XElement element)
    {
        var segments = element.AncestorsAndSelf()
            .Reverse()
            .Select(current =>
            {
                if (current.Parent is null)
                    return current.Name.LocalName;
                var siblings = current.Parent.Elements(current.Name).ToArray();
                var index = Array.IndexOf(siblings, current) + 1;
                return siblings.Length > 1 ? $"{current.Name.LocalName}[{index}]" : current.Name.LocalName;
            });

        return "/" + string.Join("/", segments);
    }
}
