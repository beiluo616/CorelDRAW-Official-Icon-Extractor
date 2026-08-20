namespace CDRIconExtractor.Windows.Detection;

public sealed record RegistryInstallCandidate(string DisplayName, string InstallLocation);

public interface IRegistrySource
{
    IEnumerable<RegistryInstallCandidate> GetInstallCandidates();
}
