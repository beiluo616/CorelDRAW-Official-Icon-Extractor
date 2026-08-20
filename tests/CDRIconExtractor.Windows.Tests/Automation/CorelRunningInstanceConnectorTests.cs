using CDRIconExtractor.Windows.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Windows.Tests.Automation;

[TestClass]
public sealed class CorelRunningInstanceConnectorTests
{
    [TestMethod]
    public void CandidateProgIds_V27_UsesVersionedThenGenericProgId()
    {
        CollectionAssert.AreEqual(
            new[] { "CorelDRAW.Application.27", "CorelDRAW.Application" },
            CorelRunningInstanceConnector.CandidateProgIds(27).ToArray());
    }

    [TestMethod]
    public void CompactDiagnostic_WhenProcessExistsButComFails_ReportsExpectedVersion()
    {
        var diagnostic = new CorelConnectionDiagnostic(
            27,
            new[] { new CorelProcessInfo(321, 27, "27.0.0.0", @"C:\Program Files\Corel\CorelDRW.exe", null) },
            Array.Empty<CorelConnectionStep>(),
            null,
            null);

        var text = diagnostic.ToCompactText();

        StringAssert.Contains(text, "1 个 CorelDRW.exe");
        StringAssert.Contains(text, "VersionMajor 27");
    }

    [TestMethod]
    public void CompactDiagnostic_WhenConnected_ShowsMethodAndVersion()
    {
        var diagnostic = new CorelConnectionDiagnostic(
            27,
            Array.Empty<CorelProcessInfo>(),
            new[] { new CorelConnectionStep("ROT", true, "已取得 CorelDRAW Application") },
            27,
            "ROT");

        var text = diagnostic.ToCompactText();

        StringAssert.Contains(text, "已连接");
        StringAssert.Contains(text, "ROT");
        StringAssert.Contains(text, "VersionMajor 27");
    }
}
