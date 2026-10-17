using System.IO;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Drafts;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// A draft's Save as: asked where under the draft's own name and extension, starting in the folder offered; kept there by
/// the viewer's own keep, or as a copy; and the tab told where — or nothing at all when the user cancels, and why when it
/// could not be kept.
/// </summary>
[TestClass]
[CoversNode("made-file-drafts")]
public class DraftSavingTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup() => _dir = TestTemp.CreateSubdirectory("nexaflow-draft-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Draft()
    {
        var path = Path.Combine(_dir, "fox.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private static Func<string, IReadOnlyList<string>, string?, Task<string?>> Picking(string? picked)
        => (_, _, _) => Task.FromResult(picked);

    [TestMethod]
    public async Task SaveAs_AsksUnderTheDraftsName_InTheFolderOffered_KeepsItAsWhatItIs_AndSaysWhere()
    {
        var draft = Draft();
        (string Name, IReadOnlyList<string> Extensions, string? Folder)? asked = null;
        var picked = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "kept")).FullName, "my fox.jpg");
        var saving = new DraftSaving(draft, @"C:\Pictures",
                                     (name, extensions, folder) => { asked = (name, extensions, folder); return Task.FromResult<string?>(picked); },
                                     why => Assert.Fail(why));
        string? kept = null;
        saving.Kept += path => kept = path;

        await saving.SaveAsCommand.ExecuteAsync(null);

        Assert.AreEqual("fox", asked!.Value.Name);
        CollectionAssert.AreEqual(new[] { ".png" }, asked.Value.Extensions.ToArray());
        Assert.AreEqual(@"C:\Pictures", asked.Value.Folder);
        var expected = Path.ChangeExtension(picked, ".png");
        Assert.AreEqual(expected, kept, "kept as what it is: under its own extension");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(expected));
        Assert.IsTrue(File.Exists(draft), "the draft stays where it was until Windows clears it");
    }

    [TestMethod]
    public async Task Cancelling_KeepsNothing_AndTheTabStaysADraft()
    {
        var keeps = 0;
        var saving = new DraftSaving(Draft(), null, Picking(null), why => Assert.Fail(why), (_, _) => { keeps++; return Task.CompletedTask; });
        var told = false;
        saving.Kept += _ => told = true;

        await saving.SaveAsCommand.ExecuteAsync(null);

        Assert.AreEqual(0, keeps);
        Assert.IsFalse(told);
    }

    [TestMethod]
    public async Task AKeepThatFails_IsToldToTheUser_AndTheTabStaysADraft()
    {
        string? failed = null;
        var saving = new DraftSaving(Draft(), null, Picking(Path.Combine(_dir, "kept.png")), why => failed = why,
                                     (_, _) => throw new IOException("There is not enough space on the disk."));
        var told = false;
        saving.Kept += _ => told = true;

        await saving.SaveAsCommand.ExecuteAsync(null);

        Assert.AreEqual("There is not enough space on the disk.", failed);
        Assert.IsFalse(told);
        Assert.IsFalse(saving.IsSaving);
        Assert.IsTrue(saving.SaveAsCommand.CanExecute(null), "it can be tried again");
    }

    [TestMethod]
    public async Task TheViewersOwnKeep_IsWhatWritesIt()
    {
        var draft = Draft();
        var target = Path.Combine(_dir, "kept.png");
        (string From, string To)? kept = null;
        var saving = new DraftSaving(draft, null, Picking(target), why => Assert.Fail(why),
                                     (from, to) => { kept = (from, to); return Task.CompletedTask; });

        await saving.SaveAsCommand.ExecuteAsync(null);

        Assert.AreEqual((draft, target), kept);
        Assert.IsFalse(File.Exists(target), "nothing but the viewer's keep writes it");
    }
}
