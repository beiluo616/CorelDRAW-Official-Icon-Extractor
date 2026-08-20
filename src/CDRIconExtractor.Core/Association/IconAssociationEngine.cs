using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;
using CDRIconExtractor.Core.Utilities;

namespace CDRIconExtractor.Core.Association;

public sealed class IconAssociationEngine
{
    public IReadOnlyList<IconAssociation> Associate(
        IEnumerable<DrawUiCommand> commands,
        IEnumerable<IconAsset> assets,
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> guidMap)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(guidMap);

        var commandList = commands.ToArray();
        var assetList = assets.ToArray();

        var guidToIconId = BuildGuidToIconIdIndex(guidMap);
        var mappedModernAssetByGuid = BuildAssetIconGuidIndex(assetList);
        var guidMappedAssetById = BuildPreferredGuidMappedAssetIndex(assetList);
        var anyAssetById = BuildPreferredAssetIndex(assetList);
        var modernAssetByKey = BuildModernAssetIndex(assetList);
        var legacyBmpCellsById = BuildPreferredLegacyBitmapIndex(assetList.Where(x =>
            x.ResourceType.Equals("RT_BITMAP_STRIP_CELL", StringComparison.OrdinalIgnoreCase)));

        var exactByGuid = new Dictionary<string, IconAsset>(StringComparer.OrdinalIgnoreCase);
        var interim = new Dictionary<DrawUiCommand, IconAssociation>(commandList.Length);

        foreach (var command in commandList)
        {
            // Prefer a dedicated icon GUID declared by DrawUI (icon="guid://...") over
            // reusing the command GUID. CorelDRAW supports separate command/control GUIDs
            // and icon GUIDs, so preserving the distinction is important for add-on reuse.
            var exact = TryMappedAssetIconGuid(command, mappedModernAssetByGuid) ??
                        TryMappedAssetCommandGuid(command, mappedModernAssetByGuid) ??
                        TryDirectIconGuid(command, guidToIconId, guidMappedAssetById) ??
                        TryGuidMap(command, guidToIconId, guidMappedAssetById) ??
                        TryExplicitResource(command, anyAssetById) ??
                        TryNamedModernResource(command, modernAssetByKey) ??
                        TryLegacyBmpCoordinates(command, legacyBmpCellsById);
            if (exact is not null)
            {
                interim[command] = exact;
                RegisterCommandGuids(command, exact.Asset!, exactByGuid);
            }
        }

        foreach (var command in commandList)
        {
            if (interim.ContainsKey(command))
                continue;

            var viaImageGuid = TryImageGuid(command, exactByGuid);
            if (viaImageGuid is not null)
            {
                interim[command] = viaImageGuid;
                continue;
            }

            var viaModernCaption = TryModernCaptionResource(command, modernAssetByKey);
            if (viaModernCaption is not null)
            {
                interim[command] = viaModernCaption;
                continue;
            }

            var declaredIconGuid = FindDeclaredIconGuid(command);
            var resourceIdHint = declaredIconGuid is not null && guidToIconId.TryGetValue(declaredIconGuid, out var pendingIconId)
                ? pendingIconId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            var hasBmpCoordinates = command.ResourceHints.Any(x => x.Name.Equals("bmpRow", StringComparison.OrdinalIgnoreCase)) &&
                                    command.ResourceHints.Any(x => x.Name.Equals("bmpCol", StringComparison.OrdinalIgnoreCase));
            interim[command] = hasBmpCoordinates
                ? new IconAssociation(command, null, AssociationConfidence.Heuristic,
                    "bmpRow/bmpCol present; matching legacy bitmap strip cell was not found", declaredIconGuid, resourceIdHint)
                : new IconAssociation(command, null, AssociationConfidence.Unmapped,
                    declaredIconGuid is null
                        ? "No reliable icon mapping rule matched"
                        : resourceIdHint is null
                            ? "DrawUI declares an icon GUID, but no matching local icon resource was found"
                            : $"DrawUI icon GUID maps to CrlIcons id={resourceIdHint}, but the 2026 image payload is not directly extractable",
                    declaredIconGuid, resourceIdHint);
        }

        return commandList.Select(x => interim[x]).ToArray();
    }

    private static IReadOnlyDictionary<string, ushort> BuildGuidToIconIdIndex(
        IReadOnlyDictionary<ushort, IReadOnlyList<string>> guidMap)
    {
        var result = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in guidMap)
        {
            foreach (var mapped in pair.Value)
            {
                var normalized = CrlIconGuidMapParser.NormalizeGuid(mapped);
                if (normalized is null || ambiguous.Contains(normalized))
                    continue;

                if (result.TryGetValue(normalized, out var existing) && existing != pair.Key)
                {
                    result.Remove(normalized);
                    ambiguous.Add(normalized);
                    continue;
                }

                result[normalized] = pair.Key;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IconAsset> BuildAssetIconGuidIndex(IEnumerable<IconAsset> assets)
    {
        var result = new Dictionary<string, IconAsset>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets)
        {
            foreach (var raw in asset.IconGuids)
            {
                var guid = IconGuidReference.Normalize(raw);
                if (guid is null || ambiguous.Contains(guid))
                    continue;
                if (result.TryGetValue(guid, out var existing) && !ReferenceEquals(existing, asset))
                {
                    result.Remove(guid);
                    ambiguous.Add(guid);
                    continue;
                }
                result[guid] = asset;
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, IconAsset> BuildUniqueAssetIndex(IEnumerable<IconAsset> assets)
    {
        var result = new Dictionary<string, IconAsset>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets)
        {
            var id = asset.ResourceId;
            if (string.IsNullOrWhiteSpace(id) || ambiguous.Contains(id))
                continue;

            if (result.ContainsKey(id))
            {
                result.Remove(id);
                ambiguous.Add(id);
                continue;
            }

            result[id] = asset;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IconAsset> BuildModernAssetIndex(IEnumerable<IconAsset> assets)
    {
        var result = new Dictionary<string, IconAsset>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets.Where(x =>
                     x.ResourceType.Equals("ModernCrlIcons", StringComparison.OrdinalIgnoreCase) ||
                     Path.GetFileName(x.SourceFile).Equals("Modern.crlicons", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var key in EnumerateModernResourceKeys(asset.ResourceId)
                         .Concat(EnumerateModernResourceKeys(asset.DisplayName))
                         .Concat(EnumerateModernResourceKeys(asset.ResourcePath))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (ambiguous.Contains(key))
                    continue;
                if (result.TryGetValue(key, out var existing) && !ReferenceEquals(existing, asset))
                {
                    result.Remove(key);
                    ambiguous.Add(key);
                    continue;
                }
                result[key] = asset;
            }
        }

        return result;
    }

    private static IEnumerable<string> EnumerateModernResourceKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var normalizedPath = value.Trim().Replace('\\', '/').Trim('/');
        if (normalizedPath.StartsWith("guid://", StringComparison.OrdinalIgnoreCase) ||
            IconGuidReference.Normalize(normalizedPath) is not null)
            yield break;

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 &&
            segments[^1].EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(Path.GetFileNameWithoutExtension(segments[^1]), out var variantSize) &&
            variantSize is 16 or 20 or 24 or 32 or 36 or 40 or 48 or 64 or 72 or 96 or 128 or 256)
        {
            normalizedPath = string.Join('/', segments[..^1]);
            segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        var fullKey = NormalizeModernResourceKey(normalizedPath);
        if (fullKey.Length >= 2)
            yield return fullKey;

        if (segments.Length > 0)
        {
            var leafKey = NormalizeModernResourceKey(segments[^1]);
            if (leafKey.Length >= 2 && !leafKey.Equals(fullKey, StringComparison.OrdinalIgnoreCase))
                yield return leafKey;
        }
    }

    private static string NormalizeModernResourceKey(string value)
    {
        var candidate = value.Trim();
        foreach (var suffix in new[] { ".ico.png", ".png", ".ico", ".svg" })
        {
            if (candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^suffix.Length];
                break;
            }
        }

        return new string(candidate
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static IReadOnlyDictionary<string, IconAsset> BuildPreferredGuidMappedAssetIndex(IEnumerable<IconAsset> assets)
    {
        return assets
            .Where(x => !string.IsNullOrWhiteSpace(x.ResourceId))
            .GroupBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(GuidMapSourcePriority)
                    .ThenBy(ResourceTypePriority)
                    .ThenByDescending(x => (long)x.Width * x.Height)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static int GuidMapSourcePriority(IconAsset asset)
    {
        var name = Path.GetFileName(asset.SourceFile);
        if (name.Equals("CrlIcons.dll", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Equals("CrlGenericUI.dll", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Equals("CorelDRW.exe", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static IReadOnlyDictionary<string, IconAsset> BuildPreferredAssetIndex(IEnumerable<IconAsset> assets)
    {
        return assets
            .Where(x => !string.IsNullOrWhiteSpace(x.ResourceId))
            .GroupBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(SourcePriority)
                    .ThenBy(ResourceTypePriority)
                    .ThenByDescending(x => (long)x.Width * x.Height)
                    .ThenBy(x => x.SourceFile, StringComparer.OrdinalIgnoreCase)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static int SourcePriority(IconAsset asset)
    {
        var name = Path.GetFileName(asset.SourceFile);
        if (name.Equals("CrlGenericUI.dll", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Equals("CrlIcons.dll", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Equals("CorelDRW.exe", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static int ResourceTypePriority(IconAsset asset) => asset.ResourceType switch
    {
        "CrlIconsPng" => 0,
        "RT_GROUP_ICON" => 1,
        "RT_ICON" => 2,
        "RT_RCDATA_PNG" => 3,
        "RT_BITMAP" => 4,
        "RT_BITMAP_STRIP_CELL" => 5,
        _ => 6
    };

    private static IReadOnlyDictionary<string, IconAsset> BuildPreferredLegacyBitmapIndex(IEnumerable<IconAsset> assets)
    {
        return assets
            .Where(x => !string.IsNullOrWhiteSpace(x.ResourceId))
            .GroupBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(x => Path.GetFileName(x.SourceFile).Equals("CrlGenericUI.dll", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(x => x.SourceFile, StringComparer.OrdinalIgnoreCase)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IconAssociation? TryMappedAssetIconGuid(
        DrawUiCommand command,
        IReadOnlyDictionary<string, IconAsset> assetByGuid)
    {
        foreach (var hint in EnumerateIconGuidHints(command))
        {
            var normalized = IconGuidReference.Normalize(hint.Value);
            if (normalized is null || !assetByGuid.TryGetValue(normalized, out var asset))
                continue;

            return new IconAssociation(
                command,
                asset,
                AssociationConfidence.Exact,
                $"{hint.Name}={IconGuidReference.FormatUri(normalized)} -> icons.map.xml {asset.ResourcePath ?? asset.ResourceId}",
                normalized,
                asset.ResourceId);
        }
        return null;
    }

    private static IconAssociation? TryMappedAssetCommandGuid(
        DrawUiCommand command,
        IReadOnlyDictionary<string, IconAsset> assetByGuid)
    {
        foreach (var raw in EnumerateCommandGuids(command))
        {
            var normalized = IconGuidReference.Normalize(raw);
            if (normalized is null || !assetByGuid.TryGetValue(normalized, out var asset))
                continue;

            return new IconAssociation(
                command,
                asset,
                AssociationConfidence.Exact,
                $"Command GUID maps through icons.map.xml -> {asset.ResourcePath ?? asset.ResourceId}",
                normalized,
                asset.ResourceId);
        }
        return null;
    }

    private static IconAssociation? TryDirectIconGuid(
        DrawUiCommand command,
        IReadOnlyDictionary<string, ushort> guidToIconId,
        IReadOnlyDictionary<string, IconAsset> crlAssetById)
    {
        foreach (var hint in EnumerateIconGuidHints(command))
        {
            var normalized = IconGuidReference.Normalize(hint.Value);
            if (normalized is null || !guidToIconId.TryGetValue(normalized, out var iconId))
                continue;

            var id = iconId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (crlAssetById.TryGetValue(id, out var asset))
                return new IconAssociation(command, asset, AssociationConfidence.Exact,
                    $"{hint.Name}={IconGuidReference.FormatUri(normalized)} -> CrlIcons id={iconId}", normalized, id);
        }

        return null;
    }

    private static IconAssociation? TryGuidMap(
        DrawUiCommand command,
        IReadOnlyDictionary<string, ushort> guidToIconId,
        IReadOnlyDictionary<string, IconAsset> crlAssetById)
    {
        foreach (var commandGuid in EnumerateCommandGuids(command))
        {
            var normalized = CrlIconGuidMapParser.NormalizeGuid(commandGuid);
            if (normalized is null || !guidToIconId.TryGetValue(normalized, out var iconId))
                continue;

            var id = iconId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (crlAssetById.TryGetValue(id, out var asset))
                return new IconAssociation(command, asset, AssociationConfidence.Exact,
                    $"Command GUID maps to CrlIcons id={iconId}", normalized, id);
        }

        return null;
    }

    private static IconAssociation? TryExplicitResource(
        DrawUiCommand command,
        IReadOnlyDictionary<string, IconAsset> assetById)
    {
        foreach (var hint in command.ResourceHints.Where(x =>
                     x.Name.Equals("resourceId", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Equals("icon", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)))
        {
            if (!ushort.TryParse(hint.Value, out var numericId))
                continue;

            var id = numericId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (assetById.TryGetValue(id, out var asset))
                return new IconAssociation(command, asset, AssociationConfidence.Exact, $"Explicit {hint.Name}={id}", null, id);
        }

        return null;
    }

    private static IconAssociation? TryNamedModernResource(
        DrawUiCommand command,
        IReadOnlyDictionary<string, IconAsset> modernAssetByKey)
    {
        foreach (var hint in command.ResourceHints.Where(x =>
                     x.Name.Equals("resourceId", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Equals("icon", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)))
        {
            if (IconGuidReference.Normalize(hint.Value) is not null)
                continue;

            foreach (var key in EnumerateModernResourceKeys(hint.Value))
            {
                if (!modernAssetByKey.TryGetValue(key, out var asset))
                    continue;

                var iconGuid = FindDeclaredIconGuid(command);
                if (iconGuid is null && IsResourceDefinition(command))
                    iconGuid = IconGuidReference.Normalize(command.Guid) ?? IconGuidReference.Normalize(command.GuidRef);

                return new IconAssociation(
                    command,
                    asset,
                    AssociationConfidence.Exact,
                    $"Modern named {hint.Name}={hint.Value} -> {asset.ResourcePath ?? asset.ResourceId}",
                    iconGuid,
                    asset.ResourceId);
            }
        }

        return null;
    }

    private static IconAssociation? TryModernCaptionResource(
        DrawUiCommand command,
        IReadOnlyDictionary<string, IconAsset> modernAssetByKey)
    {
        if (IsResourceDefinition(command))
            return null;

        foreach (var caption in new[] { command.Caption, command.LocalizedCaption })
        {
            if (string.IsNullOrWhiteSpace(caption) || caption.TrimStart().StartsWith("*", StringComparison.Ordinal))
                continue;

            foreach (var key in EnumerateModernResourceKeys(caption))
            {
                if (!modernAssetByKey.TryGetValue(key, out var asset))
                    continue;

                return new IconAssociation(
                    command,
                    asset,
                    AssociationConfidence.Strong,
                    $"Modern resource name matches command caption '{caption}'",
                    FindDeclaredIconGuid(command),
                    asset.ResourceId);
            }
        }

        return null;
    }

    private static bool IsResourceDefinition(DrawUiCommand command) =>
        command.ElementName.Equals("resEntry", StringComparison.OrdinalIgnoreCase) ||
        command.ElementName.Equals("resourceEntry", StringComparison.OrdinalIgnoreCase);

    private static IconAssociation? TryLegacyBmpCoordinates(
        DrawUiCommand command,
        IReadOnlyDictionary<string, IconAsset> legacyBmpCellsById)
    {
        var row = command.ResourceHints.FirstOrDefault(x => x.Name.Equals("bmpRow", StringComparison.OrdinalIgnoreCase))?.Value;
        var col = command.ResourceHints.FirstOrDefault(x => x.Name.Equals("bmpCol", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!int.TryParse(row, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var rowNumber) ||
            !int.TryParse(col, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var colNumber) ||
            rowNumber < 0 || colNumber is < 0 or > 9)
            return null;

        var id = $"{rowNumber}:{colNumber}";
        if (!legacyBmpCellsById.TryGetValue(id, out var asset))
            return null;

        return new IconAssociation(command, asset, AssociationConfidence.Exact,
            $"Legacy bmpRow={rowNumber}, bmpCol={colNumber}", null, id);
    }

    private static IconAssociation? TryImageGuid(DrawUiCommand command, IReadOnlyDictionary<string, IconAsset> exactByGuid)
    {
        foreach (var hint in EnumerateIconGuidHints(command))
        {
            var normalized = IconGuidReference.Normalize(hint.Value);
            if (normalized is not null && exactByGuid.TryGetValue(normalized, out var asset))
                return new IconAssociation(command, asset, AssociationConfidence.Strong,
                    $"{hint.Name} resolves to an exact-mapped command GUID", normalized, asset.ResourceId);
        }
        return null;
    }

    private static string? FindDeclaredIconGuid(DrawUiCommand command)
    {
        foreach (var hint in EnumerateIconGuidHints(command))
        {
            var normalized = IconGuidReference.Normalize(hint.Value);
            if (normalized is not null)
                return normalized;
        }
        return null;
    }

    private static IEnumerable<ResourceHint> EnumerateIconGuidHints(DrawUiCommand command) =>
        command.ResourceHints.Where(x =>
            x.Name.Equals("icon", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("iconGuid", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("image", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("imageGuid", StringComparison.OrdinalIgnoreCase));

    private static void RegisterCommandGuids(DrawUiCommand command, IconAsset asset, IDictionary<string, IconAsset> target)
    {
        foreach (var value in EnumerateCommandGuids(command))
        {
            var normalized = CrlIconGuidMapParser.NormalizeGuid(value);
            if (normalized is not null)
                target[normalized] = asset;
        }
    }

    private static IEnumerable<string?> EnumerateCommandGuids(DrawUiCommand command)
    {
        yield return command.Guid;
        yield return command.GuidRef;
    }
}
