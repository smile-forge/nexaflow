using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.Harness;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Tests.Providers.Unit;

[TestClass]
public class ServerToolLoopTests
{
    private static readonly List<LlmMessage> Msgs =
        [new(LlmRole.System, "sys"), new(LlmRole.User, "what is 6*7?")];

    [TestMethod]
    public async Task RunsServerTool_FeedsResultBack_ReturnsFinalText()
    {
        var harness  = new GemmaHarness();
        var registry = ServerToolRegistry.Build(["calculator"]);
        var prompts  = new List<string>();
        int round    = 0;

        ServerToolLoop.InferFunc infer = (prompt, _, _) =>
        {
            prompts.Add(prompt);
            round++;
            return Task.FromResult(round == 1
                ? "<|tool_call>call:calculator{expression:<|\"|>6*7<|\"|>}<tool_call|>"
                : "The answer is 42.");
        };

        var text = await ServerToolLoop.RunAsync(
            harness, Msgs, new HarnessOptions(false), registry, infer, maxRounds: 10, CancellationToken.None);

        Assert.AreEqual("The answer is 42.", text);
        Assert.AreEqual(2, round);
        StringAssert.Contains(prompts[1], "= 42");   // calculator's result was fed back into the next prompt
    }

    [TestMethod]
    public async Task NoToolCall_ReturnsImmediately()
    {
        var harness  = new GemmaHarness();
        var registry = ServerToolRegistry.Build(["calculator"]);
        int round    = 0;

        ServerToolLoop.InferFunc infer = (_, _, _) => { round++; return Task.FromResult("Just answering."); };

        var text = await ServerToolLoop.RunAsync(
            harness, Msgs, new HarnessOptions(false), registry, infer, 10, CancellationToken.None);

        Assert.AreEqual("Just answering.", text);
        Assert.AreEqual(1, round);
    }

    [TestMethod]
    public async Task ClientToolFence_PassesThroughUntouched()
    {
        // A client_tool fence is NOT a native server tool call — the inner loop must leave it intact
        // for Nexaflow's outer client loop.
        var harness  = new GemmaHarness();
        var registry = ServerToolRegistry.Build(["calculator"]);
        const string reply = "Sure.\n```client_tool\n{\"tool\":\"open_file\",\"arguments\":{}}\n```";

        ServerToolLoop.InferFunc infer = (_, _, _) => Task.FromResult(reply);

        var text = await ServerToolLoop.RunAsync(
            harness, Msgs, new HarnessOptions(false), registry, infer, 10, CancellationToken.None);

        StringAssert.Contains(text, "client_tool");
        StringAssert.Contains(text, "open_file");
    }
}
