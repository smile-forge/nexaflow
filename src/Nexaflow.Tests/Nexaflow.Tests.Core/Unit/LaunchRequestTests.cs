using System.IO;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Explorer's "Open in Nexaflow": what a launch's arguments ask for (<see cref="LaunchRequest"/>), the path as Explorer
/// actually hands it over, and which workspace the item opens in (<see cref="WorkspaceManager.ChooseWorkspaceForOpenedPath"/>).
/// </summary>
[TestClass]
[CoversNode("ws-open-path")]
public class LaunchRequestTests
{
    [TestMethod]
    public void TheWorkspaceAndTheItem_AreReadFromTheirSwitches_InAnyOrderAndCase()
    {
        var launch = LaunchRequest.Parse(["--OPEN", @"C:\Work\notes.md", "--context", "Dev"]);

        Assert.AreEqual("Dev", launch.WorkspaceName);
        Assert.AreEqual(@"C:\Work\notes.md", launch.OpenPath);
    }

    [TestMethod]
    public void ALaunchWithNeither_AsksForNeither()
    {
        var launch = LaunchRequest.Parse(["--prestart"]);

        Assert.IsNull(launch.WorkspaceName);
        Assert.IsNull(launch.OpenPath);
    }

    [TestMethod]
    public void ASwitchWithNoValueAfterIt_IsIgnored()
        => Assert.IsNull(LaunchRequest.Parse(["--open"]).OpenPath);

    [TestMethod]
    public void ARelativePath_IsMadeAbsolute_AgainstTheLaunchingProcess()
        => Assert.AreEqual(Path.GetFullPath("notes.md"), LaunchRequest.AbsolutePath("notes.md"));

    [TestMethod]
    public void ADriveRoot_WhoseBackslashEscapedTheClosingQuote_IsTheRoot()
    {
        // "C:\" from Explorer's %1 reaches the process as C:" — read as C: it would be the current directory on C.
        Assert.AreEqual(@"C:\", LaunchRequest.AbsolutePath("C:\""));
        Assert.AreEqual(@"D:\", LaunchRequest.AbsolutePath("D:"));
    }

    [TestMethod]
    public void ABlankOrMalformedPath_IsNoPath()
    {
        Assert.IsNull(LaunchRequest.AbsolutePath("  "));
        Assert.IsNull(LaunchRequest.AbsolutePath("\""));
        Assert.IsNull(LaunchRequest.AbsolutePath("C:\\a\0b"));
    }

    [TestMethod]
    public void WithNoWorkspaceOpen_TheItemGoesToTheFirst()
    {
        Workspace first = new() { Name = "Default" }, dev = new() { Name = "Dev" };

        Assert.AreSame(first, WorkspaceManager.ChooseWorkspaceForOpenedPath([first, dev], []));
    }

    [TestMethod]
    public void WithOneWorkspaceOpen_TheItemGoesThere_HoweverManyWindowsItHas()
    {
        Workspace first = new() { Name = "Default" }, dev = new() { Name = "Dev" };

        Assert.AreSame(dev, WorkspaceManager.ChooseWorkspaceForOpenedPath([first, dev], [dev, dev]));
    }

    [TestMethod]
    public void WithSeveralWorkspacesOpen_TheItemGoesToTheFirst()
    {
        Workspace first = new() { Name = "Default" }, dev = new() { Name = "Dev" }, docs = new() { Name = "Docs" };

        Assert.AreSame(first, WorkspaceManager.ChooseWorkspaceForOpenedPath([first, dev, docs], [dev, docs]));
    }
}
