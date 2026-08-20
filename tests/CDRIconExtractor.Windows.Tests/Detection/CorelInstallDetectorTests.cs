using CDRIconExtractor.Windows.Detection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Windows.Tests.Detection;

[TestClass]
public sealed class CorelInstallDetectorTests
{
    [TestMethod]
    public void ValidateCandidate_RequiresCorelDrwExe()
    {
        using var temp = new TempDirectory();
        var detector = new CorelInstallDetector(new FakeRegistrySource(), new[] { temp.Path });

        Assert.IsFalse(detector.TryCreateInstallation(temp.Path, out _));
    }

    [TestMethod]
    public void TryCreateInstallation_DetectsCrlIconsNextToProgram()
    {
        using var temp = TestCorelLayout.Create(includeCrlIcons: true);
        var detector = new CorelInstallDetector(new FakeRegistrySource(), Array.Empty<string>());

        Assert.IsTrue(detector.TryCreateInstallation(temp.ProgramFolder, out var install));
        Assert.IsNotNull(install);
        Assert.IsNotNull(install.CrlIconsPath);
        StringAssert.EndsWith(install.CrlIconsPath, "CrlIcons.dll");
    }

    [TestMethod]
    public void Detect_DeduplicatesSameProgramPath()
    {
        using var temp = TestCorelLayout.Create(includeCrlIcons: false);
        var registry = new FakeRegistrySource(
            new RegistryInstallCandidate("CorelDRAW A", temp.ProgramFolder),
            new RegistryInstallCandidate("CorelDRAW B", temp.ProgramFolder));
        var detector = new CorelInstallDetector(registry, new[] { temp.Root });

        var items = detector.Detect();

        Assert.AreEqual(1, items.Count(x => Path.GetFullPath(x.ProgramPath) == Path.GetFullPath(temp.ProgramPath)));
    }

    [TestMethod]
    public void TryCreateInstallation_AcceptsPortableX4Version14Path()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CDRIconExtractorTests", "CorelDRAW 14 Green", Guid.NewGuid().ToString("N"));
        try
        {
            var program = System.IO.Path.Combine(root, "Programs");
            Directory.CreateDirectory(program);
            var exe = System.IO.Path.Combine(program, "CorelDRW.exe");
            File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
            var detector = new CorelInstallDetector(new FakeRegistrySource(), Array.Empty<string>());

            Assert.IsTrue(detector.TryCreateInstallation(root, out var install));
            Assert.IsNotNull(install);
            Assert.AreEqual(14, install.VersionMajor);
            StringAssert.Contains(install.DisplayName, "X4");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [TestMethod]
    public void TryCreateInstallation_FindsCrlIconsInsideDrawFolder()
    {
        using var temp = TestCorelLayout.Create(includeCrlIcons: false);
        var draw = System.IO.Path.Combine(temp.Root, "Draw");
        Directory.CreateDirectory(draw);
        File.WriteAllBytes(System.IO.Path.Combine(draw, "CrlIcons.dll"), new byte[] { 0x4D, 0x5A });
        var detector = new CorelInstallDetector(new FakeRegistrySource(), Array.Empty<string>());

        Assert.IsTrue(detector.TryCreateInstallation(temp.ProgramFolder, out var install));
        Assert.IsNotNull(install);
        Assert.IsNotNull(install.CrlIconsPath);
        StringAssert.Contains(install.CrlIconsPath, $"{System.IO.Path.DirectorySeparatorChar}Draw{System.IO.Path.DirectorySeparatorChar}");
    }

    private sealed class FakeRegistrySource(params RegistryInstallCandidate[] candidates) : IRegistrySource
    {
        public IEnumerable<RegistryInstallCandidate> GetInstallCandidates() => candidates;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CDRIconExtractorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }

    private sealed class TestCorelLayout : IDisposable
    {
        private TestCorelLayout(string root, string programFolder, string programPath)
        {
            Root = root;
            ProgramFolder = programFolder;
            ProgramPath = programPath;
        }

        public string Root { get; }
        public string ProgramFolder { get; }
        public string ProgramPath { get; }

        public static TestCorelLayout Create(bool includeCrlIcons)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CDRIconExtractorTests", "CorelDRAW Graphics Suite 26", Guid.NewGuid().ToString("N"));
            var program = System.IO.Path.Combine(root, "Programs64");
            Directory.CreateDirectory(program);
            var exe = System.IO.Path.Combine(program, "CorelDRW.exe");
            File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
            if (includeCrlIcons)
                File.WriteAllBytes(System.IO.Path.Combine(program, "CrlIcons.dll"), new byte[] { 0x4D, 0x5A });
            return new TestCorelLayout(root, program, exe);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
