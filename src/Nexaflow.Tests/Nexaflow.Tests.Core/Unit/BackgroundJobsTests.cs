using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Slow work the assistant leaves running: the model hears at once that it started; it is listed with its workspace's
/// background tasks, saying what it is doing, until it ends or is stopped; the user is told how it ended and the run's handler
/// is given the outcome to keep. A workspace's jobs stop with its models, and its window lists only its own.
/// </summary>
[TestClass]
[CoversNode("aibar-background-tasks")]
public class BackgroundJobsTests
{
    /// <summary>A slow tool: it reports its steps the way a provider does, then waits to be let finish — or stopped.</summary>
    private sealed class Drawing(IBackgroundActivityManager activities) : IClientTool
    {
        public readonly TaskCompletionSource<bool> Finish = new();
        public readonly TaskCompletionSource Begun = new();
        public string Name => "draw";
        public string Description => "Draws slowly.";
        public IReadOnlyList<ClientToolParameter> Parameters => [];
        public ToolSafety Safety => ToolSafety.SafeOperation;
        public bool Parallelizable => false;
        public bool RunsInBackground(JsonObject arguments) => true;
        public ToolCallAnnouncement? Announce(JsonObject arguments) => new("draw", "a picture", "Drawing a picture", "Picture made.", "No picture was made.");

        public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var activity = activities.StartActivity("Drawing with the model…");
            activity.Update("Drawing with the model — step 3 of 8");
            Begun.SetResult();
            var made = await Finish.Task.WaitAsync(ct);
            activity.Complete();
            return made
                ? ToolResult.Ok("made a.png", @"Saved to C:\Pictures\a.png and opened it.") with
                  {
                      Attachments = [@"C:\Pictures\a.png"],
                      Images      = [new ContextImage([0x89, 0x50, 0x4E, 0x47], "image/png", "a.png")],
                  }
                : ToolResult.Error("could not draw it", "The model failed.");
        }
    }

    private sealed class Model(params string[] replies) : ILlmProvider
    {
        private readonly Queue<string> _replies = new(replies);
        public List<IReadOnlyList<LlmMessage>> Asked { get; } = [];
        public string Name => "Claude";
        public DataResidency Residency => DataResidency.LeavesMachine;
        public bool IsAvailable => true;

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            Asked.Add([.. messages]);
            return Said(_replies.Count > 0 ? _replies.Dequeue() : "done");
        }

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["m"]);

        private static async IAsyncEnumerable<LlmStreamEvent> Said(string text)
        {
            await Task.Yield();
            yield return new LlmTextDelta(text);
        }
    }

    private sealed class User : IAIResponseHandler
    {
        public List<string> Ended { get; } = [];
        public void BeginResponse(string userText) { }
        public void ReportProgress(string message) { }
        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => Task.FromResult(true);
        public void ShowFinal(string finalMarkdown) { }
        public void Abort() { }
        public void BackgroundJobEnded(string outcome) => Ended.Add(outcome);
    }

    private sealed class Page(params IClientTool[] tools) : IPageViewModel
    {
        public string GetContext() => "A page.";
        public IReadOnlyList<IClientTool> GetClientTools() => tools;
    }

    private const string DrawCall = "```client_tool\n{\"tool\":\"draw\",\"arguments\":{}}\n```";

    private const string AskHowTheyStand = "```client_tool\n{\"tool\":\"get_background_tasks\",\"arguments\":{}}\n```";
    private const string StopTheFirst = "```client_tool\n{\"tool\":\"stop_background_task\",\"arguments\":{\"id\":1}}\n```";

    /// <summary>What the model was told last: the answer to its last call.</summary>
    private static string Answered(Model model) => model.Asked.Last().Last().Text;

    /// <summary>One workspace's assistant, its jobs listed in <paramref name="tasks"/> and its notifications in <paramref name="told"/>.</summary>
    private static (AIService Service, WorkspaceRuntime Workspace) Assistant(BackgroundActivityManager tasks, List<NotificationItem> told, Model model)
    {
        var ws  = new WorkspaceRuntime();
        var svc = new AIService(ws, TestTemp.Root, new BackgroundJobs(tasks, ws, told.Add));
        Set(nameof(WorkspaceRuntime.AiService), svc);
        Set(nameof(WorkspaceRuntime.ShellServices), new ShellServices(ws));
        svc.Register("c1", model);
        var config = new AiConfig { Columns = [new ProviderModelPair { Id = "c1", ProviderName = "Claude", Model = "m" }] };
        config.Assignments["GeneralIntelligence"] = AbilityChain.Of("c1");
        svc.LoadAbilityConfig(config);
        return (svc, ws);

        void Set(string property, object value) =>
            typeof(WorkspaceRuntime).GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(ws, [value]);
    }

    [TestMethod]
    public void ASlowCall_StartsAndTheRunMovesOn_ThenTheJobEnds_TellingTheUserAndTheRun() => OnUi(async () =>
    {
        var tasks   = new BackgroundActivityManager();
        var told    = new List<NotificationItem>();
        var model   = new Model(DrawCall, "It is being drawn.");
        var (svc, ws) = Assistant(tasks, told, model);
        var drawing = new Drawing(tasks);
        var user    = new User();

        var reply = await svc.RunAgentAsync(new Page(drawing), "Draw a lighthouse.", true, user);
        await drawing.Begun.Task;

        Assert.AreEqual("It is being drawn.", reply!.Text, "the run did not wait for the picture");
        StringAssert.Contains(model.Asked.Last().Last().Text, BackgroundJobs.Started(1));
        var job = tasks.Tasks.Single(t => t.Workspace == ws);
        Assert.AreEqual("Drawing a picture", job.Description);
        Assert.AreEqual("Drawing with the model — step 3 of 8", job.Detail, "the provider's progress is the job's");
        Assert.AreEqual(1, tasks.Tasks.Count, "and not a task of its own");
        Assert.IsTrue(job.CanStop);

        drawing.Finish.SetResult(true);
        await Until(() => job.Status != TaskStatus.Running);

        Assert.AreEqual(TaskStatus.Completed, job.Status);
        Assert.AreEqual("Picture made.", told.Single().Title);
        Assert.AreEqual(@"Picture made. Saved to C:\Pictures\a.png and opened it.", user.Ended.Single());
    });

    [TestMethod]
    public void AJob_Stopped_EndsStopped_AndTheRunIsToldSo() => OnUi(async () =>
    {
        var tasks   = new BackgroundActivityManager();
        var told    = new List<NotificationItem>();
        var (svc, _) = Assistant(tasks, told, new Model(DrawCall, "Drawing."));
        var drawing = new Drawing(tasks);
        var user    = new User();
        await svc.RunAgentAsync(new Page(drawing), "Draw.", true, user);
        await drawing.Begun.Task;
        var job = tasks.Tasks.Single(t => t.Workspace is not null);

        job.StopCommand.Execute(null);
        await Until(() => job.Status != TaskStatus.Running);

        Assert.AreEqual(TaskStatus.Stopped, job.Status);
        Assert.IsFalse(job.CanStop);
        Assert.AreEqual(0, told.Count, "the user stopped it, so there is nothing to tell them");
        StringAssert.StartsWith(user.Ended.Single(), "No picture was made.");
    });

    /// <summary>A video model that makes clips from a picture, holding each until it is stopped.</summary>
    private sealed class Filming : IVideoGenerationProvider
    {
        public readonly TaskCompletionSource<VideoGenerationRequest> Asked = new();
        public string Name => "Filming";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public IReadOnlyList<int> Lengths => [5];
        public bool StartsFromImages => true;
        public VideoSettings Settings { get; } = new([new(16, 9), new(9, 16)]) { Seeds = true };
        public (int Width, int Height)? FirstFrameSize(MediaAspect aspect, string? resolution) => null;
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public async Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default)
        {
            Asked.SetResult(request);
            await Task.Delay(Timeout.Infinite, ct);
            throw new OperationCanceledException(ct);
        }
    }

    private sealed class Painting : IImageGenerationProvider
    {
        public static readonly byte[] Drawn = [0x89, 0x50, 0x4E, 0x47, 9, 9];
        public readonly TaskCompletionSource<ImageGenerationRequest> Asked = new();
        public string Name => "Painting";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public int MaxSources => 0;
        public bool Masks => false;
        public ImageSettings Settings { get; } = new([new(1, 1), new(3, 2), new(2, 3)]) { Details = [ImageDetail.Low, ImageDetail.High], Seeds = true };
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
        {
            Asked.TrySetResult(request);
            return Task.FromResult(new GeneratedMedia(new MediaBytes(Drawn, "image/png")));
        }
    }

    [TestMethod]
    public void AClipTheUserAsksForFromAPicture_RunsAsTheWorkspacesJob_WithNothingAskedAndNoRunToTell() => OnUi(async () =>
    {
        var tasks   = new BackgroundActivityManager();
        var told    = new List<NotificationItem>();
        var (svc, ws) = Assistant(tasks, told, new Model("unused"));
        var filming = new Filming();
        svc.Register("v1", filming);
        var config = new AiConfig { Columns = [new ProviderModelPair { Id = "v1", ProviderName = "Filming", Model = "f", Kind = ModelKind.VideoGeneration }] };
        config.Assignments["HighQualityVideoGeneration"] = AbilityChain.Of("v1");
        svc.LoadAbilityConfig(config);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        CollectionAssert.AreEqual(new[] { 5 }, svc.PictureClipLengths.ToArray(), "the lengths of the model that carries a picture on");
        svc.MakeClipFromPicture(new ClipFromPicture(png, "The fox sits down.", 5, Portrait: true));
        var asked = await filming.Asked.Task;

        Assert.AreEqual(("The fox sits down.", MediaShape.Portrait, 5), (asked.Prompt, asked.Aspect.Shape, asked.Seconds));
        CollectionAssert.AreEqual(png, asked.FirstFrame!.Bytes, "the picture as the user saw it");
        var job = tasks.Tasks.Single(t => t.Workspace == ws);
        Assert.IsTrue(job.CanStop, "listed with the workspace's jobs, and stoppable there");

        job.StopCommand.Execute(null);
        await Until(() => job.Status != TaskStatus.Running);
        Assert.AreEqual(TaskStatus.Stopped, job.Status);
    });

    [TestMethod]
    public void APictureTheUserSetsUp_IsDrawnAsTheWorkspacesJob_WithWhatTheyChose_AndSavedWhereTheyNamedIt() => OnUi(async () =>
    {
        var tasks   = new BackgroundActivityManager();
        var (svc, _) = Assistant(tasks, [], new Model("unused"));
        var painting = new Painting();
        svc.Register("i1", painting);
        var config = new AiConfig { Columns = [new ProviderModelPair { Id = "i1", ProviderName = "Painting", Model = "p", Kind = ModelKind.ImageGeneration }] };
        config.Assignments["ImageGeneration"] = AbilityChain.Of("i1");
        svc.LoadAbilityConfig(config);

        var making = svc.MakesPictures!;
        Assert.IsNull(making.HighQuality, "only the standard row has a model");
        CollectionAssert.AreEqual(new[] { "1:1", "3:2", "2:3" }, making.Standard!.Aspects.ToArray());
        CollectionAssert.AreEqual(new[] { "low", "high" }, making.Standard.Details.ToArray(), "as generate_image names them");
        Assert.IsTrue(making.Standard.Seeds);

        var folder = Path.Combine(TestTemp.Root, "nexaflow-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "fox.png");
            svc.MakePicture(new PictureAsked("A fox in the snow", path, HighQuality: false) { Aspect = "3:2", Detail = "high", Seed = 7 });
            var asked = await painting.Asked.Task;

            Assert.AreEqual(("A fox in the snow", new MediaAspect(3, 2), ImageDetail.High, (int?)7), (asked.Prompt, asked.Aspect, asked.Detail, asked.Seed));
            // Read once it is whole: the file is there from the moment it is opened to be written.
            byte[] saved = [];
            await Until(() =>
            {
                try { return (saved = File.ReadAllBytes(path)).Length == Painting.Drawn.Length; }
                catch (IOException) { return false; }
            });
            CollectionAssert.AreEqual(Painting.Drawn, saved, "saved as the name the user gave it");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    [TestMethod]
    public void APictureOrAClip_IsNotOffered_WhereNoModelMakesIt_AndOneAskedForAnywayIsRefused()
    {
        var (svc, _) = Assistant(new BackgroundActivityManager(), [], new Model("unused"));

        Assert.IsNull(svc.MakesPictures);
        Assert.IsNull(svc.MakesClips);
        Assert.ThrowsExactly<InvalidOperationException>(() => svc.MakePicture(new PictureAsked("A fox", Path.Combine(TestTemp.Root, "fox.png"), false)));
        Assert.ThrowsExactly<InvalidOperationException>(() => svc.MakeClip(new ClipAsked("Waves", Path.Combine(TestTemp.Root, "waves.mp4"), 5)));
    }

    [TestMethod]
    public void WhereNoModelMakesAClipFromAPicture_NoneIsOffered_OrMade()
    {
        var (svc, _) = Assistant(new BackgroundActivityManager(), [], new Model("unused"));

        Assert.AreEqual(0, svc.PictureClipLengths.Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => svc.MakeClipFromPicture(new ClipFromPicture([1], "Waves.", 5, Portrait: false)));
    }

    [TestMethod]
    public void AWorkspacesJobs_StopWithIt_AndAWindowListsOnlyItsOwn() => OnUi(async () =>
    {
        var tasks    = new BackgroundActivityManager();
        var told     = new List<NotificationItem>();
        var (mine, here)    = Assistant(tasks, told, new Model(DrawCall, "Drawing."));
        var (theirs, there) = Assistant(tasks, told, new Model(DrawCall, "Drawing."));
        var drawingHere  = new Drawing(tasks);
        var drawingThere = new Drawing(tasks);
        await mine.RunAgentAsync(new Page(drawingHere), "Draw.", true, new User());
        await theirs.RunAgentAsync(new Page(drawingThere), "Draw.", true, new User());
        await Task.WhenAll(drawingHere.Begun.Task, drawingThere.Begun.Task);

        var window = new WorkspaceBackgroundTasks(tasks, () => here);
        Assert.AreEqual(1, window.Running.Count);
        Assert.AreSame(here, window.Running.Single().Workspace);
        Assert.IsTrue(window.Any);

        tasks.StopJobsOf(here);
        await Until(() => window.Running.Count == 0);

        Assert.IsFalse(window.Any);
        Assert.AreEqual(TaskStatus.Running, tasks.Tasks.Single(t => t.Workspace == there).Status, "another workspace's job runs on");
        drawingThere.Finish.SetResult(true);
    });

    [TestMethod]
    public void TheModel_AsksHowItsJobsStand_AndIsShownWhatAFinishedOneMade_Once() => OnUi(async () =>
    {
        var tasks   = new BackgroundActivityManager();
        var model   = new Model(DrawCall, "Drawing.", AskHowTheyStand, "At step 3.", AskHowTheyStand, "Done.", AskHowTheyStand, "Still done.");
        var (svc, _) = Assistant(tasks, [], model);
        var drawing = new Drawing(tasks);
        var page    = new Page(drawing);
        await svc.RunAgentAsync(page, "Draw a lighthouse.", true, new User());
        await drawing.Begun.Task;

        await svc.RunAgentAsync(page, "How is the picture going?", true, new User());
        StringAssert.Contains(model.Asked.Last()[0].Text, "stop_background_task", "both tools are offered while a task runs");
        StringAssert.Contains(Answered(model), "Task 1: Drawing a picture — running for");
        StringAssert.Contains(Answered(model), "now: Drawing with the model — step 3 of 8");

        drawing.Finish.SetResult(true);
        await Until(() => tasks.Tasks.Single(t => t.Workspace is not null).Status == TaskStatus.Completed);
        await svc.RunAgentAsync(page, "Is it done?", true, new User());
        StringAssert.Contains(Answered(model), @"done after");
        StringAssert.Contains(Answered(model), @"Saved to C:\Pictures\a.png");
        StringAssert.Contains(Answered(model), "An image was captured", "what it made is shown to the model");

        await svc.RunAgentAsync(page, "And now?", true, new User());
        Assert.IsFalse(Answered(model).Contains("An image was captured"), "once is enough");
    });

    [TestMethod]
    public void TheModel_StopsAJobTheUserNoLongerWants() => OnUi(async () =>
    {
        var tasks   = new BackgroundActivityManager();
        var model   = new Model(DrawCall, "Drawing.", StopTheFirst, "Stopped it.");
        var (svc, _) = Assistant(tasks, [], model);
        var drawing = new Drawing(tasks);
        var user    = new User();
        await svc.RunAgentAsync(new Page(drawing), "Draw a lighthouse.", true, user);
        await drawing.Begun.Task;

        await svc.RunAgentAsync(new Page(drawing), "Never mind the picture.", true, new User());

        StringAssert.Contains(Answered(model), "Task 1 (Drawing a picture) is being stopped");
        await Until(() => tasks.Tasks.Single(t => t.Workspace is not null).Status == TaskStatus.Stopped);
        StringAssert.StartsWith(user.Ended.Single(), "No picture was made.", "the run that started it is told");
    });

    /// <summary>Runs <paramref name="body"/> on a thread with a dispatcher of its own, as the app's UI thread: the loop, the
    /// tools and the background tasks all come back to it.</summary>
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

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.IsTrue(condition(), "it came about within five seconds");
    }
}
