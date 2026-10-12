using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Rtf.ClientTools;
using Nexaflow.Features.Rtf.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Text.Markdown;
using NSubstitute;

namespace Nexaflow.Tests.Features.Rtf;

/// <summary>
/// The rich-text page's view-model: the document handed to the surface as the RTF it is, the words it draws beside it,
/// the reasons it gives when there is no document to show, the file read again when it changes, and what the assistant
/// is told and can read.
/// </summary>
[TestClass]
[CoversNode("rtf-viewer")]
public class RtfViewModelTests
{
    private const string Letter = @"{\rtf1\ansi\ansicpg1252{\fonttbl{\f0 Arial;}}\pard Dear {\b Ada},\par The caf\'e9 opens at nine.\par}";

    private readonly List<string> _paths = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in _paths)
            if (File.Exists(path)) File.Delete(path);
    }

    private string Written(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexaflow-{Guid.NewGuid():N}.rtf");
        File.WriteAllText(path, content, Encoding.Latin1);
        _paths.Add(path);
        return path;
    }

    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Func<Task<SearchOutcome>>>()).Returns(call => call.Arg<Func<Task<SearchOutcome>>>()());
        return shell;
    }

    private async Task<RtfViewModel> LoadedAsync(string content, IShellServices? shell = null)
    {
        var vm = new RtfViewModel(Written(content), shell ?? Shell());
        await vm.LoadAsync();
        return vm;
    }

    // ── The document ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("rtf-viewer-document")]
    public async Task TheDocumentIsHandedToTheSurfaceAsTheRtfItIs_OnPaper()
    {
        using var vm = await LoadedAsync(Letter);

        Assert.IsFalse(vm.HasError, vm.ErrorMessage);
        Assert.AreEqual(Letter, vm.Document, "drawn from the RTF itself, byte for byte");
        Assert.AreEqual("rtf", vm.Language);
        Assert.AreSame(StyleFormat.Light, vm.Palette, "the writer's colours, on the paper they chose them against");
    }

    [TestMethod]
    [CoversNode("rtf-file-reading")]
    public async Task AByteLeftPastSevenBits_ReachesTheReaderAsTheByteItIs()
    {
        // A writer may leave a character of the document's code page unescaped. Read as Latin-1, the byte is kept for the
        // reader to decode in the code page the document names — here 1251, where 0xE4 is a Cyrillic de.
        var path = Path.Combine(Path.GetTempPath(), $"nexaflow-{Guid.NewGuid():N}.rtf");
        File.WriteAllBytes(path, [.. Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg1251\pard "), 0xE4, .. Encoding.ASCII.GetBytes(@"\par}")]);
        _paths.Add(path);

        using var vm = new RtfViewModel(path, Shell());
        await vm.LoadAsync();

        Assert.AreEqual('\u00E4', vm.Document[^6], "one character per byte, undecoded");
        Assert.AreEqual("\u0434", vm.Words);
    }

    [TestMethod]
    [CoversNode("rtf-viewer-document")]
    public async Task ItsWordsAreTheOnesItDraws_NeverItsControlWords()
    {
        using var vm = await LoadedAsync(Letter);

        Assert.AreEqual("Dear Ada,\nThe caf\u00E9 opens at nine.", vm.Words);
    }

    [TestMethod]
    [CoversNode("rtf-viewer-footer")]
    public async Task TheFooterCountsWordsAndParagraphs_AndGivesTheFileSize()
    {
        using var vm = await LoadedAsync(Letter);

        Assert.AreEqual(7, vm.WordCount);
        Assert.AreEqual(2, vm.ParagraphCount);
        Assert.AreEqual(SizeFormatter.FormatBytes(Letter.Length), vm.FileSizeText);
    }

    [TestMethod]
    [CoversNode("rtf-viewer-document")]
    public async Task ALinkOutOfTheDocumentIsTheShellsToRoute()
    {
        var shell = Shell();
        shell.HandleObject("https://example.com").Returns(true);
        using var vm = await LoadedAsync(Letter, shell);

        Assert.IsTrue(vm.FollowLink("https://example.com"));
        Assert.IsFalse(vm.FollowLink(" "), "nothing to follow");
        shell.Received(1).HandleObject("https://example.com");
    }

    [TestMethod]
    [CoversNode("rtf-open-externally")]
    public void OpeningExternallyAFileThatIsGone_SaysWhy()
    {
        var shell = Shell();
        using var vm = new RtfViewModel(Path.Combine(Path.GetTempPath(), $"nexaflow-{Guid.NewGuid():N}.rtf"), shell);

        vm.OpenExternallyCommand.Execute(null);

        shell.Received(1).ShowError(Arg.Is<string>(message => message.Contains("Couldn't open the document externally")));
    }

    // ── No document to show ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("rtf-viewer-error")]
    public async Task AFileThatIsNotRtfSaysSo_RatherThanDrawingItsTextAsADocument()
    {
        using var vm = await LoadedAsync("Just some words, saved with the wrong name.");

        Assert.IsTrue(vm.HasError);
        StringAssert.Contains(vm.ErrorMessage, "isn't a rich-text document");
        Assert.AreEqual(string.Empty, vm.Document);
    }

    [TestMethod]
    [CoversNode("rtf-viewer-error")]
    public async Task AFileThatCannotBeReadSaysWhy()
    {
        using var vm = new RtfViewModel(Path.Combine(Path.GetTempPath(), $"nexaflow-{Guid.NewGuid():N}.rtf"), Shell());
        await vm.LoadAsync();

        Assert.IsTrue(vm.HasError);
        StringAssert.Contains(vm.ErrorMessage, "Couldn't read this document");
    }

    // ── The AI gate ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("rtf-load-gate")]
    public async Task TheAiIsHeldBackUntilTheFileHasLoaded()
    {
        using var vm = new RtfViewModel(Written(Letter), Shell());

        Assert.IsFalse(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "still loading");

        await vm.LoadAsync();

        Assert.IsTrue(vm.IsContextReady);
    }

    [TestMethod]
    [CoversNode("rtf-load-gate")]
    public async Task TheGateIsReleasedEvenWhenTheLoadFails()
    {
        // Otherwise a broken file leaves the conversation waiting forever for a page that will never load.
        using var vm = await LoadedAsync("not rtf");

        Assert.IsTrue(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "could not be shown");
    }

    // ── The file on disk ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("rtf-reload")]
    public async Task TheFileIsWatched_HeldWhileTheTabIsOffScreen_AndLetGoOnClose()
    {
        var shell = Shell();
        var watch = Substitute.For<IFileWatch>();
        shell.WatchFile(Arg.Any<string>(), Arg.Any<Action>()).Returns(watch);

        var vm = await LoadedAsync(Letter, shell);
        shell.Received(1).WatchFile(vm.FilePath, Arg.Any<Action>());

        vm.OnScreen(false);
        watch.Received().Enabled = false;

        vm.OnScreen(true);
        watch.Received().Enabled = true;

        vm.Dispose();
        watch.Received(1).Dispose();
    }

    [TestMethod]
    [CoversNode("rtf-reload")]
    public async Task AChangeOnDiskIsReadAgain()
    {
        using var vm = await LoadedAsync(Letter);
        const string Revised = @"{\rtf1\ansi\pard Dear Grace,\par}";

        File.WriteAllText(vm.FilePath, Revised, Encoding.Latin1);
        await vm.ReloadAsync();

        Assert.AreEqual(Revised, vm.Document);
        Assert.AreEqual("Dear Grace,", vm.Words);
        Assert.AreEqual(2, vm.WordCount);
    }

    [TestMethod]
    [CoversNode("rtf-reload")]
    public async Task AReloadDropsTheSearchShownOverTheOldDocument()
    {
        using var vm = await LoadedAsync(Letter);
        var cleared = 0;
        vm.FindInDocument = _ => [new RenderedMatch(0, "Dear Ada,")];
        vm.ClearDocument = () => cleared++;

        await vm.SearchAsync(new SearchRequest("Ada"), display: true, CancellationToken.None);
        Assert.IsTrue(vm.IsSearchActive);

        await vm.ReloadAsync();

        Assert.IsFalse(vm.IsSearchActive, "its highlights were over words that may no longer be there");
        Assert.IsTrue(cleared > 0);
    }

    // ── The assistant ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("rtf-viewer-ai-context")]
    public async Task TheContextNamesTheDocumentAndItsSize_AndPointsAtTheWords()
    {
        using var vm = await LoadedAsync(Letter);
        var context = vm.GetContext();

        StringAssert.Contains(context, vm.FileName);
        StringAssert.Contains(context, "7 word(s) in 2 paragraph(s)");
        StringAssert.Contains(context, "rtf_read_text");
        Assert.IsFalse(context.Contains("Ada"), "the words are read on request, not poured into every turn");

        Assert.AreEqual(vm.FilePath, vm.GetSecurityContext());
        var place = vm.GetContextObject() as FileSystemContext;
        Assert.IsNotNull(place);
        CollectionAssert.AreEqual(new[] { vm.FilePath }, place.SelectedItems.ToArray());
    }

    [TestMethod]
    [CoversNode("rtf-read-text")]
    public async Task ReadText_IsTheOneTool_AndOnlyReads()
    {
        using var vm = await LoadedAsync(Letter);
        var tools = vm.GetClientTools();

        CollectionAssert.AreEqual(new[] { "rtf_read_text" }, tools.Select(t => t.Name).ToArray());
        Assert.AreEqual(ToolSafety.SafeOperation, tools[0].Safety);
    }

    [TestMethod]
    [CoversNode("rtf-read-text")]
    public async Task ReadText_GivesTheWordsAParagraphToALine_Numbered()
    {
        using var vm = await LoadedAsync(Letter);

        var all = await Invoke(vm);
        Assert.IsTrue(all.Success);
        Assert.AreEqual("1: Dear Ada,\n2: The caf\u00E9 opens at nine.", all.ModelText);

        var second = await Invoke(vm, new JsonObject { ["from"] = 2, ["count"] = 1 });
        Assert.AreEqual("2: The caf\u00E9 opens at nine.", second.ModelText);
    }

    [TestMethod]
    [CoversNode("rtf-read-text")]
    public async Task ReadText_RefusesAParagraphThatIsNotThere()
    {
        using var vm = await LoadedAsync(Letter);

        var result = await Invoke(vm, new JsonObject { ["from"] = 50 });

        Assert.IsFalse(result.Success, "reading paragraph 1 instead would have the model reason about the wrong part");
        StringAssert.Contains(result.ModelText, "has 2");
    }

    [TestMethod]
    [CoversNode("rtf-read-text")]
    public async Task ReadText_ReadsALongDocumentAStretchAtATime_AndSaysWhereToCarryOn()
    {
        var paragraph = new string('w', 1000);
        var body = string.Concat(Enumerable.Repeat(paragraph + @"\par ", 100));
        using var vm = await LoadedAsync(@"{\rtf1\ansi\pard " + body + "}");

        var first = await Invoke(vm);
        Assert.IsTrue(first.ModelText.Length <= RtfTools.ReadCap + 200);
        StringAssert.Contains(first.ModelText, "call again with from=");

        var next = int.Parse(Regex.Match(first.ModelText, @"from=(\d+)").Groups[1].Value);
        var rest = await Invoke(vm, new JsonObject { ["from"] = next });
        Assert.IsTrue(rest.ModelText.StartsWith($"{next}: "), "carries on where the first answer stopped");
    }

    [TestMethod]
    [CoversNode("rtf-read-text")]
    public async Task ReadText_SaysWhyWhenThereIsNothingToRead()
    {
        using var unread = new RtfViewModel(Written(Letter), Shell());
        Assert.IsFalse((await Invoke(unread)).Success, "not loaded yet");

        using var broken = await LoadedAsync("not rtf");
        StringAssert.Contains((await Invoke(broken)).ModelText, "couldn't be read");
    }

    private static Task<ToolResult> Invoke(RtfViewModel vm, JsonObject? args = null) =>
        vm.GetClientTools().Single(t => t.Name == "rtf_read_text").InvokeAsync(args ?? [], CancellationToken.None);
}
