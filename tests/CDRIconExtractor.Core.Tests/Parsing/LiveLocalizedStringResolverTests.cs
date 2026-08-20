using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class LiveLocalizedStringResolverTests
{
    [TestMethod]
    public void Enrich_PrefersStringReferenceGuidAndFillsChineseCaption()
    {
        var provider = new FakeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["22222222-2222-2222-2222-222222222222"] = "转换为曲线"
        });
        var command = new DrawUiCommand(
            "11111111-1111-1111-1111-111111111111",
            null,
            "*CT('{22222222-2222-2222-2222-222222222222}')",
            null,
            null,
            "item",
            new[] { new ResourceHint("icon", "guid://33333333-3333-3333-3333-333333333333") },
            "/item");

        var result = new LiveLocalizedStringResolver().Enrich(new[] { command }, provider, 10, CancellationToken.None);

        Assert.AreEqual("转换为曲线", result.Commands[0].LocalizedCaption);
        Assert.AreEqual(1, result.RequestCount);
        Assert.AreEqual(1, result.ResolvedCount);
    }

    [TestMethod]
    public void Enrich_StopsAtRequestLimit()
    {
        var provider = new FakeProvider(new Dictionary<string, string>());
        var commands = Enumerable.Range(1, 10)
            .Select(i => new DrawUiCommand($"00000000-0000-0000-0000-{i:000000000000}", null, null, null, null, "item", Array.Empty<ResourceHint>(), "/item"))
            .ToArray();

        var result = new LiveLocalizedStringResolver().Enrich(commands, provider, 3, CancellationToken.None);

        Assert.AreEqual(3, result.RequestCount);
    }

    private sealed class FakeProvider : ILocalizedStringProvider
    {
        private readonly IReadOnlyDictionary<string, string> _values;
        public FakeProvider(IReadOnlyDictionary<string, string> values) => _values = values;
        public string? LoadLocalizedString(string guid) => _values.TryGetValue(guid, out var value) ? value : null;
    }
}
