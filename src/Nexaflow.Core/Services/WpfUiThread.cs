using Nexaflow.Features.Common;
using System.Windows.Threading;

namespace Nexaflow.Core.Services;

/// <summary>The UI thread as WPF runs it: its <see cref="System.Windows.Threading.Dispatcher"/>.</summary>
public sealed class WpfUiThread(Dispatcher dispatcher) : IUiThread
{
    /// <summary>The dispatcher, for the WPF pieces that are bound to one: a window, a dispatcher timer.</summary>
    internal Dispatcher Dispatcher { get; } = dispatcher;

    public bool IsCurrent => Dispatcher.CheckAccess();

    public void Run(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.Invoke(action);
    }

    public T Run<T>(Func<T> func) => Dispatcher.CheckAccess() ? func() : Dispatcher.Invoke(func);

    public Task RunAsync(Action action)
    {
        if (Dispatcher.CheckAccess()) { action(); return Task.CompletedTask; }
        return Dispatcher.InvokeAsync(action).Task;
    }

    public Task<T> RunAsync<T>(Func<Task<T>> action)
        => Dispatcher.CheckAccess() ? action() : Dispatcher.InvokeAsync(action).Task.Unwrap();

    public void Post(Action action) => Dispatcher.InvokeAsync(action);

    public void PostInBackground(Action action) => Dispatcher.InvokeAsync(action, DispatcherPriority.Background);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority, bool once)
        => new DispatcherUiTimer(Dispatcher, interval, tick, priority, once);
}
