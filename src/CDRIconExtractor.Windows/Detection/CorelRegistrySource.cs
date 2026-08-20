using Microsoft.Win32;

namespace CDRIconExtractor.Windows.Detection;

public sealed class CorelRegistrySource : IRegistrySource
{
    private static readonly RegistryView[] Views = [RegistryView.Registry64, RegistryView.Registry32];

    public IEnumerable<RegistryInstallCandidate> GetInstallCandidates()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in Views)
        {
            foreach (var candidate in ReadUninstallView(view))
            {
                if (seen.Add(candidate.InstallLocation))
                    yield return candidate;
            }
        }
    }

    private static IEnumerable<RegistryInstallCandidate> ReadUninstallView(RegistryView view)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall is null)
            yield break;

        foreach (var subKeyName in uninstall.GetSubKeyNames())
        {
            using var key = uninstall.OpenSubKey(subKeyName);
            var displayName = key?.GetValue("DisplayName") as string;
            var installLocation = key?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation))
                continue;
            if (!displayName.Contains("CorelDRAW", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new RegistryInstallCandidate(displayName.Trim(), installLocation.Trim());
        }
    }
}
