using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.FileActions;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// One thread standing in for the app's UI thread: whatever the viewer marshals runs there, in order, as it does on
/// the dispatcher — so the bitmaps it presents are made and written on one thread. An await begun there comes back
/// there, as it comes back to the dispatcher, so a view-model's own async work never runs beside the test driving it.
/// </summary>
internal sealed class ViewerUiThread : IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
    private readonly Thread _thread;

    public ViewerUiThread()
    {
        _thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new ThreadQueueContext(this));
            foreach (var action in _work.GetConsumingEnumerable()) action();
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task Run(Action action)
    {
        if (Thread.CurrentThread == _thread) { action(); return Task.CompletedTask; }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }

    public T Invoke<T>(Func<T> read)
    {
        T value = default!;
        Run(() => value = read()).GetAwaiter().GetResult();
        return value;
    }

    /// <summary>Queues an await's continuation; one coming back after the thread is done is dropped, as a shut-down dispatcher drops it.</summary>
    private void Post(Action continuation)
    {
        try { _work.Add(continuation); }
        catch (InvalidOperationException) when (_work.IsAddingCompleted) { }
    }

    /// <summary>The thread's context: what an await posts to it joins the thread's queue.</summary>
    private sealed class ThreadQueueContext(ViewerUiThread ui) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => ui.Post(() => callback(state));

        public override void Send(SendOrPostCallback callback, object? state) => ui.Run(() => callback(state)).GetAwaiter().GetResult();

        public override SynchronizationContext CreateCopy() => this;
    }

    /// <summary>
    /// Has <paramref name="shell"/> run a queued background task at once, off the thread that queued it as the app runs it, then say
    /// it is done — so a test reads what the task did as soon as the queueing call returns, and the task's own awaits never wait on
    /// the thread blocked for it.
    /// </summary>
    public static void RunQueuedTasksAtOnce(IShellServices shell) =>
        shell.When(s => s.QueueBackgroundTask(Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>?>(), Arg.Any<CancellationToken>()))
             .Do(call =>
             {
                 var task = call.Arg<IBackgroundTask>();
                 Task.Run(() => task.RunAsync(CancellationToken.None)).GetAwaiter().GetResult();
                 call.Arg<Action<bool>?>()?.Invoke(true);
             });

    public void Dispose() => _work.CompleteAdding();
}
