using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editor;
using NSubstitute;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Tests.Visuals.Editor;

/// <summary>
/// Covers the read-write Edit Text view-model's save path (the data-loss-sensitive part): encoding/BOM
/// round-trip, EOL normalization, dirty tracking, and the over-size read-only guard — headless, through the page's
/// own document and its view of it, with nothing drawing either.
/// </summary>
[TestClass]
[CoversNode("vtext-editor")]
public class FileTextEditorViewModelTests
{
    private const long Big = 50L * 1024 * 1024;

    private static string Temp() => Path.Combine(TestTemp.Root, $"editvm_{Guid.NewGuid():N}.txt");

    /// <summary>
    /// Runs a file operation, tolerating the brief window where the editor still holds the file open.
    /// <para>The watcher callback is <c>async void</c>, so a test that fires it cannot await it: an
    /// in-flight <c>File.ReadAllTextAsync</c> from one burst can still hold the handle when the test
    /// writes the next change or deletes the temp file. Reading the file it was told changed is exactly
    /// what the editor is supposed to do, so the test waits for it instead of racing it — which is also
    /// what a real external editor writing the same file would have to do.</para>
    /// </summary>
    private static void WhenFree(Action op)
    {
        for (var i = 0; i < 100; i++)
        {
            try { op(); return; }
            catch (IOException) { Thread.Sleep(20); }
            catch (UnauthorizedAccessException) { Thread.Sleep(20); }
        }
        op();   // last attempt: let a genuine failure surface as itself
    }

    /// <summary>Cleanup form — a leaked temp file must never turn a green test red.</summary>
    private static void DeleteWhenFree(string path)
    {
        try { WhenFree(() => File.Delete(path)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IShellServices Shell()
    {
        var s = Substitute.For<IShellServices>();
        s.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(Task.FromResult(true));
        return s;
    }

    /// <summary>Writes <paramref name="text"/> in place of everything the page holds, as an edit would — one step that can be taken back.</summary>
    private static void Rewrite(FileTextEditorViewModel vm, string text) =>
        vm.Document.Write(ContentChange.Write(0, vm.Document.Current.Length, text));

    private static bool HasUtf8Bom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    [TestMethod]
    [CoversNode("code-save")]
    public async Task Save_RoundTripsUtf8NoBom()
    {
        var path = Temp();
        File.WriteAllText(path, "alpha\nbeta\n", new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();
            Assert.IsFalse(vm.IsReadOnlyMode);
            Assert.AreEqual("alpha\nbeta\n", vm.Document.ToString());
            Assert.IsFalse(vm.IsDirty);

            Rewrite(vm, "alpha\nbeta\ngamma\n");
            Assert.IsTrue(vm.IsDirty);

            await vm.SaveCommand.ExecuteAsync(null);

            Assert.IsFalse(vm.IsDirty, "what was saved is what it holds");
            Assert.IsFalse(HasUtf8Bom(File.ReadAllBytes(path)));
            Assert.AreEqual("alpha\nbeta\ngamma\n", File.ReadAllText(path));
        }
        finally { DeleteWhenFree(path); }
    }

    [TestMethod]
    [CoversNode("code-ai-act")]
    [CoversNode("code-ai-context")]
    public async Task AiTools_ReadEditReplaceSave_AndScopeReadinessPreview()
    {
        var path = Temp();
        File.WriteAllText(path, "alpha\nbeta\n", new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();

            Assert.IsTrue(vm.IsContextReady);                     // gated on load completing
            Assert.AreEqual(path, vm.GetSecurityContext());       // aspect 4: file-scoped
            Assert.IsInstanceOfType(vm, typeof(IContextPreview)); // offers a read-only preview
            StringAssert.Contains(vm.GetContext(), Path.GetFileName(path));  // context names the file…
            StringAssert.Contains(vm.GetContext(), "alpha");                 // …and carries its content

            var tools = vm.GetClientTools();
            var get  = tools.Single(t => t.Name == "get_editor_text");
            var set  = tools.Single(t => t.Name == "set_editor_text");
            var repl = tools.Single(t => t.Name == "replace_all");
            var save = tools.Single(t => t.Name == "save_file");

            // get_editor_text returns the current document (its summary carries the text)
            var g = await get.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.AreEqual("alpha\nbeta\n", g.Summary);

            // set_editor_text replaces the whole document (unsaved), as one step that can be taken back
            await set.InvokeAsync(new JsonObject { ["text"] = "one\ntwo\nthree\n" }, CancellationToken.None);
            Assert.AreEqual("one\ntwo\nthree\n", vm.Document.ToString());
            Assert.IsTrue(vm.IsDirty);
            Assert.IsTrue(vm.Document.CanUndo, "an assistant's edit is taken back as the reader's are");

            // replace_all edits in place (case-insensitive by default) and reports the count
            var r = await repl.InvokeAsync(new JsonObject { ["find"] = "o", ["replace"] = "0" }, CancellationToken.None);
            Assert.IsFalse(r.IsError);
            StringAssert.Contains(vm.Document.ToString(), "0ne");   // "one" → "0ne", "two" → "tw0"

            // save_file persists to disk and clears the dirty flag
            await save.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(vm.IsDirty);
            StringAssert.Contains(File.ReadAllText(path), "0ne");
        }
        finally { DeleteWhenFree(path); }
    }

    /// <summary>
    /// A project file has no highlighting grammar, so the editor's structural tools used to have nothing to say
    /// about it. It is XML to an edit, and an element path reaches the element nothing names.
    /// </summary>
    [TestMethod]
    [CoversNode("code-ai-act")]
    public async Task EditDeclaration_At_EditsAProjectFile_AndRefusesAPathAndAnIdTogether()
    {
        var path = Path.Combine(TestTemp.Root, $"editvm_{Guid.NewGuid():N}.csproj");
        File.WriteAllText(path,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"NAudio\" Version=\"2.2.1\" />\n  </ItemGroup>\n</Project>\n",
            new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();
            var edit = vm.GetClientTools().Single(t => t.Name == "edit_declaration");

            var both = await edit.InvokeAsync(new JsonObject
            {
                ["op"] = "delete", ["ast_path"] = "T:Project", ["at"] = "//PackageReference",
            }, CancellationToken.None);
            Assert.IsTrue(both.IsError, "ast_path and at each name the element; given both, they could disagree");

            var set = await edit.InvokeAsync(new JsonObject
            {
                ["op"] = "set_attribute", ["at"] = "//PackageReference[@Include='NAudio']", ["name"] = "Version",
                ["text"] = "3.0.0",
            }, CancellationToken.None);
            Assert.IsFalse(set.IsError, set.ModelText);
            StringAssert.Contains(vm.Document.ToString(), "<PackageReference Include=\"NAudio\" Version=\"3.0.0\" />");
        }
        finally { DeleteWhenFree(path); }
    }

    [TestMethod]
    [CoversNode("code-encoding")]
    public async Task Save_PreservesBom_WhenOriginalHadBom()
    {
        var path = Temp();
        File.WriteAllText(path, "x\n", new UTF8Encoding(true)); // BOM
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();
            Assert.AreEqual("UTF-8 with BOM", vm.SelectedEncoding.Name);

            Rewrite(vm, "x\ny\n");
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.IsTrue(HasUtf8Bom(File.ReadAllBytes(path)), "BOM should be re-emitted on save");
        }
        finally { DeleteWhenFree(path); }
    }

    [TestMethod]
    [CoversNode("code-too-large")]
    public async Task OverSizeLimit_IsShownReadOnlyOverItsBlocks_AndCannotSave()
    {
        var path = Temp();
        File.WriteAllText(path, new string('a', 5000));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, maxEditableBytes: 1000);
            await vm.LoadAsync();

            Assert.IsTrue(vm.IsReadOnlyMode);
            Assert.AreEqual(new string('a', 5000), vm.Document.ToString(), "shown, read from the file a block at a time");
            Assert.IsFalse(vm.SaveCommand.CanExecute(null));
            Assert.AreEqual(0, vm.CommandGroups.Count + vm.LineCommands.Count, "no command makes a file read over its blocks one string");

            var read = await vm.GetClientTools().Single(t => t.Name == "get_editor_text").InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsTrue(read.IsError, "there is no whole of it to hand the assistant");
        }
        finally { DeleteWhenFree(path); }
    }

    [TestMethod]
    [CoversNode("vtext-editor-host")]
    public void LineCommands_HideLineReorderingForCode()
    {
        using var code = new FileTextEditorViewModel("snippet.cs", Shell(), VirtualFileSystem.Instance, Big);
        using var text = new FileTextEditorViewModel("notes.txt", Shell(), VirtualFileSystem.Instance, Big);

        // Line ops live in the footer "Lines" popup, not the floating panel.
        static bool OffersSort(FileTextEditorViewModel vm) => vm.LineCommands.Any(c => c.Name.Contains("Sort"));

        Assert.IsFalse(OffersSort(code), "code files must not offer line sorting");
        Assert.IsTrue(OffersSort(text), "text files should offer line sorting");
        Assert.IsFalse(code.CommandGroups.Any(g => g.Name == "Lines"), "Lines moved out of the floating panel");
    }

    [TestMethod]
    [CoversNode("vtext-editor-host")]
    [CoversNode("code-commands")]
    public void CommandGroups_HideSelectionOnlyGroups_UntilSelectionExists()
    {
        using var vm = new FileTextEditorViewModel("notes.txt", Shell(), VirtualFileSystem.Instance, Big);
        vm.Document.Load("hello world");

        // No selection yet: Encode/Decode (all selection-scoped) are hidden; Checksum stays (it has document ops).
        Assert.IsFalse(vm.CommandGroups.Any(g => g.Name is "Encode" or "Decode"), "selection-only groups hidden without a selection");
        Assert.IsTrue(vm.CommandGroups.Any(g => g.Name == "Checksum"), "Checksum stays (document-scoped entries)");

        vm.Editor.Select(0, 5);
        Assert.IsTrue(vm.CommandGroups.Any(g => g.Name == "Encode"), "Encode appears once there's a selection");
        Assert.IsTrue(vm.CommandGroups.Any(g => g.Name == "Decode"), "Decode appears once there's a selection");

        vm.Editor.MoveCaretTo(3);
        Assert.IsFalse(vm.CommandGroups.Any(g => g.Name is "Encode" or "Decode"), "they hide again when the selection clears");
    }

    [TestMethod]
    [CoversNode("code-commands")]
    public void ASelectionCommand_WritesOverWhatIsPickedOut_AsOneStep()
    {
        using var vm = new FileTextEditorViewModel("notes.txt", Shell(), VirtualFileSystem.Instance, Big);
        vm.Document.Load("hello world");
        vm.Editor.Select(6, 5);

        var encode = vm.CommandGroups.SelectMany(g => g.Commands)
            .FirstOrDefault(c => c.Name.Contains("Base64", StringComparison.OrdinalIgnoreCase) && c.Name.Contains("encode", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(encode, "a selection offers its encodings");

        encode!.Run.Execute(null);

        Assert.AreEqual("hello " + Convert.ToBase64String(Encoding.UTF8.GetBytes("world")), vm.Document.ToString());
        Assert.IsNotNull(vm.Document.Undo(new EditState(vm.Document.Current, 0)));
        Assert.AreEqual("hello world", vm.Document.ToString(), "one step takes it back");
    }

    [TestMethod]
    [CoversNode("code-eol")]
    public async Task Eol_NormalizedToCrlfOnSave()
    {
        var path = Temp();
        File.WriteAllText(path, "a\nb\n", new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();
            vm.SelectedEol = vm.AvailableEols.First(e => e.Eol == LineEnding.CrLf);

            Rewrite(vm, "a\nb\nc\n"); // marks dirty
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.AreEqual("a\r\nb\r\nc\r\n", File.ReadAllText(path));
        }
        finally { DeleteWhenFree(path); }
    }

    // ── Toolbar: line-number gutter, whitespace ───────────────────────────────

    [TestMethod]
    [CoversNode("vtext-editor-host")]
    [CoversNode("code-line-numbers")]
    public void LineNumbers_AreOnByDefault_AndTheToggleFlipsThem()
    {
        using var vm = new FileTextEditorViewModel("snippet.cs", Shell(), VirtualFileSystem.Instance, Big);

        Assert.IsTrue(vm.ShowLineNumbers, "code opens with the gutter visible");

        vm.ShowLineNumbers = false;
        Assert.IsFalse(vm.ShowLineNumbers);

        vm.ShowLineNumbers = true;
        Assert.IsTrue(vm.ShowLineNumbers);
    }

    [TestMethod]
    [CoversNode("vtext-editor-host")]
    public void ACodeFile_IsWrittenInItsGrammar_AndAnythingElseInPlainText()
    {
        using var code = new FileTextEditorViewModel("snippet.cs", Shell(), VirtualFileSystem.Instance, Big);
        using var text = new FileTextEditorViewModel("notes.txt", Shell(), VirtualFileSystem.Instance, Big);

        Assert.AreEqual("c-sharp", code.WrittenIn);
        Assert.IsFalse(code.IsPlainText);
        Assert.AreEqual(Nexaflow.Markdown.PlainText.TextParser.Language, text.WrittenIn, "plain text, never markdown");
        Assert.IsTrue(text.IsPlainText);
        Assert.IsFalse(text.ShowWhitespace, "whitespace is shown only when asked for");
    }

    // ── Status bar ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("code-status-filesize")]
    public async Task FileSize_IsReportedOnLoad_AndGrowsAfterASave()
    {
        var path = Temp();
        File.WriteAllText(path, "abc", new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();
            var onLoad = vm.FileSizeText;
            Assert.IsFalse(string.IsNullOrWhiteSpace(onLoad), "the footer shows a size once the file is read");

            Rewrite(vm, new string('x', 20_000));
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.AreNotEqual(onLoad, vm.FileSizeText, "the footer size refreshes from disk after a save");
        }
        finally { DeleteWhenFree(path); }
    }

    [TestMethod]
    [CoversNode("code-status-state")]
    public async Task UnsavedFlag_FollowsTheBuffer_AndUndoingBackToTheSavedStateClearsIt()
    {
        var path = Temp();
        File.WriteAllText(path, "one\n", new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();
            Assert.IsFalse(vm.IsDirty);
            Assert.IsFalse(vm.IsReadOnlyMode, "an editable file shows no read-only flag");

            Rewrite(vm, "one\ntwo\n");
            Assert.IsTrue(vm.IsDirty, "the '● unsaved' flag follows the buffer");
            Assert.AreEqual(3, vm.LineCount, "the footer counts the lines it holds now");

            vm.Document.Undo(new EditState(vm.Document.Current, 0));     // back to the saved state
            Assert.IsFalse(vm.IsDirty, "undoing every edit clears unsaved, it doesn't stay stuck on");
        }
        finally { DeleteWhenFree(path); }
    }

    [TestMethod]
    [CoversNode("code-status-state")]
    public async Task ReadOnlyFlag_IsSet_ForAFileOverTheEditableCeiling()
    {
        var path = Temp();
        File.WriteAllText(path, new string('a', 5000));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, maxEditableBytes: 1000);
            await vm.LoadAsync();

            Assert.IsTrue(vm.IsReadOnlyMode, "the footer shows 'read-only' for a file too large to edit");
            Assert.IsFalse(vm.IsDirty);
        }
        finally { DeleteWhenFree(path); }
    }

    // ── Reload (F5) + the external-change watch ───────────────────────────────

    [TestMethod]
    [CoversNode("code-reload")]
    public async Task Refresh_RereadsTheFileFromDisk()
    {
        var path = Temp();
        File.WriteAllText(path, "original\n", new UTF8Encoding(false));
        try
        {
            using var vm = new FileTextEditorViewModel(path, Shell(), VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();

            File.WriteAllText(path, "changed elsewhere\n", new UTF8Encoding(false));
            await vm.RefreshCommand.ExecuteAsync(null);

            Assert.AreEqual("changed elsewhere\n", vm.Document.ToString());
            Assert.IsFalse(vm.IsDirty, "a reload is the new saved baseline");
            Assert.IsFalse(vm.Document.CanUndo, "nothing before a reload is taken back into it");
        }
        finally { DeleteWhenFree(path); }
    }

    [TestMethod]
    [CoversNode("code-reload")]
    public async Task Refresh_WithUnsavedEdits_AsksBeforeDiscardingThem()
    {
        var path = Temp();
        File.WriteAllText(path, "original\n", new UTF8Encoding(false));
        try
        {
            var shell = Substitute.For<IShellServices>();
            shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult(false));           // the user declines

            using var vm = new FileTextEditorViewModel(path, shell, VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();
            Rewrite(vm, "my unsaved work\n");

            File.WriteAllText(path, "changed elsewhere\n", new UTF8Encoding(false));
            await vm.RefreshCommand.ExecuteAsync(null);

            Assert.AreEqual("my unsaved work\n", vm.Document.ToString(), "declining the prompt must keep the edits");
            await shell.Received().ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally { DeleteWhenFree(path); }
    }

    [TestMethod]
    [CoversNode("code-reload")]
    [CoversNode("code-change-banner")]
    public async Task ExternalChange_ReloadsAndRaisesTheBanner_ButIgnoresTheEditorsOwnWrite()
    {
        var path = Temp();
        File.WriteAllText(path, "original\n", new UTF8Encoding(false));
        try
        {
            // Capture the callback the view-model hands the shell's watcher, so the "file changed on disk"
            // burst can be delivered deterministically instead of racing a real FileSystemWatcher.
            Action? onChanged = null;
            var shell = Substitute.For<IShellServices>();
            shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult(true));
            shell.WatchFile(Arg.Any<string>(), Arg.Do<Action>(a => onChanged = a))
                 .Returns(Substitute.For<IFileWatch>());

            using var vm = new FileTextEditorViewModel(path, shell, VirtualFileSystem.Instance, Big);
            await vm.LoadAsync();
            Assert.IsNotNull(onChanged, "the editor should watch the file it opened");
            Assert.IsFalse(vm.BannerVisible);

            // A burst whose on-disk content still equals the buffer is our own save — ignored.
            onChanged!();
            await Task.Delay(50);
            Assert.IsFalse(vm.BannerVisible, "the editor's own write must not raise the banner");

            // A real external edit reloads and announces it. The previous burst's read may still be in
            // flight, so stand in for an external editor and wait for the handle rather than colliding.
            WhenFree(() => File.WriteAllText(path, "changed elsewhere\n", new UTF8Encoding(false)));
            onChanged!();
            for (int i = 0; i < 100 && !vm.BannerVisible; i++) await Task.Delay(10);

            Assert.AreEqual("changed elsewhere\n", vm.Document.ToString());
            Assert.IsTrue(vm.BannerVisible);
            StringAssert.Contains(vm.BannerMessage, "changed on disk");
        }
        finally { DeleteWhenFree(path); }
    }
}
