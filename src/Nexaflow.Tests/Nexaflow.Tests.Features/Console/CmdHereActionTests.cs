using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Console.FileActions;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// "Cmd Here" — the folder action that opens a console rooted at wherever you are.
/// <para>
/// It is offered on a drive root as well as a folder, because opening a shell at <c>D:\</c> is a perfectly
/// ordinary thing to want and a folder action that refuses roots would be missing exactly there. It is offered wherever
/// this disk holds the folder — a drive's, a mount's — and on no location: a WSL distro's folder is its own shell's. Which
/// environment the new tab launches with is decided at open time; this action only has to route the path.
/// </para>
/// </summary>
[TestClass]
[CoversNode("console-cmd-here")]
public class CmdHereActionTests
{
    private readonly IVirtualFileSystem _files = TerminalFiles.With(new FakeTerminal());

    private static (IShellServices Shell, List<Dictionary<string, string>> Opened) Shell()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("Console", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        return (shell, opened);
    }

    [TestMethod]
    public async Task ItOpensAConsoleRootedAtTheFolder()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(await new CmdHereAction(shell, _files).PerformActionAsync(@"C:\work\src"));

        Assert.AreEqual(@"C:\work\src", opened.Single()["path"]);
    }

    [TestMethod]
    public void ItIsOfferedOnRootsAndDrives()
    {
        var action = new CmdHereAction(Substitute.For<IShellServices>(), _files);

        Assert.IsTrue(action.AppliesToRoot);
        Assert.IsTrue(action.AppliesToDrives, "a shell at a drive root is an ordinary thing to want");
        Assert.IsFalse(action.IsDestructive);
    }

    [TestMethod]
    public void ItIsOfferedWhereThisDiskHoldsTheFolder_AndOnNoLocation()
    {
        var action = new CmdHereAction(Substitute.For<IShellServices>(), _files);

        Assert.IsTrue(action.AppliesToFolder(@"C:\work"));
        Assert.IsTrue(action.AppliesToFolder($@"{TerminalFiles.Docs}\notes"), "a mount this disk holds whole");
        Assert.IsFalse(action.AppliesToFolder($@"{TerminalFiles.Distro}\srv"), "a WSL distro's folder is its own shell's, not cmd's");
        Assert.IsFalse(action.AppliesToFolder($@"{TerminalFiles.Nas}\srv"), "a location with no shell, held on no disk");
    }

    [TestMethod]
    public async Task AMultiFolderSelectionOpensOneConsole_NotOnePerFolder()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(await new CmdHereAction(shell, _files).PerformActionAsync([@"C:\a", @"C:\b", @"C:\c"]));

        Assert.AreEqual(1, opened.Count, "a shell has one working directory — three tabs is not the answer");
        Assert.AreEqual(@"C:\a", opened.Single()["path"]);
    }

    [TestMethod]
    public async Task AnEmptySelectionOpensNothing()
    {
        var (shell, opened) = Shell();

        Assert.IsFalse(await new CmdHereAction(shell, _files).PerformActionAsync([]));

        Assert.AreEqual(0, opened.Count);
    }
}
