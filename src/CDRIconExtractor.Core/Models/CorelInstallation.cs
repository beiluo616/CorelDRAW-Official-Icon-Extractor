namespace CDRIconExtractor.Core.Models;

public sealed record CorelInstallation(
    string DisplayName,
    int VersionMajor,
    string? FileVersion,
    string ProgramPath,
    string InstallRoot,
    string? CrlIconsPath);
