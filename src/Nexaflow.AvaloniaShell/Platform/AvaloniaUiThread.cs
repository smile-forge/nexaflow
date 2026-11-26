using Avalonia.Threading;

namespace Nexaflow.AvaloniaShell.Platform;

/// <summary>The session's UI thread on Avalonia: its dispatcher.</summary>
internal sealed class AvaloniaUiThread(Dispatcher dispatcher) : IUiThread
{
    public bool IsCurrent => dispatcher.CheckAccess();

    public void Run(Action action)
    {
        if (IsCurrent) action();
        else dispatcher.Invoke(action);
    }

    public T Run<T>(Func<T> func) => IsCurrent ? func() : dispatcher.Invoke(func);

    public Task RunAsync(Action action)
    {
        if (!IsCurrent) return dispatcher.InvokeAsync(action).GetTask();
        action();
        return Task.CompletedTask;
    }

    public Task<T> RunAsync<T>(Func<Task<T>> action) => IsCurrent ? action() : dispatcher.InvokeAsync(action);

    public void Post(Action action) => dispatcher.Post(action);

    public void PostInBackground(Action action) => dispatcher.Post(action, DispatcherPriority.Background);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority, bool once)
        => Run(() => new AvaloniaUiTimer(interval, tick, priority, once));
}

/// <summary>A timer ticking on the UI thread: a <see cref="DispatcherTimer"/>, stopped before its tick when it runs once.</summary>
internal sealed class AvaloniaUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;
    private readonly Action _tick;
    private readonly bool _once;

    /// <summary>Made on the UI thread, whose dispatcher an Avalonia 12 timer takes from the thread that makes it.</summary>
    public AvaloniaUiTimer(TimeSpan interval, Action tick, UiTimerPriority priority, bool once)
    {
        _tick = tick;
        _once = once;
        _timer = new DispatcherTimer(priority == UiTimerPriority.Render ? DispatcherPriority.Render : DispatcherPriority.Background)
        {
            Interval = interval,
        };
        _timer.Tick += OnTick;
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_once) _timer.Stop();
        _tick();
    }
}
