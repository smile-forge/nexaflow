using System.Collections.Generic;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.Harness;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Tests.Providers.Unit;

[TestClass]
public class GemmaHarnessTests
{
    private static readonly IReadOnlyList<IServerTool> Tools = [new CalculatorServerTool()];
    private static GemmaHarness New() => new();

    [TestMethod]
    public void Parse_ToolCall_NameAndStringArg()
    {
        var r = New().Parse("<|tool_call>call:calculator{expression:<|\"|>2+2<|\"|>}<tool_call|>");

        Assert.IsNotNull(r.ToolCall);
        Assert.AreEqual("calculator", r.ToolCall!.Name);
        Assert.AreEqual("2+2", r.ToolCall.Arguments["expression"]);
    }

    [TestMethod]
    public void Parse_ToolCall_CommaInsideStringIsKept()
    {
        var r = New().Parse("<|tool_call>call:echo{text:<|\"|>a, b, c<|\"|>}<tool_call|>");

        Assert.IsNotNull(r.ToolCall);
        Assert.AreEqual("a, b, c", r.ToolCall!.Arguments["text"]);
    }

    [TestMethod]
    public void Parse_ToolCall_NumericAndBoolArgs()
    {
        var r = New().Parse("<|tool_call>call:t{n:42,flag:true}<tool_call|>");

        Assert.AreEqual(42L,  r.ToolCall!.Arguments["n"]);
        Assert.AreEqual(true, r.ToolCall.Arguments["flag"]);
    }

    [TestMethod]
    public void Parse_ExtractsThought_AndVisibleText()
    {
        var r = New().Parse("<|channel>thought\nLet me think<channel|>The answer.");

        StringAssert.Contains(r.Thought, "Let me think");
        Assert.AreEqual("The answer.", r.VisibleText);
        Assert.IsNull(r.ToolCall);
    }

    [TestMethod]
    public void Parse_TruncatesAtTurnEnd()
    {
        var r = New().Parse("Hello there<turn|><|turn>user\nleftover");
        Assert.AreEqual("Hello there", r.VisibleText);
    }

    [TestMethod]
    public void Format_ServerSystemFirst_ClientSystemDemoted()
    {
        var msgs = new List<LlmMessage>
        {
            new(LlmRole.System, "You are Aria. Use ```client_tool blocks."),
            new(LlmRole.User,   "hi"),
        };

        var prompt = New().Format(msgs, new HarnessOptions(false), Tools);

        StringAssert.Contains(prompt, "<|turn>system");
        StringAssert.Contains(prompt, "model server");                 // server system prompt
        StringAssert.Contains(prompt, "declaration:calculator");       // tool declaration
        StringAssert.Contains(prompt, "## Instructions from Nexaflow"); // client system demoted to user turn
        StringAssert.Contains(prompt, "You are Aria.");                // ...preserved verbatim
        StringAssert.Contains(prompt, "hi");
        StringAssert.Contains(prompt, "<|turn>model\n");               // open model turn at the end
    }

    [TestMethod]
    public void RenderToolRound_EmitsCallAndResponse()
    {
        var call = new ServerToolCall { Name = "calculator", Arguments = { ["expression"] = "6*7" } };
        var frag = New().RenderToolRound(call, thought: null, resultText: "6*7 = 42");

        StringAssert.Contains(frag, "<|tool_call>call:calculator");
        StringAssert.Contains(frag, "<|tool_response>response:calculator");
        StringAssert.Contains(frag, "42");
    }

    [TestMethod]
    public void Format_Parse_RoundTrip_AssistantHistoryPreserved()
    {
        var msgs = new List<LlmMessage>
        {
            new(LlmRole.System,    "sys"),
            new(LlmRole.User,      "q1"),
            new(LlmRole.Assistant, "a1"),
            new(LlmRole.User,      "q2"),
        };

        var prompt = New().Format(msgs, new HarnessOptions(false), Tools);
        StringAssert.Contains(prompt, "a1");
        StringAssert.Contains(prompt, "q2");
    }
}
