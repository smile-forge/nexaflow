using System.Text;
using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Interaction;

/// <summary>Files dragged in as content, written out. Their names are another application's, so they are hostile.</summary>
[TestClass]
[CoversNode("interaction-drop-staging")]
public class DropStagingTests
{
    private static CarriedFile Holding(string name, string content = "x")
        => new(name, false, into => into.Write(Encoding.UTF8.GetBytes(content)));

    [TestMethod]
    [DataRow(@"..\..\evil.exe")]
    [DataRow("../../evil.exe")]
    [DataRow(@"C:\Windows\evil.exe")]
    [DataRow(@"\\server\share\evil.exe")]
    [DataRow("..")]
    [DataRow(". .")]
    [DataRow("")]
    [DataRow("a.txt:hidden")]
    [DataRow("CON")]
    [DataRow("nul.txt")]
    [DataRow("COM¹")]
    [DataRow("line\r\nbreak.txt")]
    public void AName_CannotReachOutsideTheDropsOwnFolder(string name)
    {
        var root = TestTemp.CreateSubdirectory("staging-").FullName;

        var saved = new DropStaging(root).Save([Holding(name)]);

        Assert.AreEqual(1, saved.Count);
        var full = Path.GetFullPath(saved[0]);
        Assert.IsTrue(full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), full);
        Assert.IsFalse(full[root.Length..].Contains(':'), "no stream is named");
        Assert.IsFalse(full.Any(char.IsControl), "nothing that would press Enter when typed into a terminal");

        var written = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, written.Length);
    }

    [TestMethod]
    [DataRow("con.txt", "_con.txt")]
    [DataRow("what?.txt", "what_.txt")]
    [DataRow("trailing. ", "trailing")]
    [DataRow("report.pdf", "report.pdf")]
    public void AName_IsKeptAsFarAsItSafelyCanBe(string name, string expected)
        => Assert.AreEqual(expected, DropStaging.SafeParts(name).Single());

    [TestMethod]
    public void AVeryLongName_KeepsItsExtension()
    {
        var safe = DropStaging.SafeParts(new string('a', 400) + ".png").Single();

        Assert.IsTrue(safe.Length <= 120);
        StringAssert.EndsWith(safe, ".png");
    }

    [TestMethod]
    public void AFolder_ComesBackAsTheOneThingDragged_WithWhatIsInIt()
    {
        var root = TestTemp.CreateSubdirectory("staging-").FullName;

        var saved = new DropStaging(root).Save(
            [new CarriedFile("album", true, _ => { }), Holding(@"album\one.txt", "1"), Holding(@"album\two.txt", "2")]);

        Assert.AreEqual("album", Path.GetFileName(saved.Single()));
        Assert.AreEqual("2", File.ReadAllText(Path.Combine(saved[0], "two.txt")));
    }

    [TestMethod]
    public void TwoFilesOfOneName_AreBothKept()
    {
        var root = TestTemp.CreateSubdirectory("staging-").FullName;

        var saved = new DropStaging(root).Save([Holding("a.txt", "first"), Holding("A.txt", "second")]);

        Assert.AreEqual(2, saved.Count);
        Assert.AreEqual("first", File.ReadAllText(saved[0]));
        Assert.AreEqual("second", File.ReadAllText(saved[1]));
    }

    [TestMethod]
    public void WhatIsWritten_IsMarkedAsFromTheInternet()
    {
        var root = TestTemp.CreateSubdirectory("staging-").FullName;

        var saved = new DropStaging(root).Save([Holding("macro.docm")]);

        StringAssert.Contains(File.ReadAllText(saved[0] + ":Zone.Identifier"), "ZoneId=3");
    }

    [TestMethod]
    public void ASourceThatFailsPartWay_LeavesNothing()
    {
        var root = TestTemp.CreateSubdirectory("staging-").FullName;
        var failing = new CarriedFile("b.txt", false, _ => throw new IOException("gone"));

        Assert.ThrowsExactly<IOException>(() => new DropStaging(root).Save([Holding("a.txt"), failing]));

        Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length);
    }
}
