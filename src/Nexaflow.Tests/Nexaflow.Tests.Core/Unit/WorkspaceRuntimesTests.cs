using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The session's workspaces: one runtime each, however often a workspace is opened; dormant once its last window goes — unless
/// its work is still running — and back as it was when a window returns; ended when its dormant time runs out. A standby is
/// no window anyone saw: its closing is not the last window closing.
/// </summary>
[TestClass]
[CoversNode("ws-lifetimes")]
public class WorkspaceRuntimesTests
{
    private readonly ManualTime _time = new();

    [TestMethod]
    public async Task AWorkspace_HasOneRuntime_HoweverOftenItIsOpened()
    {
        var runtimes  = Runtimes();
        var workspace = TestRuntimes.Saved(new Workspace { Name = "Home" });

        var first  = runtimes.Open(workspace);
        var second = runtimes.Open(workspace);

        Assert.AreSame(first, second, "a second launch joins the workspace's runtime rather than forking its state");
        await first.DisposeAsync();
    }

    [TestMethod]
    public async Task AWorkspaceLeftByItsLastWindow_TurnsDormant_AndComesBackAsItWas()
    {
        var runtimes  = Runtimes();
        var workspace = TestRuntimes.Saved(new Workspace { Name = "Videos" });
        var runtime   = runtimes.Open(workspace);
        var window    = new FakeWindowHost();
        runtime.ShellServices.RegisterWindow(window);

        runtime.ShellServices.UnregisterWindow(window);

        Assert.AreEqual(WorkspaceRuntimeState.Dormant, runtime.State);
        Assert.IsNull(runtime.Providers, "a dormant workspace's providers are back in the pool");

        var resumed = runtimes.Open(workspace);

        Assert.AreSame(runtime, resumed, "coming back within its time finds the same runtime");
        Assert.AreEqual(WorkspaceRuntimeState.Active, resumed.State);
        Assert.IsNotNull(resumed.Providers);
        await resumed.DisposeAsync();
    }

    [TestMethod]
    public void ADormantWorkspace_EndsWhenItsTimeRunsOut()
    {
        var runtimes  = Runtimes();
        var workspace = TestRuntimes.Saved(new Workspace { Name = "Community" });
        var runtime   = runtimes.Open(workspace);
        var window    = new FakeWindowHost();
        runtime.ShellServices.RegisterWindow(window);
        runtime.ShellServices.UnregisterWindow(window);

        _time.Elapse();

        Assert.AreEqual(WorkspaceRuntimeState.Ended, runtime.State);
        Assert.IsNull(runtimes.Find(workspace), "the next window builds the workspace afresh");
    }

    [TestMethod]
    public async Task AWorkspaceWhoseWorkStillRuns_StaysActive_UntilTheWorkIsDone()
    {
        var tasks     = new BackgroundActivityManager();
        var runtimes  = Runtimes(TestRuntimes.FactoryWith(tasks, _ => { }));
        var workspace = TestRuntimes.Saved(new Workspace { Name = "Home" });
        var runtime   = runtimes.Open(workspace);
        var window    = new FakeWindowHost();
        runtime.ShellServices.RegisterWindow(window);
        var job = tasks.StartJob("Summarising", runtime, new CancellationTokenSource());

        runtime.ShellServices.UnregisterWindow(window);
        Assert.AreEqual(WorkspaceRuntimeState.Active, runtime.State, "the work is the person's, and needs the workspace's models");

        tasks.EndJob(job, Nexaflow.Core.Models.TaskStatus.Completed, null);
        Assert.AreEqual(WorkspaceRuntimeState.Dormant, runtime.State);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public void AStandbyClosing_IsNotTheLastWindowClosing_AndEndsIt()
    {
        var runtimes = Runtimes();
        int said = 0;
        runtimes.LastWindowClosed += (_, _) => said++;
        var standby = runtimes.OpenStandby(TestRuntimes.Saved(new Workspace { Name = "Ready" }));
        var window  = new FakeWindowHost();
        standby.ShellServices.RegisterWindow(window);

        standby.ShellServices.UnregisterWindow(window);

        Assert.AreEqual(0, said, "setting a standby aside leaves the app as windowless as it was");
        Assert.AreEqual(WorkspaceRuntimeState.Ended, standby.State);
    }

    [TestMethod]
    public async Task TheLastWindowOfALaunch_Closing_IsSaid()
    {
        var runtimes = Runtimes();
        int said = 0;
        runtimes.LastWindowClosed += (_, _) => said++;
        var standby = runtimes.OpenStandby(TestRuntimes.Saved(new Workspace { Name = "Ready" }));
        runtimes.Adopt(standby);
        var window = new FakeWindowHost();
        standby.ShellServices.RegisterWindow(window);

        standby.ShellServices.UnregisterWindow(window);

        Assert.AreEqual(1, said, "a launch's window closing as the last one is what builds the next standby");
        await standby.DisposeAsync();
    }

    private WorkspaceRuntimes Runtimes(WorkspaceRuntimeFactory? factory = null)
        => new(factory ?? TestRuntimes.FactoryWith(tasks: null, notify: _ => { }), new InlineUi(), () => TimeSpan.FromMinutes(5),
               _ => true, _time, TestRuntimes.LoggerFor<WorkspaceRuntimes>());

    /// <summary>A UI thread that is always the calling one: what is posted to it runs at once.</summary>
    private sealed class InlineUi : IUiThread
    {
        public bool IsCurrent => true;
        public void Run(Action action) => action();
        public T Run<T>(Func<T> func) => func();
        public Task RunAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> RunAsync<T>(Func<Task<T>> action) => action();
        public void Post(Action action) => action();
        public void PostInBackground(Action action) => action();
        public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority, bool once) => new StillTimer(interval);
    }

    /// <summary>A timer that never ticks: nothing here waits on one.</summary>
    private sealed class StillTimer(TimeSpan interval) : IUiTimer
    {
        public TimeSpan Interval { get; set; } = interval;
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Restart() => IsRunning = true;
        public void Dispose() => IsRunning = false;
    }

    /// <summary>A clock whose timers fire when the test says the time is up.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<(TimerCallback Callback, object? State)> _due = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var entry = (callback, state);
            _due.Add(entry);
            return new Timer(() => _due.Remove(entry));
        }

        /// <summary>Every timer's time is up.</summary>
        public void Elapse()
        {
            foreach (var (callback, state) in _due.ToArray()) callback(state);
        }

        private sealed class Timer(Action cancel) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => cancel();
            public ValueTask DisposeAsync() { cancel(); return ValueTask.CompletedTask; }
        }
    }
}
