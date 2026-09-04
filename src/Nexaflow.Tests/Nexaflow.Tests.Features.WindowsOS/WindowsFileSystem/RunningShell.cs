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

        // Inline rather than posted: there is no dispatcher in a unit test, and the queue's only
        // requirement is that the action runs somewhere before the operation reports itself finished.
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>()();
            return Task.CompletedTask;
        });

        return shell;
    }
}
