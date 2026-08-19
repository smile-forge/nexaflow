using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hex.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Hex;

/// <summary>
/// "As Hex" — the action that opens anything in the hex editor.
/// <para>
/// It owns the <c>/binary</c> experience, which is the file map's catch-all: an executable, a firmware
/// image, a file with no extension at all. That makes it the last thing standing between the user and a
/// file nothing else can open, so it must be offered for a single file and must not pretend to handle a
/// multi-selection — a hex editor has one buffer.
/// </para>
/// </summary>
[TestClass]
[CoversNode("hex-open-actions")]
public class HexOpenActionTests
{
    private static (IShellServices Shell, List<Dictionary<string, string>> Opened) Shell()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("Hex", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        return (shell, opened);
    }

    [TestMethod]
    public void ItOpensTheFileItWasInvokedOn()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(new ShowBinaryAction(shell).PerformAction(@"C:\fw\image.bin"));

        Assert.AreEqual(@"C:\fw\image.bin", opened.Single()["path"]);
    }

    [TestMethod]
    public void ItOwnsTheBinaryCatchAllExperience()
    {
        var action = new ShowBinaryAction(Substitute.For<IShellServices>());

        Assert.AreEqual("/binary", action.ExperienceId,
                        "this is what the file map falls back to when nothing more specific claims a file");
        Assert.IsTrue(action.OpensViewer);
        Assert.IsFalse(action.IsDestructive, "opening a file to look at its bytes changes nothing");
    }

    [TestMethod]
    public void ASelectionOpensTheFirstFileOnly_BecauseAHexEditorHasOneBuffer()
    {
        var (shell, opened) = Shell();

        Assert.IsFalse(new ShowBinaryAction(shell).SupportsMultipleFiles);
        Assert.IsTrue(new ShowBinaryAction(shell).PerformAction([@"C:\a.bin", @"C:\b.bin"]));

        Assert.AreEqual(1, opened.Count);
        Assert.AreEqual(@"C:\a.bin", opened.Single()["path"]);
    }

    [TestMethod]
    public void AnEmptySelectionOpensNothing()
    {
        var (shell, opened) = Shell();

        Assert.IsFalse(new ShowBinaryAction(shell).PerformAction([]));

        Assert.AreEqual(0, opened.Count);
    }
}
