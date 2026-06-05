using System.Collections.Generic;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.Harness;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Tests.Providers.Unit;

[TestClass]
public class QwenHarnessTests
{
    private static readonly IReadOnlyList<IServerTool> Tools = [new CalculatorServerTool()];
    private static QwenHarness New() => new();

    [TestMethod]
    public void Parse_HermesToolCall()
    {
        var r = New().Parse("<tool_call>\n{\"name\":\"calculator\",\"arguments\":{\"expression\":\"2+2\"}}\n</tool_call>");

        Assert.IsNotNull(r.ToolCall);
        Assert.AreEqual("calculator", r.ToolCall!.Name);
        Assert.AreEqual("2+2", r.ToolCall.Arguments["expression"]);
    }

    [TestMethod]
    public void Parse_ExtractsThink_AndVisibleText()
    {
        var r = New().Parse("<think>reasoning here</think>The answer.");

        StringAssert.Contains(r.Thought, "reasoning here");
        Assert.AreEqual("The answer.", r.VisibleText);
    }

    [TestMethod]
    public void Parse_TruncatesAtImEnd()
    {
        var r = New().Parse("Hello<|im_end|><|im_start|>user\nleftover");
        Assert.AreEqual("Hello", r.VisibleText);
    }

    [TestMethod]
    public void Format_ChatMl_ServerSystemFirst_ClientDemoted()
    {
        var msgs = new List<LlmMessage>
        {
            new(LlmRole.System, "You are Aria."),
            new(LlmRole.User,   "hi"),
        };

        var prompt = New().Format(msgs, new HarnessOptions(false), Tools);

        StringAssert.Contains(prompt, "<|im_start|>system");
        StringAssert.Contains(prompt, "<tools>");                       // Hermes tool block
        StringAssert.Contains(prompt, "calculator");
        StringAssert.Contains(prompt, "## Instructions from Nexaflow"); // client system demoted to user turn
        StringAssert.Contains(prompt, "You are Aria.");
        Assert.IsTrue(prompt.TrimEnd().EndsWith("<|im_start|>assistant"), "should end with an open assistant turn");
    }

    [TestMethod]
    public void RenderToolRound_ClosesAndReopensAssistant()
    {
        var call = new ServerToolCall { Name = "calculator", Arguments = { ["expression"] = "6*7" } };
        var frag = New().RenderToolRound(call, thought: null, resultText: "6*7 = 42");

        StringAssert.Contains(frag, "<tool_call>");
        StringAssert.Contains(frag, "calculator");
        StringAssert.Contains(frag, "<tool_response>");
        StringAssert.Contains(frag, "42");
        StringAssert.Contains(frag, "<|im_start|>assistant");           // reopened for continuation
    }
}
