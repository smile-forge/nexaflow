using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ChatHandlers;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Core.AiPaths;

/// <summary>
/// The model the AI bar asks about a line that reads either way over a console, for real. A command is chosen to run, and
/// words asking for something are left to the assistant — above all the ones that would do damage run as typed.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("console-routing-group")]
public sealed class ConsoleRoutingModelTests
{
    private const string Posix = "Terminal: '::wsl-ubuntu\\home\\adam', running a POSIX shell on Ubuntu (wsl://Ubuntu/) " +
                                 "(sh / bash syntax such as `ls`, `cd`, `export` — not cmd or PowerShell).";

    private const string Cmd = "Terminal: 'C:\\work', running the Windows cmd.exe command prompt (use cmd / batch syntax " +
                               "such as `dir`, not PowerShell cmdlets).";

    [ClassCleanup]
    public static Task Stop() => OwnedModelHarness.StopAsync();

    [TestMethod]
    [DataRow(Posix, "make install")]
    [DataRow(Posix, "make test")]
    [DataRow(Posix, "apt install nginx curl")]
    [DataRow(Posix, "git merge origin main")]
    [DataRow(Posix, "which python")]
    [DataRow(Posix, "touch README")]
    [DataRow(Posix, "ll")]
    [DataRow(Posix, "htop")]
    [DataRow(Posix, "find src")]
    [DataRow(Posix, "echo hello to the world")]
    [DataRow(Posix, "for f in *.log; do gzip $f; done")]
    [DataRow(Cmd, "where python")]
    [DataRow(Cmd, "type readme")]
    [DataRow(Cmd, "net start spooler")]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task ACommand_IsChosenToRun(string shell, string line)
        => Assert.IsNotNull(await ChosenAsync(shell, line), $"'{line}' should run");

    [TestMethod]
    [DataRow(Posix, "rm the temp files")]
    [DataRow(Posix, "rm temp files please")]
    [DataRow(Posix, "kill the process on port 3000")]
    [DataRow(Posix, "kill node processes")]
    [DataRow(Posix, "find the big files")]
    [DataRow(Posix, "find large files")]
    [DataRow(Posix, "sort these by size")]
    [DataRow(Posix, "make the build faster")]
    [DataRow(Posix, "git add everything and commit")]
    [DataRow(Posix, "touch base")]
    [DataRow(Posix, "restart nginx")]
    [DataRow(Posix, "clean up docker")]
    [DataRow(Posix, "yes")]
    [DataRow(Posix, "thanks")]
    [DataRow(Posix, "hello")]
    [DataRow(Cmd, "del the temp files")]
    [DataRow(Cmd, "start the dev server")]
    [DataRow(Cmd, "format the code")]
    [DataRow(Cmd, "delete old logs")]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task WordsAskingForSomething_AreLeftToTheAssistant(string shell, string line)
        => Assert.IsNull(await ChosenAsync(shell, line), $"'{line}' must not run as typed");

    private static async Task<IQueryHandler?> ChosenAsync(string shell, string line)
    {
        var service = OwnedModelHarness.Service(await OwnedModelHarness.ModelAsync());
        var page    = Substitute.For<IPageViewModel>();
        page.GetContext().Returns(shell);

        return await service.DisambiguateToolSelection(page, line, [(new ConsoleQueryHandler(), 0.5f)]);
    }
}
