using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Nexaflow.Core.Services;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Voice;

/// <summary>
/// A spoken conversation, from the microphone opening to it closing: held in the bar it takes its turns under a name of its own,
/// which the workspace's models let go of once it ends; held by a page it takes the page's. However it ends, its devices are let go
/// of and it says it ended once; one that never started is let go of without saying so. The workspace's assistant says whether one
/// can be held at all, and when that may have changed.
/// </summary>
[TestClass]
[CoversNode("voice-session")]
public class SpokenConversationTests
{
    private sealed class Devices : ISpeechDevices
    {
        public readonly FakeCapture Capture = new();
        public readonly FakePlayback Playback = new();
        public (ISpeechCapture Capture, ISpeechPlayback Playback) Open() => (Capture, Playback);
    }

    /// <summary>A language model that only records the conversations it was told to forget.</summary>
    private sealed class Model : ILlmProvider
    {
        public readonly List<string> Forgotten = [];
        public string Name => "Claude";
        public DataResidency Residency => DataResidency.LeavesMachine;
        public bool IsAvailable => true;
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default) => AsyncEnumerable.Empty<LlmStreamEvent>();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["m"]);

        public Task ForgetConversationAsync(string conversation, CancellationToken ct = default)
        {
            Forgotten.Add(conversation);
            return Task.CompletedTask;
        }
    }

    /// <summary>A workspace's assistant that listens, speaks and answers — or, without <paramref name="speaks"/>, only listens and answers.</summary>
    private static (AIService Service, WorkspaceRuntime Workspace, Model Model) Assistant(ISpeechDevices devices, bool speaks = true)
    {
        var ws  = TestRuntimes.FactoryWith(tasks: null, notify: _ => { }, speech: devices).CreateUnstarted(TestRuntimes.Saved(new Workspace()));
        var svc = ws.AiService;
        var model = new Model();
        svc.Register("llm", model);
        svc.Register("stt", new FakeListener());
        var config = new AiConfig
        {
            Columns =
            [
                new ProviderModelPair { Id = "llm", ProviderName = "Claude", Model = "m" },
                new ProviderModelPair { Id = "stt", ProviderName = "listener", Model = "l", Kind = ModelKind.SpeechToText },
            ],
        };
        config.Assignments["GeneralIntelligence"] = AbilityChain.Of("llm");
        config.Assignments["SpeechIn"]            = AbilityChain.Of("stt");
        if (speaks)
        {
            svc.Register("tts", new FakeSpeaker());
            config.Columns.Add(new ProviderModelPair { Id = "tts", ProviderName = "speaker", Model = "s", Kind = ModelKind.TextToSpeech });
            config.Assignments["SpeechOut"] = AbilityChain.Of("tts");
        }
        svc.LoadAbilityConfig(config);
        return (svc, ws, model);
    }

    private static SpokenConversation Conversation(AIService service, bool heldByPage)
        => new(service, new WpfUiThread(Dispatcher.CurrentDispatcher), () => null, () => false, new ShownConversation(), new FakeConfirmations(), heldByPage);

    [TestMethod]
    public void HeldInTheBar_ItHasANameOfItsOwn_WhichTheModelsForgetOnceItEnds() => OnUi(async () =>
    {
        var devices = new Devices();
        var (svc, _, model) = Assistant(devices);
        var conversation = Conversation(svc, heldByPage: false);
        var ended = new List<string?>();
        conversation.Ended += (_, why) => ended.Add(why);

        await conversation.StartAsync();
        var name = conversation.ConversationName;
        await conversation.StopAsync();
        await Dispatcher.Yield(DispatcherPriority.Background);   // whatever the session posted as it ended

        StringAssert.StartsWith(name, "voice-");
        CollectionAssert.AreEqual(new[] { name }, model.Forgotten);
        CollectionAssert.AreEqual(new string?[] { null }, ended, "stopped, it says it ended, once");
        Assert.IsTrue(devices.Capture.Disposed);
    });

    [TestMethod]
    public void HeldByAPage_ItsTurnsAreThePages_AndNothingIsForgotten() => OnUi(async () =>
    {
        var (svc, _, model) = Assistant(new Devices());
        var conversation = Conversation(svc, heldByPage: true);

        await conversation.StartAsync();
        await conversation.StopAsync();

        Assert.IsNull(conversation.ConversationName);
        Assert.AreEqual(0, model.Forgotten.Count, "the page holds the conversation, and forgets it when it closes");
    });

    [TestMethod]
    public void OneThatNeverStarted_IsLetGo_WithoutSayingItEnded() => OnUi(async () =>
    {
        var devices = new Devices();
        var (svc, _, _) = Assistant(devices);
        var conversation = Conversation(svc, heldByPage: false);
        var ended = 0;
        conversation.Ended += (_, _) => ended++;

        await conversation.DisposeAsync();

        Assert.IsTrue(devices.Capture.Disposed);
        Assert.AreEqual(0, ended);
    });

    [TestMethod]
    public void TheMic_IsOfferedOnlyWhereSomethingListens_AVoiceSpeaks_AndAModelAnswers()
    {
        Assert.IsTrue(Assistant(new Devices()).Service.CanHoldSpokenConversation);
        Assert.IsFalse(Assistant(new Devices(), speaks: false).Service.CanHoldSpokenConversation, "no voice to answer with");
    }

    [TestMethod]
    public void AChangeToTheWorkspacesModels_SaysTheMicMayHaveChanged()
    {
        var (svc, ws, _) = Assistant(new Devices());
        var told = 0;
        EventHandler onChanged = (_, _) => told++;
        svc.ReadinessChanged += onChanged;

        ws.RaiseProvidersChanged();
        svc.ReadinessChanged -= onChanged;
        ws.RaiseProvidersChanged();

        Assert.AreEqual(1, told);
    }

    private static void OnUi(Func<Task> body)
    {
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.InvokeAsync(async () =>
            {
                try { await body(); }
                catch (Exception ex) { failed = ex; }
                finally { frame.Continue = false; }
            });
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) Assert.Fail("It did not finish within thirty seconds.");
        if (failed is not null) ExceptionDispatchInfo.Capture(failed).Throw();
    }
}
