using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// Drafts kept in the app's own folder and cleared by the app, never by Windows: never one a tab shows or will open again, one
/// opened kept as in use, and those long untouched let go; and what is kept held under its budget, longest unused first.
/// </summary>
[TestClass]
[CoversNode("made-file-drafts")]
public class DraftLocationTests
{
    private string _root = "";

    [TestInitialize]
    public void Make() => _root = TestTemp.CreateSubdirectory("nexaflow-drafts-").FullName;

    [TestCleanup]
    public void Clean()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>A draft made <paramref name="daysAgo"/> days ago and not used since.</summary>
    private string Draft(string name, int daysAgo)
    {
        var path = DraftLocation.NewFile(name, _root);
        File.WriteAllText(path, name);
        Aged(path, Path.GetDirectoryName(path)!, daysAgo);
        return path;
    }

    /// <summary>A kept folder of <paramref name="bytes"/> bytes, last used <paramref name="daysAgo"/> days ago.</summary>
    private string Kept(string name, int daysAgo, int bytes)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var file = Path.Combine(folder, "kept.bin");
        File.WriteAllBytes(file, new byte[bytes]);
        Aged(file, folder, daysAgo);
        return folder;
    }

    private static void Aged(string file, string folder, int daysAgo)
    {
        var when = DateTime.UtcNow.AddDays(-daysAgo);
        File.SetLastWriteTimeUtc(file, when);
        Directory.SetLastWriteTimeUtc(folder, when);
    }

    [TestMethod]
    public void Drafts_AreInTheAppsOwnFolder()
        => Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "drafts"), DraftLocation.Root,
                           "the app's own, not the temp folder, whose clean-up takes the files of a draft still open");

    [TestMethod]
    public void ADraftNoTabShows_UntouchedTooLong_IsLetGo_AndEveryOtherIsKept()
    {
        var forgotten = Draft("forgotten.png", 45);
        var shown = Draft("shown.md", 45);
        var recent = Draft("recent.png", 3);

        DraftLocation.Sweep([shown, @"C:\elsewhere\notes.txt"], _root);

        Assert.IsFalse(File.Exists(forgotten), "untouched for longer than drafts are kept, and nothing shows it");
        Assert.IsTrue(File.Exists(shown), "a tab shows it, or would open it again, however old");
        Assert.IsTrue(File.Exists(recent));
    }

    [TestMethod]
    public void ADraftOpened_IsMarkedUsed_SoItIsKept()
    {
        var reopened = Draft("canvas.ora", 45);

        DraftLocation.Touch(reopened, _root);
        DraftLocation.Sweep([], _root);

        Assert.IsTrue(File.Exists(reopened));
    }

    [TestMethod]
    public void OnlyWhatIsInADraftsOwnFolder_IsADraft()
    {
        Assert.IsTrue(DraftLocation.Holds(Path.Combine(_root, "1a2b3c", "picture.png"), _root));
        Assert.IsTrue(DraftLocation.Holds(Path.Combine(_root, "1a2b3c", "scan.figures", "figure-1.png"), _root), "and what is beside it");
        Assert.IsFalse(DraftLocation.Holds(Path.Combine(_root, "picture.png"), _root), "the drafts folder itself holds no draft");
        Assert.IsFalse(DraftLocation.Holds(_root + "-other" + Path.DirectorySeparatorChar + "1a2b3c" + Path.DirectorySeparatorChar + "x.png", _root),
                       "a folder named like it is not it");
        Assert.IsFalse(DraftLocation.Holds(@"C:\Users\someone\picture.png", _root));
    }

    [TestMethod]
    public void WhatATabNames_IsEachValue_AndEachOfSeveralJoined()
        => CollectionAssert.AreEqual(new[] { @"C:\a.wav", @"C:\b.wav", "0" },
                                     DraftLocation.PathsIn(new Dictionary<string, string> { ["paths"] = @"C:\a.wav|C:\b.wav", ["index"] = "0" }).ToArray());

    [TestMethod]
    public void WhatIsKeptPastItsBudget_IsLetGo_LongestUnusedFirst_NeverWhatIsToBeKept()
    {
        var oldest = Kept("oldest", 30, 100);
        var older = Kept("older", 20, 100);
        var newer = Kept("newer", 1, 100);

        CacheLocation.Bound(_root, 250, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(oldest) });

        Assert.IsTrue(Directory.Exists(oldest), "kept, however long unused");
        Assert.IsFalse(Directory.Exists(older), "the longest unused of the rest goes first");
        Assert.IsTrue(Directory.Exists(newer), "and no more than it takes to come under the budget");
    }

    [TestMethod]
    public void AFolderWrittenIn_IsUsed_ThoughNothingTouchedIt()
    {
        var folder = Kept("written", 30, 10);
        File.SetLastWriteTimeUtc(Path.Combine(folder, "kept.bin"), DateTime.UtcNow);

        Assert.IsTrue(CacheLocation.LastUsed(folder) > DateTime.UtcNow.AddDays(-1));
    }

    [TestMethod]
    public void ASessionsWorkingCopies_AreItsOwn_AndWhatAnEndedSessionLeftIsLetGo()
    {
        var kind = Directory.CreateDirectory(Path.Combine(_root, "fonts")).FullName;
        var ended = Directory.CreateDirectory(Path.Combine(kind, $"{int.MaxValue}-0f1e2d3c")).FullName;
        File.WriteAllText(Path.Combine(ended, "left.ttf"), "left behind");
        var running = Directory.CreateDirectory(Path.Combine(kind, $"{Environment.ProcessId}-another")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(kind, "not-a-session")).FullName;

        var mine = CacheLocation.SessionFolder("fonts", _root);
        CacheLocation.LetGoOfEndedSessions(kind);   // what SessionFolder sets going in the background, waited for here

        Assert.IsTrue(Directory.Exists(mine));
        Assert.AreEqual(kind, Path.GetDirectoryName(mine), "a folder of this session's own, among that kind's");
    Assert.IsFalse(Directory.Exists(ended), "no process runs with its id: a session that ended without cleaning up");
        Assert.IsTrue(Directory.Exists(running), "a process runs with its id: never taken from under it");
        Assert.IsTrue(Directory.Exists(other), "a folder not named as a session is none of its business");
        Assert.AreEqual(mine, CacheLocation.SessionFolder("fonts", _root), "asked again, the same folder");
    }
}
