namespace CDRIconExtractor.Core.Models;

public sealed record DrawUiCommand(
    string? Guid,
    string? GuidRef,
    string? Caption,
    string? LocalizedCaption,
    string? Shortcut,
    string ElementName,
    IReadOnlyList<ResourceHint> ResourceHints,
    string XmlPath);
