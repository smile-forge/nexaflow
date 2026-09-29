using System;
using System.IO;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>What a save picker is told about a destination before it offers to write to it.</summary>
[TestClass]
[CoversNode("win-cux-file-picker")]
public class SaveRoomTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-saveroom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_root)) new FileInfo(file).IsReadOnly = false;
            Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    private static SaveRoom Room(long bytes, long? free, long? replacing) =>
        new("C:\\x", bytes, free, replacing, null, SaveTrouble.None);

    [TestMethod]
    public void AnEmptyFolderTakesAWrite_AndAskingLeavesNothingBehindInIt()
    {
        var room = SaveRoom.For(Path.Combine(_root, "new.txt"), 1024);

        Assert.AreEqual(SaveTrouble.None, room.Trouble);
        Assert.IsTrue(room.Allowed);
        Assert.IsFalse(room.Replaces, "nothing is there yet");
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length,
                        "the write probe is taken back out — a picker must not litter the folder a reader is browsing");
    }

    [TestMethod]
    public void AFileAlreadyThereComesBackWithItsSizeAndWhenItWasLastWritten()
    {
        var path = Path.Combine(_root, "already.txt");
        File.WriteAllText(path, new string('x', 300));
        var written = File.GetLastWriteTime(path);

        var room = SaveRoom.For(path, 1024);

        Assert.IsTrue(room.Replaces);
        Assert.AreEqual(300, room.Replacing);
        Assert.AreEqual(written, room.LastWritten);
        Assert.AreEqual(SaveTrouble.None, room.Trouble, "replacing a file of your own is allowed");
    }

    [TestMethod]
    public void ReplacingHandsTheOldFilesSpaceBack_SoOverwritingAsksTheVolumeForOnlyTheDifference()
    {
        // 150 MB over a 100 MB file on a volume with 60 MB left: the write needs 50, and it has room.
        Assert.IsTrue(Room(bytes: 150_000_000, free: 60_000_000, replacing: 100_000_000).Fits);

        // The same write where nothing is there to reclaim does not fit, and says by how much.
        var fresh = Room(bytes: 150_000_000, free: 60_000_000, replacing: null);
        Assert.IsFalse(fresh.Fits);
        Assert.AreEqual(90_000_000, fresh.Shortfall);
    }

    [TestMethod]
    public void AVolumeThatWouldNotSayHowMuchItHasLeftNeverBlocksASave()
    {
        var unknown = Room(bytes: long.MaxValue, free: null, replacing: null);

        Assert.IsTrue(unknown.Fits, "a figure we could not read is not a refusal");
        Assert.AreEqual(0, unknown.Shortfall);
        Assert.IsTrue(unknown.Allowed);
    }

    [TestMethod]
    public void ASizeNobodyStatedFitsAnywhere()
    {
        // Registry export runs reg.exe and cannot know the size ahead of the write; it must still be offered a target.
        Assert.IsTrue(Room(bytes: 0, free: 0, replacing: null).Fits);
    }

    [TestMethod]
    public void ARealVolumeSaysWhatItHasLeft()
    {
        var room = SaveRoom.For(Path.Combine(_root, "new.txt"));

        Assert.IsNotNull(room.Free);
        Assert.IsTrue(room.Free > 0, $"a writable temp volume with {room.Free} bytes free");
    }

    [TestMethod]
    public void AFolderThatIsNotThereIsTroubleBeforeAnyByteIsWritten()
    {
        var room = SaveRoom.For(Path.Combine(_root, "nope", "deeper", "file.txt"), 10);

        Assert.AreEqual(SaveTrouble.NoFolder, room.Trouble);
        Assert.IsFalse(room.Allowed);
    }

    [TestMethod]
    public void NothingTypedIsNotATarget()
    {
        Assert.AreEqual(SaveTrouble.NoName, SaveRoom.For("").Trouble);
        Assert.AreEqual(SaveTrouble.NoName, SaveRoom.For("   ").Trouble);
        Assert.AreEqual(SaveTrouble.NoName, SaveRoom.For(null).Trouble);
        Assert.AreEqual(SaveTrouble.NoName, SaveRoom.For(_root + Path.DirectorySeparatorChar).Trouble,
                        "a folder is not a file to write");
    }

    [TestMethod]
    public void CharactersWindowsWillNotPutInAFileNameAreCaughtBeforeTheWrite()
    {
        // GetFullPath stopped refusing these on .NET Core, so a picker that leans on it alone offers a Save
        // button that throws when pressed.
        Assert.AreEqual(SaveTrouble.NotAPath, SaveRoom.For(Path.Combine(_root, "a<b>c.txt")).Trouble);
        Assert.AreEqual(SaveTrouble.NotAPath, SaveRoom.For(Path.Combine(_root, "pipe|d.txt")).Trouble);
        Assert.AreEqual(SaveTrouble.None,     SaveRoom.For(Path.Combine(_root, "ordinary name.txt")).Trouble);
    }

    [TestMethod]
    public void AFolderCanBeAskedOnItsOwnWhetherItTakesASave()
    {
        Assert.IsTrue(SaveRoom.TakesASave(_root));
        Assert.IsFalse(SaveRoom.TakesASave(Path.Combine(_root, "not-there")));
        Assert.IsFalse(SaveRoom.TakesASave(null));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length, "and asking leaves the folder as it was");
    }

    [TestMethod]
    public void AReadOnlyFileIsTroubleRatherThanAnOfferToReplaceIt()
    {
        var path = Path.Combine(_root, "locked-down.txt");
        File.WriteAllText(path, "x");
        new FileInfo(path).IsReadOnly = true;

        var room = SaveRoom.For(path, 10);

        Assert.AreEqual(SaveTrouble.FileReadOnly, room.Trouble);
        Assert.IsFalse(room.Allowed);
        Assert.IsTrue(room.Replaces, "it is still reported as what is there");
    }

    [TestMethod]
    public void AFileAnotherProgramIsWritingIsReportedAsInUse()
    {
        var path = Path.Combine(_root, "busy.txt");
        File.WriteAllText(path, "x");

        using var holder = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        Assert.AreEqual(SaveTrouble.FileInUse, SaveRoom.For(path, 10).Trouble);
    }

    [TestMethod]
    public void AFileMerelyBeingReadElsewhereIsStillSaveable()
    {
        // The opposite mistake: a reader holding the file must not read as a lock, or Save As refuses a file
        // the very tab offering to save it has open.
        var path = Path.Combine(_root, "being-read.txt");
        File.WriteAllText(path, "x");

        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        Assert.AreEqual(SaveTrouble.None, SaveRoom.For(path, 10).Trouble);
    }
}
