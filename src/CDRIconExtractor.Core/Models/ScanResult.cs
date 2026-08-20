namespace CDRIconExtractor.Core.Models;

public enum ScanDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ScanDiagnostic(
    ScanDiagnosticSeverity Severity,
    string? Path,
    string Message);

public sealed record ScanProgress(
    int Percent,
    string Phase,
    string Message);

public sealed record ScanResult(
    IReadOnlyList<IconAssociation> Associations,
    IReadOnlyList<IconAsset> Assets,
    IReadOnlyList<DrawUiCommand> Commands,
    IReadOnlyList<ScanDiagnostic> Diagnostics,
    bool IsCancelled,
    int ScannedFiles,
    TimeSpan Elapsed)
{
    public static ScanResult Completed(
        IReadOnlyList<IconAssociation> associations,
        IReadOnlyList<IconAsset> assets,
        IReadOnlyList<DrawUiCommand> commands,
        IReadOnlyList<ScanDiagnostic> diagnostics,
        int scannedFiles,
        TimeSpan elapsed) =>
        new(associations, assets, commands, diagnostics, false, scannedFiles, elapsed);

    public static ScanResult Cancelled(
        IReadOnlyList<IconAssociation> associations,
        int scannedFiles,
        TimeSpan elapsed) =>
        new(
            associations,
            associations.Where(x => x.Asset is not null).Select(x => x.Asset!).DistinctBy(x => x.Sha256).ToArray(),
            associations.Select(x => x.Command).Distinct().ToArray(),
            Array.Empty<ScanDiagnostic>(),
            true,
            scannedFiles,
            elapsed);

    public static ScanResult Cancelled(
        IReadOnlyList<IconAssociation> associations,
        IReadOnlyList<IconAsset> assets,
        IReadOnlyList<DrawUiCommand> commands,
        IReadOnlyList<ScanDiagnostic> diagnostics,
        int scannedFiles,
        TimeSpan elapsed) =>
        new(associations, assets, commands, diagnostics, true, scannedFiles, elapsed);
}
