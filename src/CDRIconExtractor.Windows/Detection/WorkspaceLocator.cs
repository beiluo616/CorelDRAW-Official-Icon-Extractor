using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Windows.Detection;

public static class WorkspaceLocator
{
    public static IReadOnlyList<string> Locate(CorelInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            return Array.Empty<string>();
        return Locate(installation, appData);
    }

    public static IReadOnlyList<string> Locate(CorelInstallation installation, string appDataRoot)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (string.IsNullOrWhiteSpace(appDataRoot) || !Directory.Exists(appDataRoot))
            return Array.Empty<string>();

        var roots = new[]
        {
            Path.Combine(appDataRoot, "Corel"),
            appDataRoot
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var candidates = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(root, "*.cdws", SearchOption.AllDirectories).Take(64));
            }
            catch
            {
                // Roaming profiles can contain inaccessible folders. Skip them.
            }
            if (candidates.Count > 0 && root.EndsWith($"{Path.DirectorySeparatorChar}Corel", StringComparison.OrdinalIgnoreCase))
                break;
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new { Path = Path.GetFullPath(path), Score = Score(path, installation), Modified = SafeLastWrite(path) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Modified)
            .Take(8)
            .Select(x => x.Path)
            .ToArray();
    }

    private static int Score(string path, CorelInstallation installation)
    {
        var score = 0;
        var text = path.Replace('_', ' ');
        if (Path.GetFileName(path).Equals("_default.cdws", StringComparison.OrdinalIgnoreCase))
            score += 100;
        if (text.Contains(installation.VersionMajor.ToString(), StringComparison.OrdinalIgnoreCase))
            score += 40;
        foreach (var token in VersionTokens(installation.VersionMajor))
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 60;
        if (text.Contains("CorelDRAW", StringComparison.OrdinalIgnoreCase))
            score += 10;
        return score;
    }

    private static IEnumerable<string> VersionTokens(int versionMajor)
    {
        if (versionMajor == 14) yield return "X4";
        if (versionMajor == 15) yield return "X5";
        if (versionMajor == 16) yield return "X6";
        if (versionMajor == 17) yield return "X7";
        if (versionMajor == 18) yield return "X8";
        switch (versionMajor)
        {
            case 19: yield return "2017"; break;
            case 20: yield return "2018"; break;
            case 21: yield return "2019"; break;
            case 22: yield return "2020"; break;
            case 23: yield return "2021"; break;
            case 24:
                yield return "2022";
                yield return "2023";
                break;
            case 25: yield return "2024"; break;
            case 26: yield return "2025"; break;
            case 27: yield return "2026"; break;
        }
    }

    private static DateTime SafeLastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }
}
