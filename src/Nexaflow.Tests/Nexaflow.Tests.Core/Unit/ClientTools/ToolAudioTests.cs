using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

/// <summary>
/// A recording a tool produced, on its way to the conversation model: heard by a model that can hear, transcribed
/// for one that cannot, and never silently lost.
/// </summary>
[TestClass]
[CoversNode("agent-tool-audio")]
public class ToolAudioTests
{
    private static readonly byte[] Clip = WavAudio.Write(new byte[8_000], 8_000);   // half a second of silence at 8 kHz

    private sealed class ListenTool : IClientTool
    {
        public string Name => "listen_to_track";
        public string Description => "hear part of the track";
        public IReadOnlyList<ClientToolParameter> Parameters => [];
        public ToolSafety Safety => ToolSafety.SafeOperation;
        public bool Parallelizable => false;
        public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
            => Task.FromResult(ToolResult.Ok("listened", "recording attached") with { Audio = [new ContextAudio(Clip, "the track")] });
    }

    private sealed class Page(IClientTool tool) : IPageViewModel
    {
        public string GetContext() => "An audio player.";
        public IReadOnlyList<IClientTool> GetClientTools() => [tool];
    }

    /// <summary>Calls the tool, then answers; records what each turn carried.</summary>
    private sealed class Model(bool hears) : ILlmProvider
    {
        private int _turn;
        public List<(string Text, IReadOnlyList<LlmAttachment>? Attachments)> Turns { get; } = [];
        public string Name => "model";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public bool SupportsAudio => hears;

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var last = messages.Last(m => m.Role == LlmRole.User);
            if (last.Text.Contains("Most relevant tool numbers")) { yield return new LlmTextDelta("1"); yield break; }
            Turns.Add((last.Text, last.Attachments));
            await Task.Yield();
            yield return new LlmTextDelta(_turn++ == 0 ? "```client_tool\n{\"tool\":\"listen_to_track\",\"arguments\":{}}\n```" : "It is quiet.");
        }

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["m"]);
    }

    /// <summary>A speech model that hears the same words in anything, and records how much audio it was given.</summary>
    private sealed class Transcriber : ISpeechToTextProvider
    {
        public int BytesHeard;
        public string Name => "speech";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["s"]);
        public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default)
            => Task.FromResult<ISpeechToTextSession>(new Session(this));

        private sealed class Session(Transcriber owner) : ISpeechToTextSession
        {
            private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ValueTask AppendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default) { owner.BytesHeard += pcm.Length; return ValueTask.CompletedTask; }
            public ValueTask CompleteAsync(CancellationToken ct = default) { _done.TrySetResult(); return ValueTask.CompletedTask; }
            public async IAsyncEnumerable<SpeechToTextEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct = default)
            {
                await _done.Task;
                yield return new TranscriptFinal("hello from the track");
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Approver : IAIResponseHandler
    {
        public void BeginResponse(string userText) { }
        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => Task.FromResult(true);
        public void ReportProgress(string message) { }
        public void ShowFinal(string finalMarkdown) { }
        public void Abort() { }
    }

    private static AIService Service(Model model, Transcriber? transcriber)
    {
        var ws  = new WorkspaceRuntime();
        var svc = new AIService(ws, Path.GetTempPath());
        typeof(WorkspaceRuntime).GetProperty(nameof(WorkspaceRuntime.AiService))!.GetSetMethod(nonPublic: true)!.Invoke(ws, [svc]);
        typeof(WorkspaceRuntime).GetProperty(nameof(WorkspaceRuntime.ShellServices))!.GetSetMethod(nonPublic: true)!.Invoke(ws, [new ShellServices(ws)]);
        svc.Register("c1", model);
        var config = new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "c1", ProviderName = "model", Model = "m" }],
            Assignments = { ["Conversation"] = AbilityChain.Of("c1"), ["Disambiguation"] = AbilityChain.Of("c1") },
        };
        if (transcriber is not null)
        {
            svc.Register("s1", transcriber);
            config.Columns.Add(new ProviderModelPair { Id = "s1", ProviderName = "speech", Model = "s", Kind = ModelKind.SpeechToText });
            config.Assignments["SpeechIn"] = AbilityChain.Of("s1");
        }
        svc.LoadAbilityConfig(config);
        return svc;
    }

    [TestMethod]
    public async Task AModelThatHears_GetsTheRecording()
    {
        var model = new Model(hears: true);

        await Service(model, new Transcriber()).RunAgentAsync(new Page(new ListenTool()), "what is playing?", true, new Approver(), default);

        var heard = model.Turns[1].Attachments!.Single();
        Assert.IsTrue(heard.IsAudio);
        CollectionAssert.AreEqual(Clip, heard.Bytes);
    }

    [TestMethod]
    public async Task AModelThatCannotHear_GetsWhatTheSpeechModelHeard()
    {
        var model = new Model(hears: false);
        var transcriber = new Transcriber();

        await Service(model, transcriber).RunAgentAsync(new Page(new ListenTool()), "what is playing?", true, new Approver(), default);

        Assert.IsNull(model.Turns[1].Attachments);
        StringAssert.Contains(model.Turns[1].Text, "hello from the track");
        Assert.AreEqual(16_000, transcriber.BytesHeard, 8, "half a second at the sessions' 16 kHz, made from the 8 kHz clip");
    }

    [TestMethod]
    public async Task WithNoSpeechModel_TheModelIsToldTheRecordingCouldNotBePassedOn()
    {
        var model = new Model(hears: false);

        await Service(model, transcriber: null).RunAgentAsync(new Page(new ListenTool()), "what is playing?", true, new Approver(), default);

        StringAssert.Contains(model.Turns[1].Text, "can't hear audio");
    }
}
