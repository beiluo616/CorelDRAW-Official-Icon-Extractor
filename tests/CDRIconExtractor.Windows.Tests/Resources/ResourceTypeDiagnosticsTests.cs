using CDRIconExtractor.Windows.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Windows.Tests.Resources;

[TestClass]
public sealed class ResourceTypeDiagnosticsTests
{
    [TestMethod]
    public void Format_ReportsNamedAndNumericResourceTypes()
    {
        var summaries = new[]
        {
            new Win32ResourceTypeSummary("2", 2, 12, 4096, new[] { "101", "102" }),
            new Win32ResourceTypeSummary("COREL_ICON", null, 2680, 800000, new[] { "Main" })
        };

        var text = ResourceTypeDiagnostics.Format(summaries);

        StringAssert.Contains(text, "#2=12");
        StringAssert.Contains(text, "COREL_ICON=2680");
    }
}
