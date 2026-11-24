using System;
using System.Windows.Threading;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The shell's timers, as a view-model sees them: a timer repeats until stopped, a delay runs once each time it is
/// started, and neither ticks once disposed.
/// <para>Runs a real dispatcher on an STA thread; opens no window.</para>
/// </summary>
[TestClass]
[CoversNode("shell-ui-timers")]
public class DispatcherUiTimerTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(20);

    /// <summary>Runs the dispatcher for <paramref name="span"/>, so whatever is due in it ticks.</summary>
    private static void Pump(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer(span, DispatcherPriority.ContextIdle, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        stop.Stop();
    }

    [TestMethod]
    [TestCategory("UI")]
    public void ATimer_RepeatsUntilStopped() => UiThread.Run(() =>
    {
        var ticks = 0;
        using var timer = new DispatcherUiTimer(Dispatcher.CurrentDispatcher, Short, () => ticks++, UiTimerPriority.Background, once: false);

        Pump(TimeSpan.FromMilliseconds(100));
        Assert.AreEqual(0, ticks, "made stopped");

        timer.Start();
        Pump(TimeSpan.FromMilliseconds(200));
        Assert.IsTrue(ticks >= 2, $"ticked {ticks} time(s)");
        Assert.IsTrue(timer.IsRunning);

        timer.Stop();
        var stoppedAt = ticks;
        Pump(TimeSpan.FromMilliseconds(100));
        Assert.AreEqual(stoppedAt, ticks);
    });

    [TestMethod]
    [TestCategory("UI")]
    public void ADelay_RunsOnceEachTimeItIsStarted() => UiThread.Run(() =>
    {
        var runs = 0;
        using var delay = new DispatcherUiTimer(Dispatcher.CurrentDispatcher, Short, () => runs++, UiTimerPriority.Background, once: true);

        delay.Restart();
        Pump(TimeSpan.FromMilliseconds(200));
        Assert.AreEqual(1, runs);
        Assert.IsFalse(delay.IsRunning, "it stops before it runs");

        delay.Restart();
        Pump(TimeSpan.FromMilliseconds(200));
        Assert.AreEqual(2, runs);
    });

    [TestMethod]
    [TestCategory("UI")]
    public void ADisposedTimer_NeverTicksAgain() => UiThread.Run(() =>
    {
        var ticks = 0;
        var timer = new DispatcherUiTimer(Dispatcher.CurrentDispatcher, Short, () => ticks++, UiTimerPriority.Render, once: false);
        timer.Start();
        timer.Dispose();

        timer.Start();
        Pump(TimeSpan.FromMilliseconds(150));

        Assert.AreEqual(0, ticks);
        Assert.IsFalse(timer.IsRunning);
    });
}
