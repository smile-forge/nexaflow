using System;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal.Shells;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// How a terminal speaks to its shell. cmd: <c>cd /d</c> and <c>set</c> in double quotes, a mounted folder moved to where
/// the disk holds it, and the folder read off a prompt ending in <c>&gt;</c>. A location's POSIX shell: every value one
/// single-quoted word, so nothing in it is expanded; the file system's paths handed over as the location's own; and the
/// folder it reports named back as the file system names it — names no Windows path holds encoded on the way.
/// </summary>
[TestClass]
[CoversNode("vterm-shell-dialects")]
public sealed class ShellDialectTests
{
    private static readonly CmdShellDialect Cmd = new(TerminalFiles.With(new FakeTerminal()));

    private static readonly PosixShellDialect Posix = PosixShellDialect.ForLocation(
        TerminalFiles.With(new FakeTerminal()), $@"{TerminalFiles.Distro}\home\adam");

    // ── cmd ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Cmd_ChangesDriveAndFolder_AndSetsAVariableQuoted()
    {
        Assert.AreEqual("cd /d \"C:\\my work\"", Cmd.FormatCdCommand(@"C:\my work"));
        Assert.AreEqual("set \"NODE_ENV=dev mode\"", Cmd.FormatSetCommand("NODE_ENV", "dev mode"));
    }

    [TestMethod]
    public void Cmd_MovesToAMountedFolderWhereTheDiskHoldsIt_AndNowhereNoDiskHolds()
    {
        Assert.AreEqual($"cd /d \"{TerminalFiles.DocsOnDisk}\\notes\"", Cmd.FormatCdCommand($@"{TerminalFiles.Docs}\notes"));
        Assert.IsNull(Cmd.FormatCdCommand($@"{TerminalFiles.Nas}\srv"), "cmd reaches no folder this disk does not hold");
    }

    [TestMethod]
    public void Cmd_ReadsTheFolderOffABarePrompt()
    {
        Assert.AreEqual(new ShellPromptRow(11, @"C:\Windows", IsBare: true), Cmd.ReadPrompt(@"C:\Windows>"));
        Assert.AreEqual(@"C:\Windows", Cmd.ReadPrompt(@"PS C:\Windows>")?.Folder, "PowerShell inside cmd");
        Assert.AreEqual(new ShellPromptRow(0, null, IsBare: true), Cmd.ReadPrompt(">"), "a custom prompt shows no folder");
    }

    [TestMethod]
    public void Cmd_ATypedPromptIsNotBare_AndTypingStartsPastIt()
    {
        var prompt = Cmd.ReadPrompt(@"C:\work>git status");

        Assert.AreEqual(new ShellPromptRow(8, null, IsBare: false), prompt);
        Assert.IsNull(Cmd.ReadPrompt("Microsoft Windows [Version 10.0]"), "output is no prompt");
    }

    [TestMethod]
    public void Cmd_FollowsAReportedDrivePath_AndNothingElse()
    {
        Assert.AreEqual(@"C:\work\src", Cmd.ToFolder(new TerminalWorkingFolder("pc", "/C:/work/src")));
        Assert.AreEqual(@"D:\", Cmd.ToFolder(new TerminalWorkingFolder("pc", "/D:")));
        Assert.IsNull(Cmd.ToFolder(new TerminalWorkingFolder("probe", "/home/adam")));
    }

    // ── a location's POSIX shell ─────────────────────────────────────────────

    [TestMethod]
    public void Posix_QuotesEveryWordWhole_AQuoteClosedEscapedAndReopened()
    {
        Assert.AreEqual("'plain'", PosixShellDialect.Quote("plain"));
        Assert.AreEqual(@"'it'\''s $HOME `x`'", PosixShellDialect.Quote("it's $HOME `x`"));
        Assert.AreEqual(@"export GREETING='it'\''s here'", Posix.FormatSetCommand("GREETING", "it's here"));
    }

    [TestMethod]
    public void Posix_ChangesToTheLocationsOwnPath()
    {
        Assert.AreEqual("cd -- '/srv/my files'", Posix.FormatCdCommand($@"{TerminalFiles.Distro}\srv\my files"));
        Assert.AreEqual("cd -- '/srv/a:b'", Posix.FormatCdCommand($@"{TerminalFiles.Distro}\srv\a%3Ab"), "an encoded name is the server's own");
        Assert.AreEqual("cd -- '/'", Posix.FormatCdCommand(TerminalFiles.Distro));
        Assert.AreEqual(@"cd -- '/home/o'\''brien'", Posix.FormatCdCommand($@"{TerminalFiles.Distro}\home\o'brien"));
    }

    [TestMethod]
    public void Posix_ReachesNoFolderOffItsLocation()
    {
        Assert.IsNull(Posix.FormatCdCommand(@"C:\work"));
        Assert.IsNull(Posix.FormatCdCommand($@"{TerminalFiles.Nas}\srv"));
        Assert.IsNull(Posix.FormatCdCommand($@"{TerminalFiles.Distro}-other\srv"), "a root is a whole segment");
    }

    [TestMethod]
    public void Posix_NamesAReportedFolderAsTheFileSystemDoes()
    {
        Assert.AreEqual($@"{TerminalFiles.Distro}\srv\a%3Ab c", Posix.ToFolder(new TerminalWorkingFolder("probe", "/srv/a:b c")));
        Assert.AreEqual(TerminalFiles.Distro, Posix.ToFolder(new TerminalWorkingFolder("probe", "/")));
        Assert.IsNull(Posix.ToFolder(new TerminalWorkingFolder("probe", "relative/path")));
    }

    [TestMethod]
    public void Posix_TellsTheAssistantTheLocation_AndBothNamesOfTheFolder()
    {
        StringAssert.Contains(Posix.Description, "Probe (wsl://Probe/)");
        Assert.AreEqual($@"/home/adam ({TerminalFiles.Distro}\home\adam)", Posix.DescribeFolder($@"{TerminalFiles.Distro}\home\adam"));
        Assert.AreEqual(StringComparison.Ordinal, Posix.PathComparison, "the location's names differ by case");
        Assert.IsTrue(Posix.Builtins.Contains("export"));
        Assert.IsNull(Posix.ReadPrompt("adam@probe:~$"), "its prompts are followed by their marks, never read");
    }
}
