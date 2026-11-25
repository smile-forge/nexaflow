using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using System.Windows.Threading;
using Nexaflow.Interaction;

namespace Nexaflow.Visuals.Common.Dropping;

/// <summary>
/// Every drag over a window, passed to the <see cref="IDropTarget"/> it is over. One per window, and the only code
/// that answers Windows for one.
/// <para>
/// <b>The innermost surface wins, whether it is a target or a control's own handling.</b> On the way down (preview) a
/// target is asked only where no control with drop handling of its own — a text box, a list that reorders — is nearer
/// the pointer. On the way back up, a drop such a control took stands; one it refused or ignored is put to every target
/// around it.
/// </para>
/// <para>
/// What is told to the source is decided here too. Left alone, WPF answers a hover nobody handled with <i>Move</i>, and
/// a drop nobody set an effect on with everything the source allowed — Move among it, which is leave for the source to
/// delete what was dragged. So a drag from elsewhere that nothing takes is refused, and one taken says the one thing
/// that was done.
/// </para>
/// </summary>
public sealed class DropRouter
{
    private readonly record struct Found(IDropTarget Target, DropRequest Request, DropOffer Offer, DropEffect Effect);

    private readonly UIElement _root;
    private readonly Func<DependencyObject, IDropTarget?>? _targetOf;
    private readonly Action<Exception> _failed;
    private readonly DropHint _hint;

    private WindowsDragPayload? _payload;
    private IDropTarget? _hovered, _faulted;
    private bool _rightButton;
    private long _moves;

    private DropRouter(UIElement root, Action<Exception> failed, Func<DependencyObject, IDropTarget?>? targetOf)
    {
        _root     = root;
        _failed   = failed;
        _targetOf = targetOf;
        _hint     = new DropHint(root);

        root.AllowDrop = true;   // inherited, so every surface in it is heard

        root.AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(OnPreviewHover));
        root.AddHandler(DragDrop.PreviewDragOverEvent,  new DragEventHandler(OnPreviewHover));
        root.AddHandler(DragDrop.PreviewDropEvent,      new DragEventHandler(OnPreviewDrop));

        // Handled events too: a text box marks a file it refuses as handled, and that refusal is not the window's.
        root.AddHandler(DragDrop.DragEnterEvent, new DragEventHandler(OnHover), handledEventsToo: true);
        root.AddHandler(DragDrop.DragOverEvent,  new DragEventHandler(OnHover), handledEventsToo: true);
        root.AddHandler(DragDrop.DropEvent,      new DragEventHandler(OnDrop),  handledEventsToo: true);
        root.AddHandler(DragDrop.DragLeaveEvent, new DragEventHandler(OnLeave), handledEventsToo: true);
    }

    /// <summary>Routes the drags over <paramref name="root"/>.</summary>
    /// <param name="root">A window, or the root of what one shows.</param>
    /// <param name="failed">Told what a target threw; nothing may escape into Windows' callback.</param>
    /// <param name="targetOf">A target an element stands for without being one — the shell's pages name their view-models so.</param>
    public static DropRouter Attach(UIElement root, Action<Exception> failed, Func<DependencyObject, IDropTarget?>? targetOf = null)
        => new(root, failed, targetOf);

    private void OnPreviewHover(object sender, DragEventArgs e)
    {
        _moves++;
        _rightButton = e.KeyStates.HasFlag(DragDropKeyStates.RightMouseButton);

        if (FindTarget(e, PayloadOf(e.Data), untilOwnSurface: true) is { } found) Offer(e, found);
    }

    private void OnHover(object sender, DragEventArgs e)
    {
        if (e.Handled && e.Effects != DragDropEffects.None)
        {
            Hovering(null);   // a control's own
            return;
        }

        var payload = PayloadOf(e.Data);
        if (FindTarget(e, payload, untilOwnSurface: false) is { } found)
        {
            Offer(e, found);
            return;
        }

        Hovering(null);
        if (!e.Handled && (payload.IsShareable() || !OverOwnSurface(e))) Refuse(e);
    }

    private void Offer(DragEventArgs e, in Found found)
    {
        Hovering(found.Target);
        e.Effects = ToWpf(found.Effect);
        e.Handled = true;
        _hint.Show(found.Effect == DropEffect.None ? null : found.Offer.Description, e.GetPosition(_root));
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        var payload = PayloadOf(e.Data);
        if (FindTarget(e, payload, untilOwnSurface: true) is not { } found) return;

        Accept(e, found);
        End();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var payload = PayloadOf(e.Data);

        if (e.Handled && e.Effects != DragDropEffects.None)
        {
            // Several effects at once is WPF's default left standing, not an answer.
            if (payload.IsShareable() && !IsOne(e.Effects))
                e.Effects = e.Effects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else if (FindTarget(e, payload, untilOwnSurface: false) is { } found)
        {
            Accept(e, found);
        }
        else if (payload.IsShareable())
        {
            Refuse(e);
        }

        End();
    }

    private void Accept(DragEventArgs e, in Found found)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (found.Effect == DropEffect.None) return;

        // What was promised has to be what the target reads: a move the source does not allow is a copy.
        var request = found.Offer.Effect == DropEffect.Move && found.Effect != DropEffect.Move
            ? found.Request with { Modifiers = found.Request.Modifiers & ~KeyModifiers.Shift }
            : found.Request;

        _hint.Hide();
        try
        {
            // Read in full here, while Windows still holds the drag, so the target may take its time over what it was given.
            var taken = request.Payload.Take(found.Target.AcceptedKinds, DropStaging.Default);

            // A target that fails before it first waits did nothing, and the source is told so; one that fails later is reported
            // once it does, the drop long since answered.
            var accepting = found.Target.AcceptDropAsync(request with { Payload = taken });
            if (accepting.IsFaulted)
            {
                _failed(accepting.Exception!.GetBaseException());
                return;
            }

            e.Effects = ToWpf(Told(found.Effect, found.Request.Payload));
            _ = ReportLateFailureAsync(accepting);
        }
        catch (Exception failure)
        {
            _failed(failure);
        }
    }

    private async Task ReportLateFailureAsync(Task accepting)
    {
        try { await accepting; }
        catch (Exception failure) { _failed(failure); }
    }

    /// <summary>What the source is told of a drop: never that its files were moved, which is leave to delete them.</summary>
    internal static DropEffect Told(DropEffect done, IDragPayload payload)
        => done == DropEffect.Move && payload.CarriesFiles() ? DropEffect.None : done;

    private void OnLeave(object sender, DragEventArgs e)
    {
        // Windows reports a leave at every element crossed. It is over only if no move follows.
        var moves = ++_moves;
        _root.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (moves == _moves) End();
        });
    }

    private void End()
    {
        _moves++;
        Hovering(null);
        _hint.Hide();
        _payload?.Release();
        _payload = null;
        _faulted = null;
    }

    private void Hovering(IDropTarget? target)
    {
        if (ReferenceEquals(_hovered, target)) return;

        var left = _hovered;
        _hovered = target;
        if (target is null) _hint.Hide();
        if (left is null) return;

        try { left.EndDropHover(); }
        catch (Exception failure) { _failed(failure); }
    }

    private static void Refuse(DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private WindowsDragPayload PayloadOf(IDataObject data)
    {
        if (_payload?.Wraps(data) == true) return _payload;
        _payload?.Release();
        return _payload = new WindowsDragPayload(data);
    }

    // ── Finding who is asked ─────────────────────────────────────────────────

    private Found? FindTarget(DragEventArgs e, IDragPayload payload, bool untilOwnSurface)
    {
        IDropTarget? last = null;
        for (var at = e.OriginalSource as DependencyObject; at is not null; at = DropHitTesting.Parent(at))
        {
            if (DropSurface.GetTarget(at) is { } given && Ask(given, at, e, payload, ref last) is { } byGiven) return byGiven;
            if (at is IDropTarget self && Ask(self, at, e, payload, ref last) is { } bySelf) return bySelf;
            if (_targetOf?.Invoke(at) is { } stood && Ask(stood, at, e, payload, ref last) is { } byStood) return byStood;

            if (ReferenceEquals(at, _root) || (untilOwnSurface && HandlesDropsItself(at))) break;
        }
        return null;
    }

    private Found? Ask(IDropTarget target, DependencyObject on, DragEventArgs e, IDragPayload payload, ref IDropTarget? last)
    {
        if (ReferenceEquals(target, last)) return null;
        last = target;

        try
        {
            if (!payload.ContainsAny(target.AcceptedKinds)) return null;

            var point = on is IInputElement element ? e.GetPosition(element) : e.GetPosition(_root);
            var request = new DropRequest(payload, new DropPoint(point.X, point.Y), ModifiersOf(e.KeyStates), _rightButton);
            return target.OfferDrop(request) is { } offer
                ? new Found(target, request, offer, Allowed(offer.Effect, e.AllowedEffects, payload.IsShareable()))
                : null;
        }
        catch (Exception failure)
        {
            if (!ReferenceEquals(_faulted, target)) _failed(failure);   // once, not on every move of the pointer
            _faulted = target;
            return null;
        }
    }

    /// <summary>
    /// The effect nearest <paramref name="wanted"/> that the source allows. One that allows only a move is not given a
    /// copy's answer, unless the drag is the app's own — a tab, which loses nothing by it.
    /// </summary>
    internal static DropEffect Allowed(DropEffect wanted, DragDropEffects allowed, bool shareable)
    {
        bool copy = allowed.HasFlag(DragDropEffects.Copy), move = allowed.HasFlag(DragDropEffects.Move), link = allowed.HasFlag(DragDropEffects.Link);
        var fallback = !shareable && move ? DropEffect.Move : DropEffect.None;

        return wanted switch
        {
            DropEffect.Copy => copy ? DropEffect.Copy : link ? DropEffect.Link : fallback,
            DropEffect.Move => move ? DropEffect.Move : copy ? DropEffect.Copy : DropEffect.None,
            DropEffect.Link => link ? DropEffect.Link : copy ? DropEffect.Copy : fallback,
            _               => DropEffect.None,
        };
    }

    private bool OverOwnSurface(DragEventArgs e)
    {
        for (var at = e.OriginalSource as DependencyObject; at is not null && !ReferenceEquals(at, _root); at = DropHitTesting.Parent(at))
            if (HandlesDropsItself(at)) return true;
        return false;
    }

    private bool HandlesDropsItself(DependencyObject at)
    {
        if (ReferenceEquals(at, _root)) return false;
        if (at is TextBoxBase or PasswordBox) return true;
        if (at is not UIElement { AllowDrop: true }) return false;

        var source = DependencyPropertyHelper.GetValueSource(at, UIElement.AllowDropProperty).BaseValueSource;
        return source is not (BaseValueSource.Default or BaseValueSource.Inherited);
    }

    private static KeyModifiers ModifiersOf(DragDropKeyStates keys)
    {
        var held = KeyModifiers.None;
        if (keys.HasFlag(DragDropKeyStates.AltKey))     held |= KeyModifiers.Alt;
        if (keys.HasFlag(DragDropKeyStates.ControlKey)) held |= KeyModifiers.Control;
        if (keys.HasFlag(DragDropKeyStates.ShiftKey))   held |= KeyModifiers.Shift;
        return held;
    }

    private static bool IsOne(DragDropEffects effects)
        => effects is DragDropEffects.Copy or DragDropEffects.Move or DragDropEffects.Link;

    private static DragDropEffects ToWpf(DropEffect effect) => effect switch
    {
        DropEffect.Copy => DragDropEffects.Copy,
        DropEffect.Move => DragDropEffects.Move,
        DropEffect.Link => DragDropEffects.Link,
        _               => DragDropEffects.None,
    };
}
