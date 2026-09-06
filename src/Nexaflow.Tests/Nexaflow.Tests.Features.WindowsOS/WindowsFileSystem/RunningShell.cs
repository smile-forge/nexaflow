using System;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// A substitute <see cref="IShellServices"/> whose background queue and UI marshal actually do
/// something.
/// <para>
/// A bare substitute swallows <see cref="IShellServices.QueueBackgroundTask"/>, so once copy, move
/// and delete moved off the UI thread every test that asserted on their effect would have been
/// asserting on work that never started — and passed or failed for the wrong reason. This runs the
/// task and invokes the completion callback, so a test can await the operation instead.
/// </para>
/// </summary>
internal static class RunningShell
{
    /// <summary>Teaches <paramref name="shell"/> to run what it is given. Returns the same instance.</summary>
    public static IShellServices Runs(this IShellServices shell)
    {
        shell.When(s => s.QueueBackgroundTask(Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>?>(), Arg.Any<CancellationToken>()))
             .Do(call =>
             {
                 var task       = call.ArgAt<IBackgroundTask>(0);
                 var onComplete = call.ArgAt<Action<bool>?>(1);
                 var ct         = call.ArgAt<CancellationToken>(2);

                 _ = Task.Run(async () =>
                 {
                     bool ok = true;
                     try { await task.RunAsync(ct); }
                     catch { ok = false; }
                     onComplete?.Invoke(ok);
                 });
             });

        // Posted, not inline — and in order, like the dispatcher it stands in for.
        //
        // It used to run the action on the calling thread, which quietly removed the one property the
        // real marshal has: that a background task which reports its outcome through it has NOT been
        // observed by the time it returns. Under the substitute the state was always already there, so a
        // whole class of "read it too early" bug could not be reproduced — and one was not: rows judged
        // on their state at task-return never retired, and piled up in the panel while every test passed.
        var pump = Task.CompletedTask;
        var gate = new object();

        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call =>
        {
            var action = call.Arg<Action>();
            lock (gate) return pump = pump.ContinueWith(_ => action(), TaskScheduler.Default);
        });

        return shell;
    }
}
