using System;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The file system a console opens on, without one behind it: a WSL-like location with a shell
/// (<see cref="Distro"/>, shown as <c>wsl://Probe/</c>, names compared exactly) whose shell is the <see cref="FakeTerminal"/>
/// given; a location with no shell that this disk does not hold (<see cref="Nas"/>); a mount this disk holds whole
/// (<see cref="Docs"/>, at <c>D:\docs</c>); and this disk.
/// </summary>
internal static class TerminalFiles
{
    public const string Distro = "::wsl-probe";
    public const string Nas    = "::sftp-nas";
    public const string Docs   = "::docs";
    public const string DocsOnDisk = @"D:\docs";

    public static IVirtualFileSystem With(FakeTerminal distroShell)
    {
        var files = Substitute.For<IVirtualFileSystem>();
        files.GetCapabilities(Arg.Any<string>()).Returns(ci => Capabilities(ci.Arg<string>()));
        files.GetNameComparison(Arg.Any<string>()).Returns(ci => LocationPath.Comparison(Capabilities(ci.Arg<string>())));
        files.GetBacking(Arg.Any<string>()).Returns(ci => Backing(ci.Arg<string>()));
        files.TryResolveReal(Arg.Any<string>()).Returns(ci => Real(ci.Arg<string>()));
        files.GetBreadcrumbs(Arg.Is<string>(p => On(p, Distro))).Returns([("Probe", Distro)]);
        files.GetDisplayName(Distro).Returns("Probe");
        files.GetDisplayPath(Distro).Returns("wsl://Probe/");
        files.GetTerminalLauncherAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(ci => Launcher(ci.Arg<string>(), distroShell));
        return files;
    }

    private static bool On(string path, string root)
        => path.Equals(root, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase);

    private static LocationCapabilities Capabilities(string path)
        => On(path, Distro) ? LocationCapabilities.Shell | LocationCapabilities.CaseSensitive | LocationCapabilities.SameMachine
         : On(path, Nas)    ? LocationCapabilities.RandomRead | LocationCapabilities.CaseSensitive
         :                    LocationCapabilities.RandomRead | LocationCapabilities.AtomicRename;

    private static VirtualBacking Backing(string path)
        => On(path, Distro) || On(path, Nas) ? VirtualBacking.Remote
         : On(path, Docs)                    ? VirtualBacking.PassThrough
         :                                     VirtualBacking.Real;

    private static string? Real(string path)
        => On(path, Docs)                                           ? DocsOnDisk + path[Docs.Length..]
         : path.StartsWith(VirtualMount.Prefix, StringComparison.Ordinal) ? null
         :                                                            path;

    private static ValueTask<ITerminalLauncher?> Launcher(string path, FakeTerminal distroShell)
        => On(path, Distro) ? ValueTask.FromResult<ITerminalLauncher?>(distroShell)
         : On(path, Nas)    ? ValueTask.FromException<ITerminalLauncher?>(new NotSupportedException("'NAS' has no shell."))
         :                    ValueTask.FromResult<ITerminalLauncher?>(null);
}
