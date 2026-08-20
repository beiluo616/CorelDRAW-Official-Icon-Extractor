using System.Diagnostics;
using System.Text.RegularExpressions;
using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Windows.Detection;

public sealed class CorelInstallDetector
{
    private static readonly string[] ProgramFolderNames = ["Programs64", "Programs", "Draw"];
    private readonly IRegistrySource _registrySource;
    private readonly IReadOnlyList<string> _searchRoots;

    public CorelInstallDetector()
        : this(new CorelRegistrySource(), GetDefaultSearchRoots())
    {
    }

    public CorelInstallDetector(IRegistrySource registrySource, IEnumerable<string> searchRoots)
    {
        _registrySource = registrySource ?? throw new ArgumentNullException(nameof(registrySource));
        _searchRoots = searchRoots?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
            ?? throw new ArgumentNullException(nameof(searchRoots));
    }

    public IReadOnlyList<CorelInstallation> Detect()
    {
        var found = new Dictionary<string, CorelInstallation>(StringComparer.OrdinalIgnoreCase);

        foreach (var registryCandidate in SafeRegistryCandidates())
        {
            foreach (var path in ExpandCandidatePaths(registryCandidate.InstallLocation))
            {
                if (!TryCreateInstallation(path, out var installation) || installation is null)
                    continue;

                installation = installation with { DisplayName = registryCandidate.DisplayName };
                found.TryAdd(Normalize(installation.ProgramPath), installation);
            }
        }

        foreach (var root in _searchRoots)
        {
            foreach (var path in EnumerateBoundedCandidates(root))
            {
                if (TryCreateInstallation(path, out var installation) && installation is not null)
                    found.TryAdd(Normalize(installation.ProgramPath), installation);
            }
        }

        return found.Values
            .OrderByDescending(x => x.VersionMajor)
            .ThenByDescending(x => x.FileVersion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryCreateInstallation(string candidatePath, out CorelInstallation? installation)
    {
        installation = null;
        if (string.IsNullOrWhiteSpace(candidatePath))
            return false;

        string? programPath = LocateProgram(candidatePath);
        if (programPath is null)
            return false;

        var programFolder = Path.GetDirectoryName(programPath)!;
        var installRoot = DeriveInstallRoot(programFolder);
        var fileVersion = TryReadFileVersion(programPath);
        var versionMajor = TryReadVersionMajor(fileVersion, programPath, installRoot);
        var displayName = BuildDisplayName(versionMajor);
        var crlIconsPath = LocateCrlIcons(programFolder, installRoot);

        installation = new CorelInstallation(
            displayName,
            versionMajor,
            fileVersion,
            Path.GetFullPath(programPath),
            Path.GetFullPath(installRoot),
            crlIconsPath is null ? null : Path.GetFullPath(crlIconsPath));
        return true;
    }

    private IEnumerable<RegistryInstallCandidate> SafeRegistryCandidates()
    {
        try
        {
            return _registrySource.GetInstallCandidates().ToArray();
        }
        catch
        {
            return Array.Empty<RegistryInstallCandidate>();
        }
    }

    private static IEnumerable<string> ExpandCandidatePaths(string installLocation)
    {
        yield return installLocation;
        foreach (var name in ProgramFolderNames)
            yield return Path.Combine(installLocation, name);
    }

    private static IEnumerable<string> EnumerateBoundedCandidates(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        yield return root;
        foreach (var name in ProgramFolderNames)
            yield return Path.Combine(root, name);

        IEnumerable<string> firstLevel;
        try
        {
            firstLevel = Directory.EnumerateDirectories(root).Take(256).ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var level1 in firstLevel)
        {
            if (!Path.GetFileName(level1).Contains("Corel", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(level1).Contains("DRAW", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return level1;
            foreach (var name in ProgramFolderNames)
                yield return Path.Combine(level1, name);

            IEnumerable<string> level2;
            try
            {
                level2 = Directory.EnumerateDirectories(level1).Take(128).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var child in level2)
            {
                yield return child;
                foreach (var name in ProgramFolderNames)
                    yield return Path.Combine(child, name);
            }
        }
    }

    private static string? LocateProgram(string candidatePath)
    {
        try
        {
            if (File.Exists(candidatePath) &&
                Path.GetFileName(candidatePath).Equals("CorelDRW.exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(candidatePath);

            if (!Directory.Exists(candidatePath))
                return null;

            var direct = Path.Combine(candidatePath, "CorelDRW.exe");
            if (File.Exists(direct))
                return direct;

            foreach (var folder in ProgramFolderNames)
            {
                var nested = Path.Combine(candidatePath, folder, "CorelDRW.exe");
                if (File.Exists(nested))
                    return nested;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string DeriveInstallRoot(string programFolder)
    {
        var name = Path.GetFileName(programFolder);
        if (ProgramFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return Directory.GetParent(programFolder)?.FullName ?? programFolder;
        return programFolder;
    }

    private static string? LocateCrlIcons(string programFolder, string installRoot)
    {
        string[] candidates =
        [
            Path.Combine(programFolder, "CrlIcons.dll"),
            Path.Combine(installRoot, "CrlIcons.dll"),
            Path.Combine(installRoot, "Programs64", "CrlIcons.dll"),
            Path.Combine(installRoot, "Programs", "CrlIcons.dll"),
            Path.Combine(installRoot, "Draw", "CrlIcons.dll")
        ];

        var direct = candidates.FirstOrDefault(File.Exists);
        if (direct is not null)
            return direct;

        return FindFileBounded(installRoot, "CrlIcons.dll", maxDepth: 4);
    }

    private static string? TryReadFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? null : info.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    private static int TryReadVersionMajor(string? fileVersion, params string[] paths)
    {
        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            var first = fileVersion.Split('.', '-', ' ').FirstOrDefault();
            if (int.TryParse(first, out var major) && major is >= 14 and <= 40)
                return major;
        }

        foreach (var path in paths)
        {
            foreach (Match match in Regex.Matches(path, @"(?<!\d)(\d{2})(?:\.\d+)?(?!\d)"))
            {
                if (int.TryParse(match.Groups[1].Value, out var major) && major is >= 14 and <= 40)
                    return major;
            }
        }

        return 0;
    }

    private static string BuildDisplayName(int versionMajor) => versionMajor switch
    {
        14 => "CorelDRAW Graphics Suite X4",
        15 => "CorelDRAW Graphics Suite X5",
        16 => "CorelDRAW Graphics Suite X6",
        17 => "CorelDRAW Graphics Suite X7",
        18 => "CorelDRAW Graphics Suite X8",
        > 0 => $"CorelDRAW {versionMajor}",
        _ => "CorelDRAW"
    };

    private static string? FindFileBounded(string root, string fileName, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return null;

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            try
            {
                var match = Directory.EnumerateFiles(current, fileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (match is not null)
                    return Path.GetFullPath(match);
            }
            catch { }

            if (depth >= maxDepth)
                continue;

            try
            {
                foreach (var child in Directory.EnumerateDirectories(current).Take(256))
                    queue.Enqueue((child, depth + 1));
            }
            catch { }
        }

        return null;
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static IReadOnlyList<string> GetDefaultSearchRoots()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<string>();

        var roots = new List<string>();
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var value = Environment.GetFolderPath(special);
            if (string.IsNullOrWhiteSpace(value))
                continue;
            roots.Add(Path.Combine(value, "Corel"));
            roots.Add(value);
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
