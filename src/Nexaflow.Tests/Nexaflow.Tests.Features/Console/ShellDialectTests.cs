using System;
using Nexaflow.Tests.Fixtures;
using Nexaflow.IO.Terminal.Shells;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// How a terminal speaks to its shell. cmd: <c>cd /d</c> and <c>set</c> in double quotes, a mounted folder moved to where
/// the disk holds it, and a prompt ending in <c>&gt;</c> known for one. A location's POSIX shell: every value one
/// single-quoted word, so nothing in it is expanded; the file system's paths handed over as the location's own; and the
/// folder its host names named back as the file system names it — names no Windows path holds encoded on the way.
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
    public void Cmd_KnowsABarePrompt_AndWhetherItShowsAFolder()
    {
        Assert.AreEqual(new ShellPromptRow(11, ShowsFolder: true, IsBare: true), Cmd.ReadPrompt(@"C:\Windows>"));
        Assert.IsTrue(Cmd.ReadPrompt(@"PS C:\Windows>")?.ShowsFolder, "PowerShell inside cmd");
        Assert.AreEqual(new ShellPromptRow(0, ShowsFolder: false, IsBare: true), Cmd.ReadPrompt(">"), "a custom prompt shows no folder");
    }

    [TestMethod]
    public void Cmd_ATypedPromptIsNotBare_AndTypingStartsPastIt()
    {
        var prompt = Cmd.ReadPrompt(@"C:\work>git status");

        Assert.AreEqual(new ShellPromptRow(8, ShowsFolder: false, IsBare: false), prompt);
        Assert.IsNull(Cmd.ReadPrompt("Microsoft Windows [Version 10.0]"), "output is no prompt");
    }

    [TestMethod]
    public void Cmd_TakesADrivePathFromItsHost_AndNothingElse()
    {
        Assert.AreEqual(@"C:\work\src", Cmd.ToFolder(@"C:\work\src"));
        Assert.AreEqual(@"D:\", Cmd.ToFolder(@"D:\"));
        Assert.IsNull(Cmd.ToFolder("/home/adam"));
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
    public void Posix_NamesItsHostsFolderAsTheFileSystemDoes()
    {
        Assert.AreEqual($@"{TerminalFiles.Distro}\srv\a%3Ab c", Posix.ToFolder("/srv/a:b c"));
        Assert.AreEqual(TerminalFiles.Distro, Posix.ToFolder("/"));
        Assert.IsNull(Posix.ToFolder("relative/path"));
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

    [TestMethod]
    [DataRow("x%03touch pwned", DisplayName = "Ctrl+C in a folder's name")]
    [DataRow("x%0Dtouch pwned", DisplayName = "Enter in a folder's name")]
    [DataRow("x%1B[A", DisplayName = "an escape in a folder's name")]
    [DataRow("x%7Fy", DisplayName = "a rubout in a folder's name")]
    public void Posix_NeverTypesAControlCharacterFromAName(string segment)
        => Assert.IsNull(Posix.FormatCdCommand($@"{TerminalFiles.Distro}\srv\{segment}"),
                         "quotes hold it from the shell, not from the tty, which acts on it as a key");

    [TestMethod]
    [CoversNode("io-locations-wsl-names")]
    public void Posix_TypesANameThatIsNoUtf8_AsTheShellsOwnPrintfOfIt_AndShowsItsHex()
    {
        var distro = new PosixShellDialect(TerminalFiles.Distro, "Probe", StringComparison.Ordinal);
        var file   = $@"{TerminalFiles.Distro}\home\caf%uDCE9.txt";

        Assert.AreEqual("\"$(printf '%b' '/home/caf\\0351.txt')\"", distro.FormatPathWord(file),
                        "no keystroke types a stray byte; the shell makes it from an escape");
        Assert.AreEqual(@"/home/caf\xE9.txt", distro.ShowPath(file));
        Assert.AreEqual("cd -- \"$(printf '%b' '/home/caf\\0351.txt')\"", distro.FormatCdCommand(file));
    }
}
