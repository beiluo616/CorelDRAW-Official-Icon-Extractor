using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Windows.Resources;

public static class CoreResourceModuleLocator
{
    public static IReadOnlyList<string> LocateCoreModules(CorelInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                result.Add(full);
        }

        var programFolder = Path.GetDirectoryName(installation.ProgramPath);
        Add(programFolder is null ? null : Path.Combine(programFolder, "CrlGenericUI.dll"));
        Add(Path.Combine(installation.InstallRoot, "Programs", "CrlGenericUI.dll"));
        Add(Path.Combine(installation.InstallRoot, "Programs64", "CrlGenericUI.dll"));
        Add(Path.Combine(installation.InstallRoot, "Draw", "CrlGenericUI.dll"));

        if (!result.Any(path => Path.GetFileName(path).Equals("CrlGenericUI.dll", StringComparison.OrdinalIgnoreCase)))
            Add(FindFileBounded(installation.InstallRoot, "CrlGenericUI.dll", 5));

        Add(installation.ProgramPath);
        return result;
    }

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
}
