using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using System.IO;

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

    private MarkdownViewModel Make(out IShellServices shell, out IAIService ai, string name = "pets.md", bool works = true, string? draft = null)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "# Pets\n\nDogs bark.\n");

        shell = Substitute.For<IShellServices>();
        ai = Substitute.For<IAIService>();

        var gate = Substitute.For<IAiAbilityGate>();
        gate.IsEnabled.Returns(works);
        gate.MakeReadyAsync(Arg.Any<CancellationToken>()).Returns(works);
        ai.Gate(Arg.Any<AiAbility[]>()).Returns(gate);

        return new MarkdownViewModel(path, shell, draftFolder: draft, ai: ai);
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
    public void OnlyAMarkdownFileOfItsOwn_IsWorkedThroughAsAWhole()
    {
        Assert.IsTrue(Make(out _, out _).SummarizeCommand.CanExecute(null));
        Assert.IsFalse(Make(out _, out _, name: "tune.abc").SummarizeCommand.CanExecute(null), "a file of one fenced language is no document of sections");
        Assert.IsFalse(Make(out _, out _, draft: _folder).OpenTranslateCommand.CanExecute(null), "nor is a draft not yet saved anywhere");
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
