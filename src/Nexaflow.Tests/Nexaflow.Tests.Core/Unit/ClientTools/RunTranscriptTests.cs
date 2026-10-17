using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>A run's transcript: what the model was sent and wrote, whole — its calls and their answers, the tools
/// offered and the instructions each time they were chosen again.</summary>
[TestClass]
[CoversNode("chat-interface")]
public class RunTranscriptTests
{
    private string _folder = "";
    private string? _was;

    [TestInitialize]
    public void Point()
    {
        _folder = TestTemp.CreateSubdirectory("nexaflow-transcripts-").FullName;
        _was    = Environment.GetEnvironmentVariable(RunTranscript.Variable);
        Environment.SetEnvironmentVariable(RunTranscript.Variable, _folder);
    }

    [TestCleanup]
    public void Restore()
    {
        Environment.SetEnvironmentVariable(RunTranscript.Variable, _was);
        Directory.Delete(_folder, recursive: true);
    }

    private static IClientTool Tool(string name) => new DelegateClientTool(name, name, [], ToolSafety.SafeOperation, (_, _) => Task.FromResult(ToolResult.Ok("")));

    [TestMethod]
    public void ACallAndItsAnswer_AreWritten_AndTheToolsAndInstructions_WhenChosenAgain()
    {
        var call = new LlmToolCall("c1", "delete_file", new JsonObject { ["name"] = "café" });
        var transcript = RunTranscript.Begin("TranscriptOfACall")!;

        List<LlmMessage> sent = [new(LlmRole.System, "first"), new(LlmRole.User, "Delete café.")];
        transcript.Step(sent, [Tool("delete_file")], "");
        sent[0] = new(LlmRole.System, "second");
        sent.Add(new(LlmRole.Assistant, "") { ToolCalls = [call] });
        sent.Add(new(LlmRole.Tool, "Deleted.") { Answers = call });
        transcript.Step(sent, [Tool("create_directory")], "Done.");

        var written = File.ReadAllText(Directory.GetFiles(_folder, "*TranscriptOfACall*").Single());
        StringAssert.Contains(written, "[call delete_file c1 {\"name\":\"café\"}]");
        StringAssert.Contains(written, ">>> Tool, answering delete_file c1\nDeleted.");
        StringAssert.Contains(written, ">>> tools offered\ncreate_directory");
        StringAssert.Contains(written, ">>> System, written again\nsecond");
    }
}
