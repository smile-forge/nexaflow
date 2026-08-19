using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Compressed.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>The Compressed inspector view-model: archive metadata + the flattened directory tree it
/// builds from the VFS, and folder expand/collapse of the visible rows.</summary>
[TestClass]
[CoversNode("compressed-ui")]
public class CompressedViewModelTests
{
    [TestMethod]
    [CoversNode("compressed-entry-tree")]
    [CoversNode("compressed-metadata-pane")]
    public void Load_PopulatesMetadataAndTree()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        Assert.IsTrue(vm.IsRecognised);
        Assert.AreEqual("Zip", vm.Format);
        Assert.IsTrue(vm.CanModify);
        StringAssert.Contains(vm.EntryCountText, "3 file");

        // Top level: the "docs" folder and "readme.txt".
        var topNames = vm.VisibleRows.Where(r => r.Depth == 0).Select(r => r.Name).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "docs", "readme.txt" }, topNames.ToList());
    }

    // ── AI integration: honest context + the list/read/test act tools ─────────────

    [TestMethod]
    [CoversNode("compressed-ai-act")]
    [CoversNode("compressed-ai-context")]
    public async Task AiTools_ListReadAndTest_ThroughToolSurface()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        // scope: the archive path, so two pinned Compressed tabs stay distinguishable (aspect 4)
        Assert.AreEqual(fix.ZipPath, vm.GetSecurityContext());

        // context is honest: names the file + format, surfaces the visible entry tree, and reports the
        // encryption/signature status it computed — no longer just a one-line count.
        var ctx = vm.GetContext();
        StringAssert.Contains(ctx, vm.FileName);
        StringAssert.Contains(ctx, "Zip");
        StringAssert.Contains(ctx, "readme.txt");        // the entry tree, not only a count
        StringAssert.Contains(ctx, "docs");
        StringAssert.Contains(ctx, "Not encrypted");
        StringAssert.Contains(ctx, "Unsigned");

        var tools = vm.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "list_entries", "read_entry", "test_archive" },
            tools.Select(t => t.Name).ToArray(),
            "the Compressed AI act tool surface changed — update the tree's compressed-ai-act leaves to match");

        // list_entries: the full manifest the summary only counts
        var list = tools.Single(t => t.Name == "list_entries");
        var all = await list.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(all.IsError);
        StringAssert.Contains(all.ModelText, "readme.txt");
        StringAssert.Contains(all.ModelText, "docs/guide.md");
        StringAssert.Contains(all.ModelText, "docs/img/logo.txt");

        // list_entries prefix filter narrows to a subtree
        var underDocs = await list.InvokeAsync(new JsonObject { ["prefix"] = "docs/" }, CancellationToken.None);
        Assert.IsFalse(underDocs.IsError);
        StringAssert.Contains(underDocs.ModelText, "docs/guide.md");
        Assert.IsFalse(underDocs.ModelText.Contains("readme.txt"), "prefix should exclude top-level readme.txt");

        // read_entry: pulls one entry's text back without extracting to disk
        var read = tools.Single(t => t.Name == "read_entry");
        var guide = await read.InvokeAsync(new JsonObject { ["path"] = "docs/guide.md" }, CancellationToken.None);
        Assert.IsFalse(guide.IsError);
        StringAssert.Contains(guide.ModelText, "guide");

        // read_entry: a missing entry is a reported error, not a crash
        var missing = await read.InvokeAsync(new JsonObject { ["path"] = "nope.txt" }, CancellationToken.None);
        Assert.IsTrue(missing.IsError);

        // test_archive: every entry reads back cleanly for a good zip
        var test = tools.Single(t => t.Name == "test_archive");
        var integrity = await test.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(integrity.IsError);
        StringAssert.Contains(integrity.ModelText, "OK");
    }

    [TestMethod]
    [CoversNode("compressed-entry-tree")]
    public void ActivateRow_TogglesFolderVisibility()
    {
        using var fix = new Fixture();
        var vm = new CompressedViewModel(fix.ZipPath, fix.Shell, fix.Vfs);

        var docs = vm.VisibleRows.First(r => r.Name == "docs");
        // Collapse if auto-expanded, then expand and confirm children appear.
        if (docs.IsExpanded) vm.ActivateRowCommand.Execute(docs);
        Assert.IsFalse(vm.VisibleRows.Any(r => r.Name == "guide.md"));

        vm.ActivateRowCommand.Execute(docs);
        Assert.IsTrue(vm.VisibleRows.Any(r => r.Name == "guide.md"));
    }

    private sealed class Fixture : IDisposable
    {
        public VirtualFileSystem Vfs { get; }
        public IShellServices Shell { get; } = Substitute.For<IShellServices>();
        public string ZipPath { get; }
        private readonly string _dir;

        public Fixture()
        {
            Vfs = new VirtualFileSystem();
            Vfs.RegisterHandler(new ZipArchiveHandler());
            _dir = Path.Combine(Path.GetTempPath(), "nexa-cvm-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ZipPath = Path.Combine(_dir, "a.zip");
            using var zip = ZipFile.Open(ZipPath, ZipArchiveMode.Create);
            Add(zip, "readme.txt", "top");
            Add(zip, "docs/guide.md", "guide");
            Add(zip, "docs/img/logo.txt", "logo");
        }

        private static void Add(ZipArchive zip, string path, string body)
        {
            using var w = new StreamWriter(zip.CreateEntry(path).Open());
            w.Write(body);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }
    }
}
