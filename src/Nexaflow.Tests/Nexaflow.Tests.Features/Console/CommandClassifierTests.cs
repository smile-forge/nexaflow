using System.Collections.Generic;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Tests.Fixtures;
using System;
using Nexaflow.IO.Terminal.Shells;
using Nexaflow.IO.Terminal;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// What the AI bar takes a line for over a console. The corpus is the specification: a sentence is never run as a
/// command, a command carrying a secret is never shown to a model, and what reads either way is left to one.
/// </summary>
[TestClass]
[CoversNode("vterm-classifier")]
[CoversNode("console-routing-group")]
public class CommandClassifierTests
{
    // A shell's programs, fixed, so no verdict depends on the machine.
    private static readonly HashSet<string> Programs = new(CommandClassifier.PosixBuiltins, StringComparer.Ordinal)
    {
        "apt", "git", "make", "npm", "docker", "mysql", "systemctl", "curl", "yes", "go", "python3", "ssh", "dir", "del",
    };

    private static ConsoleLineVerdict Judged(string line) => CommandClassifier.JudgeLine(line, Programs.Contains);

    [TestMethod]
    [DataRow("ls")]
    [DataRow("ls -la")]
    [DataRow("cd ..")]
    [DataRow("cd src")]
    [DataRow("git status")]
    [DataRow("git checkout main")]
    [DataRow("git log --since yesterday")]
    [DataRow("git commit -m 'fix the bug in the parser'")]
    [DataRow("apt update")]
    [DataRow("sudo apt install nginx -y")]
    [DataRow("systemctl restart nginx")]
    [DataRow("make")]
    [DataRow("npm run build")]
    [DataRow("docker compose up")]
    [DataRow("rm -rf build")]
    [DataRow("rm notes.txt")]
    [DataRow("find . -name core")]
    [DataRow("ps aux | grep node")]
    [DataRow("python3 manage.py migrate")]
    [DataRow("echo hello")]
    [DataRow("dir /s")]
    [DataRow("./build.sh")]
    [DataRow("../tools/run.sh now")]
    [DataRow("/usr/bin/env python3")]
    [DataRow("~/bin/deploy staging")]
    [DataRow("$HOME/bin/deploy")]
    [DataRow(@"C:\tools\x.exe /quiet")]
    [DataRow("build.bat")]
    public void WhatHasACommandsShape_IsACommand(string line)
        => Assert.AreEqual(ConsoleLineVerdict.Command, Judged(line), line);

    [TestMethod]
    [DataRow("mysql -u root -pHunter2", DisplayName = "a password in a flag")]
    [DataRow("notinstalledyet --token abc123", DisplayName = "a token given to a program not listed")]
    [DataRow("API_KEY=abc123 ./deploy", DisplayName = "a key set for the command")]
    [DataRow("export TOKEN=abc123", DisplayName = "a token exported")]
    [DataRow("curl -H 'Authorization: Bearer abc' https://example.test", DisplayName = "a header holding one")]
    public void ACommandCarryingASecret_NeverNeedsAModel(string line)
        => Assert.AreEqual(ConsoleLineVerdict.Command, Judged(line), line);

    [TestMethod]
    [DataRow("what is in this folder")]
    [DataRow("what's using port 3000")]
    [DataRow("why did that build fail")]
    [DataRow("explain what just happened")]
    [DataRow("explain ls -la")]
    [DataRow("summarise the errors in this log")]
    [DataRow("show me the largest files")]
    [DataRow("can you restart nginx")]
    [DataRow("list running services")]
    [DataRow("delete the temp files")]
    [DataRow("qwzzx the temp folder")]
    [DataRow("compress these logs into one archive")]
    public void ASentenceOpeningWithNoCommand_IsARequest(string line)
        => Assert.AreEqual(ConsoleLineVerdict.Request, Judged(line), line);

    [TestMethod]
    [DataRow("rm the temp files", DisplayName = "would delete files named for the words")]
    [DataRow("rm temp files", DisplayName = "the same, with no article")]
    [DataRow("kill the process on port 3000")]
    [DataRow("kill node processes")]
    [DataRow("find the big files")]
    [DataRow("find large files")]
    [DataRow("make install")]
    [DataRow("make the build faster")]
    [DataRow("sort these by size")]
    [DataRow("touch base")]
    [DataRow("go to my home folder")]
    [DataRow("git add everything and commit")]
    [DataRow("apt install nginx curl")]
    [DataRow("which python is this?")]
    [DataRow("deploy staging")]
    [DataRow("gti status")]
    [DataRow("yes", DisplayName = "an answer, and a program that never ends")]
    [DataRow("thanks")]
    [DataRow("compare old.txt > new.txt", DisplayName = "an unknown word and a redirect")]
    [DataRow("ls\nrm -rf build", DisplayName = "several pasted lines")]
    public void WhatReadsEitherWay_IsUnclear_SoItRunsOnlyIfAModelSaysSo(string line)
        => Assert.AreEqual(ConsoleLineVerdict.Unclear, Judged(line), line);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("ls\u0003rm", DisplayName = "Ctrl+C inside")]
    [DataRow("ls\u001b[A", DisplayName = "an escape inside")]
    public void NothingAndKeys_AreNeverTypedIntoTheShell(string line)
        => Assert.AreEqual(ConsoleLineVerdict.Request, Judged(line), line);

    [TestMethod]
    public void AShareIsNeverLookedUp_WhileTheLineIsTyped()
        => CommandClassifier.JudgeLine(@"\\someone\share\tool.exe",
                                       name => throw new AssertFailedException($"'{name}' was looked up"));

    [TestMethod]
    public void CmdKnowsItsBuiltinsInAnyCase_AndThePath()
    {
        var cmd  = new CmdShellDialect();
        var none = new HashSet<string>();

        Assert.IsTrue(cmd.KnowsCommand("EXIT", none));
        Assert.IsTrue(cmd.KnowsCommand("whoami", none), "a program on PATH");
        Assert.IsFalse(cmd.KnowsCommand("qwzzx-no-such-program", none));
    }

    [TestMethod]
    public void ALocationsShellKnowsItsOwnPrograms_NotWindows()
    {
        var posix = new PosixShellDialect("::wsl-probe", "Probe", StringComparison.Ordinal);

        Assert.IsTrue(posix.KnowsCommand("systemctl", new HashSet<string> { "systemctl" }));
        Assert.IsFalse(posix.KnowsCommand("notepad", new HashSet<string>()), "this computer's PATH says nothing about a distro");
        Assert.IsFalse(posix.KnowsCommand("LS", new HashSet<string>()), "its names differ by case");
    }
}
