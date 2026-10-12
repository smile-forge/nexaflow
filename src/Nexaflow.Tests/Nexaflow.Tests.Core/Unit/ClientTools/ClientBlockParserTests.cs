using System.Linq;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

[TestClass]
[CoversNode("chat-interface")]
public class ClientBlockParserTests
{
    [TestMethod]
    public void SingleToolCall_ParsesNameAndArgs()
    {
        var raw = "I'll create the file.\n\n```client_tool\n{\"tool\":\"create_text_file\",\"arguments\":{\"name\":\"a.txt\",\"content\":\"hi\"}}\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.AreEqual(1, turn.ToolCalls.Count);
        Assert.AreEqual("create_text_file", turn.ToolCalls[0].Tool);
        Assert.AreEqual("a.txt", turn.ToolCalls[0].Arguments["name"]!.GetValue<string>());
        Assert.AreEqual("hi",    turn.ToolCalls[0].Arguments["content"]!.GetValue<string>());
        StringAssert.Contains(turn.ExplanationMarkdown, "I'll create the file.");
        Assert.IsFalse(turn.ExplanationMarkdown.Contains("client_tool"), "fence must be stripped from explanation");
    }

    [TestMethod]
    public void MultipleToolCalls_FormOneBatch()
    {
        var raw = "Creating both.\n```client_tool\n{\"tool\":\"create_text_file\",\"arguments\":{\"name\":\"a.txt\"}}\n```\n```client_tool\n{\"tool\":\"create_text_file\",\"arguments\":{\"name\":\"b.txt\"}}\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.AreEqual(2, turn.ToolCalls.Count);
        CollectionAssert.AreEquivalent(
            new[] { "a.txt", "b.txt" },
            turn.ToolCalls.Select(c => c.Arguments["name"]!.GetValue<string>()).ToArray());
    }

    [TestMethod]
    public void MultiLineContent_IsPreserved()
    {
        var raw = "```client_tool\n{\"tool\":\"create_text_file\",\"arguments\":{\"name\":\"a.txt\",\"content\":\"line1\\nline2\\nline3\"}}\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.AreEqual("line1\nline2\nline3", turn.ToolCalls[0].Arguments["content"]!.GetValue<string>());
    }

    [TestMethod]
    public void ArgsAlias_IsAccepted()
    {
        var raw = "```client_tool\n{\"tool\":\"x\",\"args\":{\"k\":\"v\"}}\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.AreEqual(1, turn.ToolCalls.Count);
        Assert.AreEqual("v", turn.ToolCalls[0].Arguments["k"]!.GetValue<string>());
    }

    [TestMethod]
    public void ForeignFences_StayInExplanation_NoToolCalls()
    {
        var raw = "Here is a diagram:\n```mermaid\nflowchart TD; A-->B\n```\nand code:\n```csharp\nvar x = 1;\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.AreEqual(0, turn.ToolCalls.Count);
        Assert.IsNull(turn.Plan);
        Assert.IsNull(turn.Prefill);
        StringAssert.Contains(turn.ExplanationMarkdown, "```mermaid");
        StringAssert.Contains(turn.ExplanationMarkdown, "flowchart TD; A-->B");
        StringAssert.Contains(turn.ExplanationMarkdown, "var x = 1;");
    }

    [TestMethod]
    public void Plan_ParsesTitleMermaidAndSteps()
    {
        var raw = "```client_plan\n{\"title\":\"Do it\",\"mermaid\":\"flowchart TD; A-->B\",\"steps\":[{\"title\":\"Read\",\"tool\":\"get_file_contents\"},{\"title\":\"Decide\",\"decision\":true}]}\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.IsNotNull(turn.Plan);
        Assert.AreEqual("Do it", turn.Plan!.Title);
        Assert.AreEqual("flowchart TD; A-->B", turn.Plan.Mermaid);
        Assert.AreEqual(2, turn.Plan.Steps.Count);
        Assert.AreEqual("get_file_contents", turn.Plan.Steps[0].Tool);
        Assert.IsFalse(turn.Plan.Steps[0].IsDecisionPoint);
        Assert.IsTrue(turn.Plan.Steps[1].IsDecisionPoint);
    }

    [TestMethod]
    public void Prefill_ParsesText()
    {
        var raw = "Try this:\n```client_prefill\n{\"text\":\"rename *.jpeg to *.jpg\"}\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.AreEqual("rename *.jpeg to *.jpg", turn.Prefill);
        Assert.AreEqual(0, turn.ToolCalls.Count);
    }

    [TestMethod]
    public void MalformedJson_IsReportedNotThrown()
    {
        var raw = "```client_tool\n{ this is not json }\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.AreEqual(0, turn.ToolCalls.Count);
        Assert.IsTrue(turn.ParseErrors.Count > 0);
    }

    [TestMethod]
    public void MissingToolName_IsReported()
    {
        var raw = "```client_tool\n{\"arguments\":{\"name\":\"a.txt\"}}\n```";
        var turn = ClientBlockParser.Parse(raw);

        Assert.AreEqual(0, turn.ToolCalls.Count);
        Assert.IsTrue(turn.ParseErrors.Count > 0);
    }

    [TestMethod]
    public void PlainText_IsAllExplanation_NoActions()
    {
        var turn = ClientBlockParser.Parse("Just a normal answer with **markdown**.");

        Assert.IsFalse(turn.HasActions);
        Assert.AreEqual("Just a normal answer with **markdown**.", turn.ExplanationMarkdown);
    }

    [TestMethod]
    public void EmptyInput_YieldsEmptyTurn()
    {
        var turn = ClientBlockParser.Parse("");

        Assert.IsFalse(turn.HasActions);
        Assert.AreEqual(string.Empty, turn.ExplanationMarkdown);
    }

    private static readonly string[] Tools = ["add_note", "get_weather"];

    [TestMethod]
    [DataRow("call:add_note{text:buy milk}")]
    [DataRow("add_note(text=\"buy milk\")")]
    [DataRow("`get_weather{\"city\": \"Lyon\"}`")]
    public void AToolCalledAsText_IsAMalformedBlock_SoTheModelIsAskedAgain(string reply)
    {
        var turn = ClientBlockParser.Parse(reply, Tools);

        Assert.AreEqual(0, turn.ToolCalls.Count);
        Assert.AreEqual(1, turn.ParseErrors.Count);
        StringAssert.Contains(turn.ParseErrors[0], "client_tool");
    }

    [TestMethod]
    [DataRow("I can add_note for you if you like.")]
    [DataRow("Use `add_note` to keep it.")]
    [DataRow("print(add_note)")]
    public void AToolOnlyMentioned_IsProse(string reply)
        => Assert.AreEqual(0, ClientBlockParser.Parse(reply, Tools).ParseErrors.Count);

    [TestMethod]
    public void BesideARealBlock_ATextCallIsLeftAlone()
    {
        var turn = ClientBlockParser.Parse("add_note(x)\n```client_tool\n{\"tool\":\"get_weather\",\"arguments\":{}}\n```", Tools);

        Assert.AreEqual(1, turn.ToolCalls.Count);
        Assert.AreEqual(0, turn.ParseErrors.Count);
    }

    [TestMethod]
    [DataRow("I will add it now.\n\ncall:client_tool\n{\"tool\": \"add_note\", \"arguments\": {\"text\": \"buy milk\"}}")]
    [DataRow("client_tool\n{\"tool\": \"add_note\",\n \"arguments\": {\"text\": \"buy milk\"}}")]
    [DataRow("{\"tool\": \"add_note\", \"arguments\": {\"text\": \"buy milk\"}}")]
    [DataRow("call:client_tool{\"tool\": \"add_note\", \"arguments\": {\"text\": \"buy milk\"}}")]
    public void ACallBodyWithoutItsFence_IsTheCallItPlainlyIs(string reply)
    {
        var turn = ClientBlockParser.Parse(reply, Tools);

        Assert.AreEqual(0, turn.ParseErrors.Count);
        var call = turn.ToolCalls.Single();
        Assert.AreEqual(("add_note", "buy milk"), (call.Tool, (string?)call.Arguments["text"]));
    }

    [TestMethod]
    public void JsonThatCallsNoKnownTool_IsProse()
    {
        var turn = ClientBlockParser.Parse("{\"tool\": \"format_disk\", \"arguments\": {}}", Tools);

        Assert.AreEqual(0, turn.ToolCalls.Count);
        StringAssert.Contains(turn.ExplanationMarkdown, "format_disk");
    }

    [TestMethod]
    public void WithoutToolNames_AnUnfencedBodyIsProse_AsBefore()
        => Assert.AreEqual(0, ClientBlockParser.Parse("{\"tool\": \"add_note\", \"arguments\": {}}").ToolCalls.Count);
}
