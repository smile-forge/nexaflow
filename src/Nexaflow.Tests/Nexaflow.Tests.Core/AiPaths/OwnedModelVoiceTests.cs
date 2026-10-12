using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Core.Services.Voice;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.AiPaths;

/// <summary>
/// The agent loop on Nexaflow's own model as a voice conversation drives it: written to be heard, its actions announced
/// rather than asked about, and instructions added while it works. What is checked is what the harness owns — the
/// tools that ran, in order, and that the run answered within a bounded number of corrected tool blocks — never the
/// wording.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("voice-session")]
public class OwnedModelVoiceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    /// <summary>Tool blocks the harness may send back before the model writes one it can read.</summary>
    private const int Corrections = 2;

    public TestContext TestContext { get; set; } = null!;

    [ClassCleanup]
    public static Task Stop() => OwnedModelHarness.StopAsync();

    /// <summary>A folder: what it holds is the state the actions change.</summary>
    private sealed class FolderPage : IPageViewModel
    {
        public HashSet<string> Entries { get; } = ["test.zip", "old", "notes.txt"];
        public List<string> Called { get; } = [];

        public string GetContext() => "A file browser on a folder holding: " + string.Join(", ", Entries) + ". Selected: test.zip.";

        public IReadOnlyList<IClientTool> GetClientTools() =>
        [
            Tool("delete_file", "Delete a file or folder in the current folder (to the Recycle Bin).", "name", "The file or folder's name.",
                 name => Entries.Remove(name) ? ToolResult.Ok($"deleted {name}", $"Deleted '{name}'.") : ToolResult.Error("not found", $"No '{name}' here.")),
            Tool("create_folder", "Create a folder in the current folder.", "name", "The new folder's name.",
                 name => Entries.Add(name) ? ToolResult.Ok($"created {name}", $"Created folder '{name}'.") : ToolResult.Error("exists", $"'{name}' already exists.")),
        ];

        private IClientTool Tool(string name, string description, string parameter, string parameterDescription, Func<string, ToolResult> run)
            => new DelegateClientTool(name, description, [new(parameter, parameterDescription)], ToolSafety.RequiresApproval, (args, _) =>
            {
                Called.Add(name);
                return Task.FromResult(run(((string?)args[parameter] ?? "").Trim()));
            });
    }

    /// <summary>The voice reply as the agent loop sees it: each action goes ahead, and an instruction the user adds while
    /// the first one waits reaches the agent at its next step, as a voice reply hands it on.</summary>
    private sealed class Conversation(string? addedWhileTheFirstWaits) : IAIResponseHandler
    {
        private string? _added = addedWhileTheFirstWaits;
        private bool _asked;

        public List<IReadOnlyList<string>> Batches { get; } = [];

        public void BeginResponse(string userText) { }
        public void ReportProgress(string message) { }
        public void ShowFinal(string finalMarkdown) { }
        public void Abort() { }

        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        {
            Batches.Add([.. batch.Select(c => c.Tool)]);
            _asked = true;
            return Task.FromResult(true);
        }

        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => Task.FromResult(true);

        public IReadOnlyList<string> TakeInterjections()
        {
            if (!_asked || _added is not { } added) return [];
            _added = null;
            return [added];
        }
    }

    private async Task<(AiResponse? Response, RecordingModel Model)> TalkAsync(FolderPage page, string said, Conversation conversation,
                                                                             IReadOnlyList<LlmMessage>? history = null)
    {
        var model = new RecordingModel(await OwnedModelHarness.ModelAsync());
        using var cts = new CancellationTokenSource(Limit);
        var options = new AgentRunOptions { History = history ?? [], ReplyStyle = PageVoiceAgent.SpokenStyle };
        var response = await OwnedModelHarness.Service(model).RunAgentAsync(page, said, true, conversation, options, cts.Token);

        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_TRANSCRIPTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var name = string.Concat($"{TestContext.TestName}".Where(char.IsLetterOrDigit).Take(80));
            File.WriteAllText(Path.Combine(folder, name + ".txt"), model.Transcript + "\n=== final\n" + response?.Text);
        }
        return (response, model);
    }

    private static void AnsweredCleanly(AiResponse? response, RecordingModel model, FolderPage page)
    {
        Assert.IsNotNull(response, "the run was cancelled or refused\n" + model.Transcript);
        Assert.IsFalse(response.Text.Contains("_(stopped:"), $"the run stopped at a guard: {response.Text}\n{model.Transcript}");
        Assert.IsTrue(model.MalformedBlocks <= Corrections,
            $"the model needed {model.MalformedBlocks} tries to write a tool block the harness could read\n{model.Transcript}");
        Assert.AreEqual(0, ClientBlockParser.Parse(response.Text, [.. page.GetClientTools().Select(t => t.Name)]).ParseErrors.Count,
            "the answer holds a tool call nobody ran\n" + model.Transcript);
    }

    [TestMethod]
    public async Task AnInstructionAddedWhileAnActionWaits_IsCarriedOutToo_AfterIt()
    {
        var page = new FolderPage();
        var conversation = new Conversation("Also create a folder called archive.");

        var (response, model) = await TalkAsync(page, "Delete the folder named old.", conversation);

        AnsweredCleanly(response, model, page);
        Assert.IsFalse(page.Entries.Contains("old"), "the first instruction\n" + model.Transcript);
        Assert.IsTrue(page.Entries.Contains("archive"), "the one added while it waited\n" + model.Transcript);
        CollectionAssert.AreEqual(new[] { "delete_file", "create_folder" }, page.Called, model.Transcript);
    }

    [TestMethod]
    public async Task AskedAgainAfterAStop_TheActionIsCalled_NotAskedAbout()
    {
        var page = new FolderPage();
        var conversation = new Conversation(addedWhileTheFirstWaits: null);
        LlmMessage[] history =
        [
            new(LlmRole.User, "Delete the selected file."),
            new(LlmRole.Assistant, "Ok. I will delete file test.zip. Aborting delete. The file is unchanged."),
        ];

        var (response, model) = await TalkAsync(page, "Delete the selected file.", conversation, history);

        AnsweredCleanly(response, model, page);
        CollectionAssert.AreEqual(new[] { "delete_file" }, page.Called, "the app announces it: the model calls, it does not ask\n" + model.Transcript);
        Assert.IsFalse(page.Entries.Contains("test.zip"));
    }
}
