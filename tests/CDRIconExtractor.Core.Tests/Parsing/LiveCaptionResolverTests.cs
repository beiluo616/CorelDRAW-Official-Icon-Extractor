using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class LiveCaptionResolverTests
{
    private const string Guid1 = "11111111-1111-1111-1111-111111111111";
    private const string Guid2 = "22222222-2222-2222-2222-222222222222";

    [TestMethod]
    public void Enrich_UsesRunningCorelCaptionAsChineseLocalizedName()
    {
        var command = Command(Guid1, null, "*CT('{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}')", null);
        var provider = new FakeCaptionProvider(new Dictionary<string, string?>
        {
            [Guid1] = "转换为曲线(&V)"
        });

        var result = new LiveCaptionResolver().Enrich([command], provider, 10, CancellationToken.None);

        Assert.AreEqual("转换为曲线", result.Commands.Single().LocalizedCaption);
        Assert.AreEqual(1, result.RequestCount);
        Assert.AreEqual(1, result.ResolvedCount);
    }

    [TestMethod]
    public void Enrich_UsesGuidRefAndStripsMenuAmpersandForEnglishCaption()
    {
        var command = Command(null, Guid2, null, null);
        var provider = new FakeCaptionProvider(new Dictionary<string, string?>
        {
            [Guid2] = "&Convert to Curves"
        });

        var result = new LiveCaptionResolver().Enrich([command], provider, 10, CancellationToken.None);

        Assert.AreEqual("Convert to Curves", result.Commands.Single().Caption);
        Assert.AreEqual(1, result.ResolvedCount);
    }

    [TestMethod]
    public void Enrich_DoesNotReplaceExistingChineseCaption()
    {
        var command = Command(Guid1, null, "Convert to Curves", "转换为曲线");
        var provider = new FakeCaptionProvider(new Dictionary<string, string?> { [Guid1] = "其他名称" });

        var result = new LiveCaptionResolver().Enrich([command], provider, 10, CancellationToken.None);

        Assert.AreEqual("转换为曲线", result.Commands.Single().LocalizedCaption);
        Assert.AreEqual(0, result.RequestCount);
    }

    [TestMethod]
    public void Enrich_RespectsRequestLimit()
    {
        var commands = new[]
        {
            Command(Guid1, null, null, null),
            Command(Guid2, null, null, null)
        };
        var provider = new FakeCaptionProvider(new Dictionary<string, string?>
        {
            [Guid1] = "名称一",
            [Guid2] = "名称二"
        });

        var result = new LiveCaptionResolver().Enrich(commands, provider, 1, CancellationToken.None);

        Assert.AreEqual(1, result.RequestCount);
        Assert.AreEqual(1, result.ResolvedCount);
    }

    private static DrawUiCommand Command(string? guid, string? guidRef, string? caption, string? localized) =>
        new(guid, guidRef, caption, localized, null, "itemData", Array.Empty<ResourceHint>(), "test.xml");

    private sealed class FakeCaptionProvider(IReadOnlyDictionary<string, string?> values) : IUiCaptionProvider
    {
        public string? GetCaptionText(string guid) => values.TryGetValue(guid, out var value) ? value : null;
    }
}
