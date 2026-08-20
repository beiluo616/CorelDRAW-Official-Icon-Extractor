using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.Windows.Detection;

public sealed class UiDefinitionLocator
{
    private static readonly string[] RelativeDirectories =
    [
        @"Draw\UIConfig",
        @"Programs64\Draw\UIConfig",
        @"Programs\Draw\UIConfig",
        @"Programs\UIConfig\CorelDRAW",
        @"Programs64\UIConfig\CorelDRAW",
        @"UIConfig\CorelDRAW",
        @"UIConfig"
    ];

    public IReadOnlyList<string> Locate(CorelInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDirectCandidates(installation.InstallRoot, found);
        var programFolder = Path.GetDirectoryName(installation.ProgramPath);
        if (!string.IsNullOrWhiteSpace(programFolder))
            AddDirectCandidates(programFolder, found);

        // CorelDRAW 2026+ splits the UI definition into DrawUI.xml,
        // DrawUI.items.xml and other DrawUI*.xml fragments. Known UIConfig
        // directories above enumerate all fragments in one pass. Only fall back
        // to a bounded tree search when none of the known layouts matched.
        if (found.Count == 0 && Directory.Exists(installation.InstallRoot))
        {
            foreach (var path in EnumerateByDepth(installation.InstallRoot, "DrawUI*.xml", maxDepth: 5))
                found.Add(Path.GetFullPath(path));
        }

        return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddDirectCandidates(string root, ISet<string> found)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;

        foreach (var relative in RelativeDirectories)
        {
            var directory = Path.Combine(root, relative);
            if (!Directory.Exists(directory))
                continue;

            try
            {
                foreach (var candidate in Directory.EnumerateFiles(directory, "DrawUI*.xml", SearchOption.TopDirectoryOnly).Take(64))
                    found.Add(Path.GetFullPath(candidate));
            }
            catch
            {
                // Continue with other candidate directories.
            }
        }
    }

    private static IEnumerable<string> EnumerateByDepth(string root, string pattern, int maxDepth)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, pattern, SearchOption.TopDirectoryOnly).Take(64).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            if (depth >= maxDepth)
                continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current).Take(256).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
                queue.Enqueue((child, depth + 1));
        }
    }
}
