using System.IO.Compression;
using System.Text;
using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class WorkspaceShortcutResolverTests
{
    [TestMethod]
    public void Enrich_ReadsKeySequenceFromCdwsAndMapsByGuid()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdr-icon-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "_default.cdws");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("content/workspace.xml");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write("""
                    <workspace>
                      <item guid="11111111-1111-1111-1111-111111111111">
                        <keySequence key="Ctrl+Q" />
                      </item>
                      <keySequence guidRef="22222222-2222-2222-2222-222222222222" value="Ctrl+I" />
                      <command guid="33333333-3333-3333-3333-333333333333">
                        <keySequence>Ctrl+E</keySequence>
                      </command>
                    </workspace>
                    """);
            }

            var commands = new[]
            {
                Command("11111111-1111-1111-1111-111111111111"),
                Command("22222222-2222-2222-2222-222222222222"),
                Command("33333333-3333-3333-3333-333333333333")
            };

            var result = new WorkspaceShortcutResolver().Enrich(commands, new[] { path });

            Assert.AreEqual("Ctrl+Q", result[0].Shortcut);
            Assert.AreEqual("Ctrl+I", result[1].Shortcut);
            Assert.AreEqual("Ctrl+E", result[2].Shortcut);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Enrich_DoesNotReplaceShortcutAlreadyPresentInDrawUi()
    {
        var command = Command("11111111-1111-1111-1111-111111111111") with { Shortcut = "Ctrl+Shift+Q" };
        var result = new WorkspaceShortcutResolver().Enrich(new[] { command }, Array.Empty<string>());
        Assert.AreEqual("Ctrl+Shift+Q", result[0].Shortcut);
    }

    private static DrawUiCommand Command(string guid) =>
        new(guid, null, null, null, null, "item", Array.Empty<ResourceHint>(), "/item");
}
