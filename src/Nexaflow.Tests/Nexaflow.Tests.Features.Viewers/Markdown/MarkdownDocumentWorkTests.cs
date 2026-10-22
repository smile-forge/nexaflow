using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using System.IO;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Features.Markdown;

/// <summary>
/// The whole document with the workspace's AI, from the Markdown page: summarised or translated as the editor holds it — the
/// workspace's to do, as a background job — or, with no Documents model, the reader told where to choose one; what is made
/// opening beside it as a draft; and a link from it back into the document landing at its place.
/// </summary>
[TestClass]
public class MarkdownDocumentWorkTests
{
    private string _folder = "";

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(TestTemp.Root, "nexaflow-markdown-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, recursive: true);

    private MarkdownViewModel Make(out IShellServices shell, out IAIService ai, string name = "pets.md", bool works = true, string? draft = null,
                                   IDocumentWorkWatch? watch = null)
    {
        var path = Path.Combine(_folder, name);
        if (MarkdownPackage.Names(path)) Package(path, "# Pets\n\nDogs bark.\n\n![A dog](attachments/dog.png)\n");
        else File.WriteAllText(path, "# Pets\n\nDogs bark.\n");

        shell = Substitute.For<IShellServices>();
        ai = Substitute.For<IAIService>();

        var gate = Substitute.For<IAiAbilityGate>();
        gate.IsEnabled.Returns(works);
        gate.DisabledTip.Returns("No Documents model");
        gate.MakeReadyAsync(Arg.Any<CancellationToken>()).Returns(works);
        ai.Gate(Arg.Any<AiAbility[]>()).Returns(gate);
        ai.WatchDocumentWork(Arg.Any<string>()).Returns(watch ?? new WorkWatch());

        return new MarkdownViewModel(path, shell, draftFolder: draft, ai: ai);
    }

    /// <summary>A Markdown Package at <paramref name="path"/>: <paramref name="document"/>, and the picture it names.</summary>
    private static void Package(string path, string document)
    {
        // The app's file system has its archive handlers from the Compressed feature; a test process registers the zip one.
        if (!VirtualFileSystem.Instance.CanCreate("a" + MarkdownPackage.Extension)) VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry(MarkdownPackage.Document).Open())) writer.Write(document);
        using (var picture = zip.CreateEntry("attachments/dog.png").Open()) picture.Write([1, 2, 3]);
    }

    /// <summary>Work under way on a document, as the workspace says it, changed as a test says.</summary>
    private sealed class WorkWatch : ObservableObject, IDocumentWorkWatch
    {
        private IReadOnlyList<DocumentWorkUnderway> _underway = [];

        public IReadOnlyList<DocumentWorkUnderway> Underway
        {
            get => _underway;
            set => SetProperty(ref _underway, value);
        }

        public void Dispose() { }
    }

    [TestMethod]
    [CoversNode("markdown-summarize")]
    public void Summarise_HandsTheWorkspaceWhatTheEditorHolds()
    {
        var vm = Make(out _, out var ai);
        vm.Markdown = "# Pets\n\nDogs bark loudly.\n";

        vm.SummarizeCommand.Execute(null);

        ai.Received(1).SummarizeDocument(Arg.Is<DocumentAsked>(asked => asked.Path == vm.FilePath && asked.Source == "# Pets\n\nDogs bark loudly.\n"));
    }

    [TestMethod]
    [CoversNode("markdown-translate")]
    public void Translate_AsksTheLanguage_ThenHandsTheWorkspaceTheDocumentAndIt()
    {
        var vm = Make(out _, out var ai);

        vm.OpenTranslateCommand.Execute(null);
        Assert.IsTrue(vm.IsTranslateOpen, "the language is asked for first");

        vm.TranslateInto = "  Japanese ";
        vm.TranslateCommand.Execute(null);

        Assert.IsFalse(vm.IsTranslateOpen);
        ai.Received(1).TranslateDocument(Arg.Is<DocumentAsked>(asked => asked.Path == vm.FilePath), "Japanese");
    }

    [TestMethod]
    [CoversNode("markdown-translate")]
    public void CancellingTheLanguage_TranslatesNothing()
    {
        var vm = Make(out _, out var ai);

        vm.OpenTranslateCommand.Execute(null);
        vm.CancelTranslateCommand.Execute(null);

        Assert.IsFalse(vm.IsTranslateOpen);
        ai.DidNotReceiveWithAnyArgs().TranslateDocument(default!, default!);
    }

    [TestMethod]
    [CoversNode("markdown-summarize")]
    public void UntilTheDocumentsRowHasAModelReady_NothingIsSetGoing()
    {
        var vm = Make(out _, out var ai, works: false);

        vm.SummarizeCommand.Execute(null);

        ai.DidNotReceiveWithAnyArgs().SummarizeDocument(default!);
        ai.Received().Gate(Arg.Is<AiAbility[]>(asked => asked.SequenceEqual(new[] { AiAbility.Documents })));
    }

    [TestMethod]
    [CoversNode("markdown-summarize")]
    public void OnlyAMarkdownDocument_IsWorkedThroughAsAWhole_AndAButtonThatCannotSaysWhy()
    {
        Assert.IsTrue(Make(out _, out _).SummarizeCommand.CanExecute(null));

        var tune = Make(out _, out _, name: "tune.abc");
        Assert.IsFalse(tune.SummarizeCommand.CanExecute(null), "a file of one fenced language is no document of sections");
        Assert.IsFalse(tune.SummaryWork!.IsEnabled);
        StringAssert.Contains(tune.SummaryWork.DisabledTip, "tune.abc", "and its button says so, not that a model is missing");

        var draft = Make(out _, out _, draft: _folder);
        Assert.IsFalse(draft.OpenTranslateCommand.CanExecute(null), "nor is a draft not yet saved anywhere");
        Assert.IsFalse(draft.TranslationWork!.IsEnabled);
        Assert.AreNotEqual("No Documents model", draft.TranslationWork.DisabledTip, "which its button says too");

        var unready = Make(out _, out _, works: false);
        Assert.AreEqual("No Documents model", unready.SummaryWork!.DisabledTip, "with nothing else in the way, the model's own reason");
    }

    [TestMethod]
    [CoversNode("markdown-summarize")]
    public void APackage_IsWorkedThroughAsAWhole_AsAWordDocumentIs_ItsPicturesWhereTheEditorReadsThem()
    {
        var vm = Make(out _, out var ai, name: "pets.mdz");

        Assert.IsTrue(vm.SummarizeCommand.CanExecute(null), "a Markdown Package is a Markdown document");
        vm.SummarizeCommand.Execute(null);

        ai.Received(1).SummarizeDocument(Arg.Is<DocumentAsked>(asked =>
            asked.Path == vm.FilePath && asked.Source.Contains("Dogs bark.") && asked.Folder == vm.DocumentFolder));
        Assert.IsTrue(File.Exists(Path.Combine(vm.DocumentFolder!, "attachments", "dog.png")), "the folder handed over holds its pictures");
        vm.Release();
    }

    [TestMethod]
    [CoversNode("markdown-summarize")]
    [CoversNode("markdown-translate")]
    public void WorkUnderWay_IsShownOnItsButton_SayingWhatIsBeingDone()
    {
        var watch = new WorkWatch();
        var vm = Make(out _, out _, watch: watch);
        Assert.IsFalse(vm.IsSummarizing || vm.IsTranslating);

        watch.Underway =
        [
            new DocumentWorkUnderway(DocumentWorkKind.Summary, null, "Summarising: section 3 of 12"),
            new DocumentWorkUnderway(DocumentWorkKind.Translation, "French", "Translating: passage 2 of 9"),
        ];

        Assert.IsTrue(vm.IsSummarizing);
        Assert.IsFalse(vm.SummarizeCommand.CanExecute(null), "a second summary of it is not set going");
        StringAssert.Contains(vm.SummaryWork!.DisabledTip, "section 3 of 12", "its button says how far the one under way has got");

        Assert.IsTrue(vm.IsTranslating);
        Assert.IsTrue(vm.TranslationWork!.IsEnabled, "it can still be translated into another language");
        StringAssert.Contains(vm.TranslateTip, "French");
        StringAssert.Contains(vm.TranslateTip, "passage 2 of 9");

        watch.Underway = [];
        Assert.IsFalse(vm.IsSummarizing || vm.IsTranslating);
        Assert.IsTrue(vm.SummarizeCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("markdown-place-links")]
    public void ALinkIntoAMarkdownFileAtAPlace_OpensItThere()
    {
        var vm = Make(out var shell, out _);
        var target = Path.Combine(_folder, "notes.md");

        Assert.IsTrue(vm.FollowLink(new Uri(target).AbsoluteUri + "#heading%3Acats"));

        shell.Received(1).OpenTab(MarkdownTabRegistration.StaticPageKind,
            Arg.Is<Dictionary<string, string>>(asked => asked["path"] == target && asked[MarkdownTabRegistration.PlaceParameter] == "heading:cats"));
        shell.DidNotReceiveWithAnyArgs().HandleObject(default!);

        var package = Path.Combine(_folder, "notes.mdz");
        Assert.IsTrue(vm.FollowLink(new Uri(package).AbsoluteUri + "#heading%3Adogs"), "a package is led into as a file is");
        shell.Received(1).OpenTab(MarkdownTabRegistration.StaticPageKind,
            Arg.Is<Dictionary<string, string>>(asked => asked["path"] == package && asked[MarkdownTabRegistration.PlaceParameter] == "heading:dogs"));
    }

    [TestMethod]
    [CoversNode("markdown-save-as")]
    public void AMarkdownDraft_OpensBesideWhatTheReaderIsWorkingOn_OfferingTheFolderItWasMadeFrom()
    {
        var shell = Substitute.For<IShellServices>();
        var handler = new MarkdownDraftHandler(shell);
        var draft = new DraftFile(Path.Combine(_folder, "pets (summary).md"), _folder);
        File.WriteAllText(draft.Path, "# Pets\n");

        Assert.IsTrue(handler.CanHandleObject(draft));
        Assert.IsFalse(handler.CanHandleObject(new DraftFile(Path.Combine(_folder, "dog.png"), _folder)), "a picture is the picture viewer's");

        handler.Handle(draft);

        shell.Received(1).OpenTab(MarkdownTabRegistration.StaticPageKind,
            Arg.Is<Dictionary<string, string>>(asked => asked["path"] == draft.Path && asked[DraftFile.PageParameterName] == _folder),
            null, true);
    }
}
