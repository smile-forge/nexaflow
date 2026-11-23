using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Wsl;
using Nexaflow.Features.Wsl.FileActions;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Features.Console;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Wsl;

/// <summary>
/// "Shell Here" opens a WSL distro's own shell at the folder — offered on a distro the runtime has mounted with a shell, its
/// row in This PC among them, and nowhere else: this computer's folders are the Console's, a server's another feature's.
/// </summary>
[TestClass]
[CoversNode("wslshell-shell-here")]
public sealed class ShellHereActionTests
{
    private sealed class Locations(params MountedLocation[] mounted) : ILocationDirectory
    {
        public IReadOnlyList<MountedLocation> Mounted { get; } = mounted;
        public IReadOnlyList<UnreachableLocation> Unreachable { get; } = [];
        public event Action? Changed { add { } remove { } }
    }

    private readonly IVirtualFileSystem _files = TerminalFiles.With(new FakeTerminal());

    private static MountedLocation Distro(string root)
        => new(root[2..], "Probe", "wsl", root, LocationCapabilities.Shell | LocationCapabilities.SameMachine, "wsl://Probe/");

    private ShellHereAction Action(IShellServices shell, params MountedLocation[] mounted) => new(shell, new Locations(mounted), _files);

    [TestMethod]
    public void ItIsOfferedOnADistrosFolder_AndItsRoot_AndNowhereElse()
    {
        var action = Action(Substitute.For<IShellServices>(), Distro(TerminalFiles.Distro));

        Assert.IsTrue(action.AppliesToFolder($@"{TerminalFiles.Distro}\home\adam"));
        Assert.IsTrue(action.AppliesToFolder(TerminalFiles.Distro), "the distro's row in This PC");
        Assert.IsTrue(action.AppliesToDrives);
        Assert.IsFalse(action.AppliesToFolder(@"C:\work"), "this computer's folder is the Console's");
        Assert.IsFalse(action.AppliesToFolder($@"{TerminalFiles.Nas}\srv"), "a server is not a distro");
        Assert.IsFalse(Action(Substitute.For<IShellServices>()).AppliesToFolder($@"{TerminalFiles.Distro}\home"),
                       "a distro the runtime has not mounted");
        Assert.IsFalse(action.AppliesToFolder($@"{TerminalFiles.Distro}-other\home"), "a name that only starts the same");
    }

    [TestMethod]
    public async Task ItOpensTheDistrosShell_AtTheFolder()
    {
        var shell  = Substitute.For<IShellServices>();
        var folder = $@"{TerminalFiles.Distro}\home\adam";

        Assert.IsTrue(await Action(shell, Distro(TerminalFiles.Distro)).PerformActionAsync([folder, $@"{TerminalFiles.Distro}\srv"]));

        shell.Received(1).OpenTab(WslShellTabRegistration.StaticPageKind,
            Arg.Is<Dictionary<string, string>>(p => p["path"] == folder));
    }

    [TestMethod]
    public void ItsTab_IsNamedAfterTheDistro_AndKeepsTheFolder()
    {
        var page = new WslShellTabRegistration(Substitute.For<IShellServices>(), Substitute.For<IAIService>(), _files)
            .CreatePageDefinition(new() { ["path"] = $@"{TerminalFiles.Distro}\home" });

        Assert.AreEqual("Probe", page.Title);
        Assert.AreEqual($@"{TerminalFiles.Distro}\home", page.PageParams!["path"]);
    }
}
