using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;
using CDRIconExtractor.Core.Utilities;

namespace CDRIconExtractor.Core.Association;

public sealed record ExternalCrlIconsCatalog(
    IReadOnlyList<DrawUiCommand> Commands,
    IReadOnlyList<IconAssociation> Associations);

/// <summary>
/// Builds a browseable catalog from a standalone CrlIcons.dll.  This mode does
/// not require CorelDRAW to be installed: it exposes icon previews, resource IDs
/// and any GUIDs present in the DLL's own resource map.
/// </summary>
public static class ExternalCrlIconsCatalogBuilder
{
    public static ExternalCrlIconsCatalog Build(
        IReadOnlyList<IconAsset> assets,
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> guidMap)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(guidMap);

        var commands = new List<DrawUiCommand>(assets.Count);
        var associations = new List<IconAssociation>(assets.Count);

        foreach (var asset in assets)
        {
            var id = asset.ResourceId?.Trim() ?? string.Empty;
            var mapped = TryGetMappedGuids(id, guidMap);
            var iconGuid = mapped
                .Select(IconGuidReference.Normalize)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            var command = new DrawUiCommand(
                null,
                null,
                $"Resource {id}",
                $"图标资源 {id}",
                null,
                "resource",
                Array.Empty<ResourceHint>(),
                asset.SourceFile);

            commands.Add(command);
            associations.Add(new IconAssociation(
                command,
                asset,
                iconGuid is null ? AssociationConfidence.Unmapped : AssociationConfidence.Exact,
                iconGuid is null
                    ? $"External CrlIcons resource id={id}; no GUID mapping found"
                    : $"External CrlIcons GUID map id={id}; refs={mapped.Count}",
                iconGuid,
                id));
        }

        return new ExternalCrlIconsCatalog(commands, associations);
    }

    private static IReadOnlyList<string> TryGetMappedGuids(
        string resourceId,
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> guidMap)
    {
        if (!ushort.TryParse(resourceId, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var id))
            return Array.Empty<string>();
        return guidMap.TryGetValue(id, out var mapped) ? mapped : Array.Empty<string>();
    }
}
