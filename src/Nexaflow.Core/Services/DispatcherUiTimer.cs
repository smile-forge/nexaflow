using System.Windows.Threading;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// The shell's <see cref="IUiTimer"/>: a <see cref="DispatcherTimer"/> on the workspace's UI dispatcher, repeating, or
/// stopping before each tick when it is a delay.
/// </summary>
internal sealed class DispatcherUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;
    private readonly bool _once;
    private Action? _tick;

    /// <param name="once">A delay: it stops before each tick, so it ticks once each time it is started.</param>
    internal DispatcherUiTimer(Dispatcher ui, TimeSpan interval, Action tick, UiTimerPriority priority, bool once)
    {
        _once  = once;
        _tick  = tick;
        _timer = new DispatcherTimer(PriorityOf(priority), ui) { Interval = interval };
        _timer.Tick += OnTick;
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (_tick is not null) _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Restart()
    {
        _timer.Stop();
        Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _tick = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_once) _timer.Stop();
        _tick?.Invoke();
    }

    private static DispatcherPriority PriorityOf(UiTimerPriority priority) => priority switch
    {
        UiTimerPriority.Render => DispatcherPriority.Render,
        _                      => DispatcherPriority.Background,
    };
}
