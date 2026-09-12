using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Common.Locate;

/// <summary>How a <see cref="LocateTour"/> paces itself.</summary>
public sealed record LocateOptions
{
    /// <summary>How long a lasso stays when the reader doesn't click: five seconds.</summary>
    public TimeSpan StepDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for a control not on screen yet — the one a click in the step before is opening —
    /// before passing over it.</summary>
    public TimeSpan AppearWithin { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>A click this soon after a lasso appears doesn't dismiss it: the tail of a double-click, or of the click
    /// that followed the link.</summary>
    public TimeSpan ClickGrace { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How often a missing control is looked for again, and a lassoed one checked it is still there.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Searched only when nothing outside it matches: the surface the link sits in.</summary>
    public DependencyObject? SearchLast { get; init; }
}

/// <summary>How a tour went: how many lassos it drew, the ids that never came on screen, and whether it was stopped.</summary>
public sealed record LocateResult(int Shown, IReadOnlyList<string> Missing, bool Cancelled);

/// <summary>
/// Lassoes controls on screen one after another — what a <see cref="LocateLink"/> does. Each lasso stays for
/// <see cref="LocateOptions.StepDuration"/> or until the reader clicks, whichever comes first, and then the next is
/// thrown. A control not on screen yet is waited for, since a click in the step before may be opening it, and passed
/// over if it never comes. <b>Esc</b> stops the tour.
/// <para>
/// A click is never swallowed. The lasso points at what to click; clicking it does that <i>and</i> moves the tour on.
/// </para>
/// <para>
/// One tour per root at a time: starting another stops the first. It runs on the root's UI thread, on dispatcher timers,
/// and does nothing between ticks.
/// </para>
/// </summary>
public sealed class LocateTour
{
    private static readonly ConditionalWeakTable<FrameworkElement, LocateTour> Running = new();

    private readonly FrameworkElement _root;
    private readonly IReadOnlyList<string> _ids;
    private readonly LocateOptions _options;
    private readonly AdornerLayer? _layer;
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _step;
    private readonly MouseButtonEventHandler _onMouseDown;
    private readonly KeyEventHandler _onKeyDown;
    private readonly TaskCompletionSource<LocateResult> _done = new();
    private readonly List<string> _missing = [];

    private int _index = -1;
    private int _shown;
    private long _since;
    private FrameworkElement? _target;
    private LassoAdorner? _lasso;

    private LocateTour(FrameworkElement root, IReadOnlyList<string> ids, LocateOptions options)
    {
        _root    = root;
        _ids     = ids;
        _options = options;
        // Over everything in the window — not the nearest layer, which inside a scroller would clip the loop to it.
        _layer   = AdornerLayer.GetAdornerLayer(root is Window { Content: Visual content } ? content : root);

        _poll = new DispatcherTimer(DispatcherPriority.Normal, root.Dispatcher) { Interval = options.PollInterval };
        _step = new DispatcherTimer(DispatcherPriority.Normal, root.Dispatcher) { Interval = options.StepDuration };
        _poll.Tick += (_, _) => OnPoll();
        _step.Tick += (_, _) => Next();
        _onMouseDown = OnMouseDown;
        _onKeyDown   = OnKeyDown;
    }

    /// <summary>A lasso was thrown: the step's index in the chain, and the control it is round.</summary>
    public event Action<int, FrameworkElement>? StepShown;

    /// <summary>Completes, on the UI thread, when the last step is done or the tour is stopped.</summary>
    public Task<LocateResult> Completion => _done.Task;

    /// <summary>
    /// Lassoes <paramref name="ids"/> in turn, under <paramref name="root"/> — a window, or any element with an adorner
    /// layer above it. Stops a tour already running there.
    /// </summary>
    public static LocateTour Start(FrameworkElement root, IReadOnlyList<string> ids, LocateOptions? options = null)
    {
        if (Running.TryGetValue(root, out var running)) running.Cancel();

        var tour = new LocateTour(root, ids, options ?? new LocateOptions());
        Running.AddOrUpdate(root, tour);
        // After the click that asked for it, and after the layout that click caused: a tour that began here would
        // count the reader's own click as the one dismissing its first lasso, and would look for a control the click
        // is still opening.
        root.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(tour.Begin));
        return tour;
    }

    /// <summary>Takes the lasso down and ends the tour.</summary>
    public void Cancel() => Finish(cancelled: true);

    private void Begin()
    {
        if (_done.Task.IsCompleted) return;   // stopped before it started

        if (_layer is null || _ids.Count == 0)
        {
            _missing.AddRange(_ids);   // nowhere to draw: nothing can be shown
            Finish(cancelled: false);
            return;
        }

        _root.AddHandler(UIElement.PreviewMouseDownEvent, _onMouseDown, handledEventsToo: true);
        _root.AddHandler(UIElement.PreviewKeyDownEvent, _onKeyDown, handledEventsToo: true);
        Next();
    }

    // On to the next id: lassoed now if it is on screen, else looked for until AppearWithin runs out.
    private void Next()
    {
        EndStep();
        if (++_index >= _ids.Count)
        {
            Finish(cancelled: false);
            return;
        }

        _since = Stopwatch.GetTimestamp();
        if (!TryShow()) _poll.Start();
    }

    private bool TryShow()
    {
        if (AutomationIdLookup.Find(_root, _ids[_index], _options.SearchLast) is not { } target) return false;

        target.BringIntoView();
        _target = target;
        _lasso  = new LassoAdorner(target, _index + 1, _ids.Count);
        _layer!.Add(_lasso);
        _shown++;
        _since = Stopwatch.GetTimestamp();

        _step.Start();
        _poll.Start();   // now watching that it stays on screen
        StepShown?.Invoke(_index, target);
        return true;
    }

    private void OnPoll()
    {
        if (_target is not null)
        {
            // Its tab closed, its panel collapsed: a lasso round something no longer there points at nothing.
            if (!AutomationIdLookup.IsShown(_target, _root)) Next();
            return;
        }

        if (TryShow()) return;
        if (Stopwatch.GetElapsedTime(_since) < _options.AppearWithin) return;

        _missing.Add(_ids[_index]);
        Next();
    }

    // Never handled: the click goes where the reader aimed it, and only moves the tour on.
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_target is null || Stopwatch.GetElapsedTime(_since) < _options.ClickGrace) return;
        Next();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;   // here Esc means "stop pointing", not whatever else it would have done
        Cancel();
    }

    private void EndStep()
    {
        _step.Stop();
        _poll.Stop();
        _lasso?.Dismiss(_layer!);
        _lasso  = null;
        _target = null;
    }

    private void Finish(bool cancelled)
    {
        if (_done.Task.IsCompleted) return;

        EndStep();
        _root.RemoveHandler(UIElement.PreviewMouseDownEvent, _onMouseDown);
        _root.RemoveHandler(UIElement.PreviewKeyDownEvent, _onKeyDown);
        if (Running.TryGetValue(_root, out var running) && ReferenceEquals(running, this)) Running.Remove(_root);

        _done.TrySetResult(new LocateResult(_shown, _missing.ToArray(), cancelled));
    }
}
