using System.Text.Json.Nodes;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Visuals.Common.Localization;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;
using System.Text;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// The work a run sets going and moves on from. A call of a tool that <see cref="IClientTool.RunsInBackground"/> runs as a job
/// among the app's background tasks, named with the workspace whose assistant started it, where the user sees what it is
/// doing and for how long, and can stop it; the model is told at once that it has started, and under which id. When it ends
/// the user is told, and what came of it goes to the handler of the run that started it, for a conversation to keep. While
/// any is running or lately ended, the model has tools to ask how they stand and to stop one (<see cref="Tools"/>).
/// </summary>
/// <param name="tasks">The app's background tasks, where the jobs are listed.</param>
/// <param name="workspace">The workspace whose assistant sets them going.</param>
/// <param name="notify">Tells the user a job has ended — the app's notifications.</param>
public sealed class BackgroundJobs(BackgroundActivityManager tasks, WorkspaceRuntime workspace, Action<NotificationItem> notify)
{
    /// <summary>How many ended jobs the model can still ask about.</summary>
    internal const int EndedKept = 10;

    private readonly Lock _gate = new();
    private readonly List<Job> _jobs = [];   // newest first: every one running, and the last few ended
    private int _lastId;

    /// <summary>One job as the model knows it: its id, its task, and once ended, when and what came of it.</summary>
    internal sealed class Job(int id, BackgroundTask task)
    {
        public int Id { get; } = id;
        public BackgroundTask Task { get; } = task;
        public DateTime? Ended { get; set; }

        /// <summary>What came of it; null while it runs, and for a job stopped before it finished.</summary>
        public ToolResult? Result { get; set; }

        /// <summary>Whether the model has been shown what it made.</summary>
        public bool Shown { get; set; }

        public TimeSpan Elapsed => (Ended ?? DateTime.Now) - Task.StartedAt;
    }

    /// <summary>The jobs the model can ask about, newest first.</summary>
    internal IReadOnlyList<Job> Jobs
    {
        get { lock (_gate) return [.. _jobs]; }
    }

    /// <summary>The tools for the jobs — how they stand, and stopping one — while there is any to ask about; none otherwise.</summary>
    internal IReadOnlyList<IClientTool> Tools => Jobs.Count == 0 ? [] : [new GetBackgroundTasksTool(this), new StopBackgroundTaskTool(this)];

    /// <summary>Sets the call going as a job, and says so: the result the run hands the model, and the job's end.</summary>
    /// <param name="origin">The run that set it going, told when it ends; null for a job the user set going themselves.</param>
    internal (ToolResult Result, Task Ended) Start(IClientTool tool, JsonObject arguments, IAIResponseHandler? origin)
    {
        var told = tool.Announce(arguments);
        Job job;
        lock (_gate)
        {
            job = new Job(++_lastId, tasks.StartJob(told?.Doing ?? tool.Name, workspace, new CancellationTokenSource()));
            _jobs.Insert(0, job);
        }
        return (ToolResult.Ok("started in the background", Started(job.Id)) with { InBackground = true }, RunAsync(job, tool, arguments, told, origin));
    }

    /// <summary>What the model is told of a call that runs on in the background.</summary>
    internal static string Started(int id) =>
        $"Started in the background as task {id}. The user sees it among the background tasks, can stop it, and is told when " +
        "it ends; what it makes opens by itself. Do not wait for it, and do not say it is done: when asked how it is going, " +
        "get_background_tasks says.";

    /// <summary>The job to its end, and the telling of how it ended. Never throws.</summary>
    private async Task RunAsync(Job job, IClientTool tool, JsonObject arguments, ToolCallAnnouncement? told, IAIResponseHandler? origin)
    {
        var task = job.Task;
        BackgroundActivityManager.Within(task);

        ToolResult? result;
        try { result = await tool.InvokeAsync(arguments, task.Stopping!.Token); }
        catch (OperationCanceledException) { result = null; }
        catch (Exception ex) { result = ToolResult.Error($"{tool.Name} failed: {ex.Message}"); }

        lock (_gate)
        {
            job.Ended  = DateTime.Now;
            job.Result = result;
            var ended = _jobs.Where(j => j.Ended is not null).ToList();
            foreach (var old in ended.Skip(EndedKept)) _jobs.Remove(old);
        }

        var done   = told?.Done ?? Str.Format("Shell.BackgroundTasks.DoneFormat", task.Description);
        var unmade = told?.Unchanged ?? Str.Format("Shell.BackgroundTasks.FailedFormat", task.Description);
        if (result is null)
        {
            var stopped = Str.Get("Shell.BackgroundTasks.Stopped");
            tasks.EndJob(task, TaskStatus.Stopped, stopped);
            origin?.BackgroundJobEnded($"{unmade} {stopped}");
            return;
        }

        tasks.EndJob(task, result.IsError ? TaskStatus.Failed : TaskStatus.Completed, result.Summary);
        var title = result.IsError ? unmade : done;
        notify(new NotificationItem
        {
            Title    = title,
            Body     = result.ModelText,
            Severity = result.IsError ? MessageSeverity.Error : MessageSeverity.Info,
        });
        origin?.BackgroundJobEnded($"{title} {result.ModelText}");
    }

    /// <summary>
    /// How the jobs stand, for the model: each one's id and what it is, and whether it is running — for how long, and what it
    /// is doing now, which is all a model shows of work in progress — or how it ended and what came of it. What a finished
    /// job made is shown to the model once, as its images; the files it made go with the answer.
    /// </summary>
    internal ToolResult Describe()
    {
        var jobs = Jobs;
        var said = new StringBuilder("Background tasks you set going, newest first:\n");
        var images = new List<ContextImage>();
        var files  = new List<string>();
        foreach (var job in jobs)
        {
            var task = job.Task;
            said.Append("- Task ").Append(job.Id).Append(": ").Append(task.Description).Append(" — ");
            var took = Clock(job.Elapsed);
            switch (task.Status)
            {
                case TaskStatus.Running:
                    said.Append("running for ").Append(took);
                    if (task.Detail is { Length: > 0 } now) said.Append("; now: ").Append(now);
                    break;
                case TaskStatus.Stopped:
                    said.Append("stopped after ").Append(took);
                    break;
                default:
                    said.Append(task.Status == TaskStatus.Failed ? "failed after " : "done after ").Append(took)
                        .Append(": ").Append(job.Result?.ModelText.ReplaceLineEndings(" ") ?? "");
                    if (job.Result?.Attachments is { } made) files.AddRange(made);
                    if (!job.Shown && job.Result?.Images is { Count: > 0 } pictures) { images.AddRange(pictures); job.Shown = true; }
                    break;
            }
            said.Append('\n');
        }
        return ToolResult.Ok($"{jobs.Count} background task(s)", said.ToString().TrimEnd()) with
        {
            Images      = images.Count > 0 ? images : null,
            Attachments = files.Count > 0 ? files : null,
        };
    }

    /// <summary>Stops the job <paramref name="id"/>, where it is running.</summary>
    internal ToolResult Stop(int id)
    {
        var job = Jobs.FirstOrDefault(j => j.Id == id);
        if (job is null) return ToolResult.Error("no such task", $"There is no background task {id}. get_background_tasks lists them.");
        if (!job.Task.CanStop) return ToolResult.Error("not running", $"Task {id} is not running: it has ended ({job.Task.Status.ToString().ToLowerInvariant()}).");

        job.Task.StopCommand.Execute(null);
        return ToolResult.Ok($"stopping task {id}", $"Task {id} ({job.Task.Description}) is being stopped; nothing it was making is kept.");
    }

    private static string Clock(TimeSpan span) => span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
}

/// <summary>How the background tasks the model set going stand, and what came of the ended ones.</summary>
internal sealed class GetBackgroundTasksTool(BackgroundJobs jobs) : IClientTool
{
    public string Name => "get_background_tasks";

    public string Description =>
        "Lists the slow work you set going in the background — pictures at high quality, video clips — with what each is " +
        "doing and for how long, and for one that has ended, what came of it; a finished picture is shown to you. Check when " +
        "the user asks, or when you need what one made: do not poll, as the user is told when each ends.";

    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.SafeOperation;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.FromResult(jobs.Describe());
}

/// <summary>Stops a background task the model set going, when the user no longer wants it.</summary>
internal sealed class StopBackgroundTaskTool(BackgroundJobs jobs) : IClientTool
{
    public string Name => "stop_background_task";

    public string Description => "Stops a task you set going in the background, by its id, when the user no longer wants it.";

    public IReadOnlyList<ClientToolParameter> Parameters => [new("id", "The task's id, as get_background_tasks or its start gave it.", Type: "integer")];

    /// <summary>It only stops work the assistant itself set going, which the user can as easily start again.</summary>
    public ToolSafety Safety => ToolSafety.SafeOperation;

    public bool Parallelizable => false;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        => Task.FromResult(ToolArgs.Int(arguments, "id", 0) is > 0 and var id
            ? jobs.Stop(id)
            : ToolResult.Error("no id", "Give the 'id' of the task to stop, as get_background_tasks lists it."));
}
