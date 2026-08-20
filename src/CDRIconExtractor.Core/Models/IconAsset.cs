namespace CDRIconExtractor.Core.Models;

public sealed record IconAssetVariant(
    string ArchiveEntry,
    int Width,
    int Height,
    string Sha256,
    byte[] PngBytes);

public sealed record IconAsset(
    string SourceFile,
    string ResourceType,
    string ResourceId,
    int Width,
    int Height,
    string Sha256,
    byte[] PngBytes)
{
    /// <summary>Human-readable resource name when the source format exposes one.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Stable path/name inside the source container, if available.</summary>
    public string? ResourcePath { get; init; }

    /// <summary>Alternative source-native sizes for the same logical icon.</summary>
    public IReadOnlyList<IconAssetVariant> Variants { get; init; } = Array.Empty<IconAssetVariant>();

    /// <summary>Reusable official icon GUIDs confirmed by icons.map.xml or another authoritative source.</summary>
    public IReadOnlyList<string> IconGuids { get; init; } = Array.Empty<string>();

    /// <summary>File that established IconGuids, for example icons.map.xml.</summary>
    public string? IconGuidSource { get; init; }
}
