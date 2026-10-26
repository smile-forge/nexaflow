using System.Text;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Terminal;

/// <summary>A real shell on this computer through its transport: launched in the folder asked for, it hears what it is
/// sent, answers in raw bytes, takes a new size, and reports its exit code.</summary>
[TestClass]
[CoversNode("ioterm-transport")]
public sealed class PseudoConsoleTransportTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [TestMethod]
    public async Task AShellHearsWhatItIsSent_AnswersInBytes_AndReportsItsExitCode()
    {
        var folder = TestTemp.CreateSubdirectory("nexa-conpty-").FullName;
        using var transport = await new PseudoConsoleLauncher().LaunchAsync(
            new TerminalLaunch(folder, new Dictionary<string, string> { ["NEXA_TRANSPORT"] = "conpty" }, 120, 30), default);

        var output  = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder();
        var heard   = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited  = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Output += chunk =>
        {
            var chars = new char[decoder.GetCharCount(chunk.Span, flush: false)];
            decoder.GetChars(chunk.Span, chars, flush: false);
            lock (output)
            {
                output.Append(chars);
                if (output.ToString().Contains("echoed-conpty")) heard.TrySetResult();
            }
        };
        transport.Exited += code => exited.TrySetResult(code);

        transport.Resize(100, 25);
        transport.Write("echo echoed-%NEXA_TRANSPORT%\r"u8);
            if (await Task.WhenAny(heard.Task, Task.Delay(Patience)) != heard.Task)
                Assert.Fail($"the shell never echoed; it said: {Said(output)}");

            transport.Write("exit 7\r"u8);
            if (await Task.WhenAny(exited.Task, Task.Delay(Patience)) != exited.Task)
                Assert.Fail($"the shell never exited; it said: {Said(output)}");
            Assert.AreEqual(7, exited.Task.Result);
        }

        /// <summary>What the shell wrote so far, its control characters made visible.</summary>
        private static string Said(StringBuilder output)
        {
            lock (output) return output.ToString().Replace("\x1b", "\\e").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
