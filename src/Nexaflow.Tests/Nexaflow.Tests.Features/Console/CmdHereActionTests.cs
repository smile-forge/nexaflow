using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Console.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// "Cmd Here" — the folder action that opens a console rooted at wherever you are.
/// <para>
/// It is offered on a drive root as well as a folder, because opening a shell at <c>D:\</c> is a perfectly
/// ordinary thing to want and a folder action that refuses roots would be missing exactly there. Which
/// environment the new tab launches with is decided at open time by the launch picker; this action only
/// has to route the path.
/// </para>
/// </summary>
[TestClass]
[CoversNode("console-cmd-here")]
public class CmdHereActionTests
{
    private static (IShellServices Shell, List<Dictionary<string, string>> Opened) Shell()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("Console", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        return (shell, opened);
    }

    [TestMethod]
    public void ItOpensAConsoleRootedAtTheFolder()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(new CmdHereAction(shell).PerformAction(@"C:\work\src"));

        Assert.AreEqual(@"C:\work\src", opened.Single()["path"]);
    }

    [TestMethod]
    public void ItIsOfferedOnRootsAndDrives()
    {
        var action = new CmdHereAction(Substitute.For<IShellServices>());

        Assert.IsTrue(action.AppliesToRoot);
        Assert.IsTrue(action.AppliesToDrives, "a shell at a drive root is an ordinary thing to want");
        Assert.IsFalse(action.IsDestructive);
    }

    [TestMethod]
    public void AMultiFolderSelectionOpensOneConsole_NotOnePerFolder()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(new CmdHereAction(shell).PerformAction([@"C:\a", @"C:\b", @"C:\c"]));

        Assert.AreEqual(1, opened.Count, "a shell has one working directory — three tabs is not the answer");
        Assert.AreEqual(@"C:\a", opened.Single()["path"]);
    }

    [TestMethod]
    public void AnEmptySelectionOpensNothing()
    {
        var (shell, opened) = Shell();

        Assert.IsFalse(new CmdHereAction(shell).PerformAction([]));

        Assert.AreEqual(0, opened.Count);
    }
}
