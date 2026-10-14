using System.Text.Json.Nodes;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Visuals.Common.Localization;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// The work a run sets going and moves on from. A call of a tool that <see cref="IClientTool.RunsInBackground"/> runs as a job
/// among the app's background tasks, named with the workspace whose assistant started it, where the user sees what it is
/// doing and for how long, and can stop it; the model is told at once that it has started. When it ends the user is told,
/// and what came of it goes to the handler of the run that started it, for a conversation to keep.
/// </summary>
/// <param name="tasks">The app's background tasks, where the jobs are listed.</param>
/// <param name="workspace">The workspace whose assistant sets them going.</param>
/// <param name="notify">Tells the user a job has ended — the app's notifications.</param>
public sealed class BackgroundJobs(BackgroundActivityManager tasks, WorkspaceRuntime workspace, Action<NotificationItem> notify)
{
    /// <summary>What the model is told of a call that runs on in the background.</summary>
    internal const string Started =
        "Started in the background. The user sees it among the background tasks, can stop it, and is told when it ends; what it " +
        "makes opens by itself. Do not wait for it, and do not say it is done.";

    /// <summary>Sets the call going as a job, and says so: the result the run hands the model, and the job's end.</summary>
    internal (ToolResult Result, Task Ended) Start(IClientTool tool, JsonObject arguments, IAIResponseHandler origin)
    {
        var told = tool.Announce(arguments);
        var job  = tasks.StartJob(told?.Doing ?? tool.Name, workspace, new CancellationTokenSource());
        return (ToolResult.Ok("started in the background", Started), RunAsync(job, tool, arguments, told, origin));
    }

    /// <summary>The job to its end, and the telling of how it ended. Never throws.</summary>
    private async Task RunAsync(BackgroundTask job, IClientTool tool, JsonObject arguments, ToolCallAnnouncement? told, IAIResponseHandler origin)
    {
        BackgroundActivityManager.Within(job);

        ToolResult? result;
        try { result = await tool.InvokeAsync(arguments, job.Stopping!.Token); }
        catch (OperationCanceledException) { result = null; }
        catch (Exception ex) { result = ToolResult.Error($"{tool.Name} failed: {ex.Message}"); }

        var done     = told?.Done ?? Str.Format("Shell.BackgroundTasks.DoneFormat", job.Description);
        var unmade   = told?.Unchanged ?? Str.Format("Shell.BackgroundTasks.FailedFormat", job.Description);
        if (result is null)
        {
            var stopped = Str.Get("Shell.BackgroundTasks.Stopped");
            tasks.EndJob(job, TaskStatus.Stopped, stopped);
            origin.BackgroundJobEnded($"{unmade} {stopped}");
            return;
        }

        tasks.EndJob(job, result.IsError ? TaskStatus.Failed : TaskStatus.Completed, result.Summary);
        var title = result.IsError ? unmade : done;
        notify(new NotificationItem
        {
            Title    = title,
            Body     = result.ModelText,
            Severity = result.IsError ? MessageSeverity.Error : MessageSeverity.Info,
        });
        origin.BackgroundJobEnded($"{title} {result.ModelText}");
    }
}
