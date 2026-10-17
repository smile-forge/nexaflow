using System.IO;
using Nexaflow.Features.Audio;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Video;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Drafts;

/// <summary>
/// A file made for the user and not saved yet, handed to the shell as a <see cref="DraftFile"/>: the viewer for its kind
/// claims it and opens it as a draft tab, whose Save as starts in the folder offered — and a picture kept from a draft
/// takes its edits with it.
/// </summary>
[TestClass]
[CoversNode("made-file-drafts")]
public class DraftHandlerTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup() => _dir = TestTemp.CreateSubdirectory("nexaflow-drafts-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Made(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [TestMethod]
    public void EachKind_IsClaimedByItsOwnViewer_AndNothingElse()
    {
        var shell = Substitute.For<IShellServices>();
        IGenericObjectHandler[] handlers = [new ImageDraftHandler(shell), new AudioDraftHandler(shell), new VideoDraftHandler(shell)];
        string[] Claiming(object made) => [.. handlers.Where(h => h.CanHandleObject(made)).Select(h => h.GetType().Name)];

        CollectionAssert.AreEqual(new[] { nameof(ImageDraftHandler) }, Claiming(new DraftFile(Made("fox.png"))));
        CollectionAssert.AreEqual(new[] { nameof(AudioDraftHandler) }, Claiming(new DraftFile(Made("hello.wav"))));
        CollectionAssert.AreEqual(new[] { nameof(VideoDraftHandler) }, Claiming(new DraftFile(Made("waves.mp4"))));
        Assert.AreEqual(0, Claiming(new DraftFile(Made("notes.txt"))).Length, "no viewer here shows it");
        Assert.AreEqual(0, Claiming(new DraftFile(Path.Combine(_dir, "gone.png"))).Length, "a draft no longer there");
        Assert.AreEqual(0, Claiming(Made("plain.png")).Length, "a plain path is a file opened where it lives, not a draft");
    }

    [TestMethod]
    public void ADraft_OpensInItsViewer_WithTheFolderItsSaveAsStartsIn()
    {
        var shell = Substitute.For<IShellServices>();
        var picture = Made("fox.png");
        var clip = Made("waves.mp4");

        new ImageDraftHandler(shell).Handle(new DraftFile(picture, @"C:\Pictures"));
        new VideoDraftHandler(shell).Handle(new DraftFile(clip));

        shell.Received(1).OpenTab("Images", Arg.Is<Dictionary<string, string>>(p =>
            p["paths"] == picture && p[DraftFile.PageParameterName] == @"C:\Pictures"), Arg.Any<IPageView?>(), Arg.Any<bool>());
        shell.Received(1).OpenTab("Video", Arg.Is<Dictionary<string, string>>(p =>
            p["path"] == clip && p[DraftFile.PageParameterName] == ""), Arg.Any<IPageView?>(), Arg.Any<bool>());
    }

    [TestMethod]
    public async Task APictureKeptFromADraft_TakesItsEdits_AndNeverTheEditsOfAPictureItReplaced()
    {
        using var recipes = new ImageRecipeStore(TimeSpan.FromHours(1));
        var draft = Made("fox.png");
        File.WriteAllText(ImageRecipeStore.SidecarPath(draft), "the draft's edits");
        var kept = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "kept")).FullName, "fox.png");

        await ImageDraftHandler.KeepAsync(recipes, draft, kept);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(kept));
        Assert.AreEqual("the draft's edits", File.ReadAllText(ImageRecipeStore.SidecarPath(kept)));

        var plain = Made("plain.png");
        await ImageDraftHandler.KeepAsync(recipes, plain, kept);

        Assert.IsFalse(File.Exists(ImageRecipeStore.SidecarPath(kept)), "the replaced picture's edits do not land on this one");
    }
}
