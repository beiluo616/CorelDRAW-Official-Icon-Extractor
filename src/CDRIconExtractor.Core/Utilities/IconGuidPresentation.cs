namespace CDRIconExtractor.Core.Utilities;

public sealed record IconGuidPresentation(
    string CommandGuid,
    string IconGuid,
    string PrimaryGuid,
    string PrimaryLabel,
    bool ShowCombined,
    bool ShowSeparate)
{
    public static IconGuidPresentation Create(string? commandGuid, string? iconGuid)
    {
        var command = IconGuidReference.Normalize(commandGuid) ?? string.Empty;
        var icon = IconGuidReference.Normalize(iconGuid) ?? string.Empty;
        var separate = command.Length > 0 && icon.Length > 0 &&
                       !command.Equals(icon, StringComparison.OrdinalIgnoreCase);

        if (separate)
            return new IconGuidPresentation(command, icon, icon, "图标 GUID", false, true);

        var primary = icon.Length > 0 ? icon : command;
        var label = command.Length > 0 && icon.Length > 0
            ? "GUID（命令/图标共用）"
            : icon.Length > 0
                ? "图标 GUID"
                : "命令 GUID";

        return new IconGuidPresentation(command, icon, primary, label, primary.Length > 0, false);
    }
}
