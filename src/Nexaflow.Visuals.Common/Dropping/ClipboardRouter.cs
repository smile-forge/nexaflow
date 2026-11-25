using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Nexaflow.Interaction;

namespace Nexaflow.Visuals.Common.Dropping;

/// <summary>
/// The clipboard as a window offers it: to every element in it, and to whatever a paste in it should land in.
/// <para>
/// <b>Reaching it.</b> A window carries the application's clipboard as <see cref="ClipboardProperty"/>, inherited by everything
/// it shows, popups included, so a control with no view-model to ask still copies (<see cref="Write"/>) and reads through the
/// one clipboard — never through the toolkit's own, and never through a static.
/// </para>
/// <para>
/// <b>Paste.</b> In a window this is attached to (<see cref="Attach"/>), Ctrl+V, Shift+Insert and the Paste command go to the
/// <see cref="IPasteTarget"/> nearest where the paste was asked for that can take what the clipboard holds — found as a drop
/// target is, from the focused element (or the control that raised the command) out, the shell's pages standing for their
/// view-models — and failing one there, the active page's own. It is handed what the clipboard held of its kinds, taken. A text
/// box, or any control with a Paste command of its own, pastes for itself: a paste begun in one is never taken from it.
/// </para>
/// </summary>
public sealed class ClipboardRouter
{
    /// <summary>The application's clipboard, inherited by everything a window shows.</summary>
    public static readonly DependencyProperty ClipboardProperty = DependencyProperty.RegisterAttached(
        "Clipboard", typeof(IShellClipboard), typeof(ClipboardRouter),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static IShellClipboard? GetClipboard(DependencyObject element) => (IShellClipboard?)element.GetValue(ClipboardProperty);

    public static void SetClipboard(DependencyObject element, IShellClipboard? value) => element.SetValue(ClipboardProperty, value);

    private readonly UIElement _root;
    private readonly IShellClipboard _clipboard;
    private readonly Action<Exception> _failed;
    private readonly Func<DependencyObject, IPasteTarget?>? _targetOf;
    private readonly Func<IPasteTarget?>? _pageTarget;

    private ClipboardRouter(UIElement root, IShellClipboard clipboard, Action<Exception> failed,
                            Func<DependencyObject, IPasteTarget?>? targetOf, Func<IPasteTarget?>? pageTarget)
    {
        _root       = root;
        _clipboard  = clipboard;
        _failed     = failed;
        _targetOf   = targetOf;
        _pageTarget = pageTarget;

        SetClipboard(root, clipboard);

        // Bindings on the root: reached only by a key, or a command, that nothing nearer the focus took.
        root.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPaste, OnCanPaste));
        root.InputBindings.Add(new KeyBinding(ApplicationCommands.Paste, Key.V, ModifierKeys.Control));
        root.InputBindings.Add(new KeyBinding(ApplicationCommands.Paste, Key.Insert, ModifierKeys.Shift));
    }

    /// <summary>Routes the pastes in <paramref name="root"/>, and offers <paramref name="clipboard"/> to everything in it.</summary>
    /// <param name="root">A window, or the root of what one shows.</param>
    /// <param name="clipboard">The clipboard read and written.</param>
    /// <param name="failed">Told what a target threw while taking a paste; nothing may escape into the input system.</param>
    /// <param name="targetOf">A target an element stands for without being one — the shell's pages name their view-models so.</param>
    /// <param name="pageTarget">The active page's own target, asked when nothing nearer the focus can take the paste.</param>
    public static ClipboardRouter Attach(UIElement root, IShellClipboard clipboard, Action<Exception> failed,
                                         Func<DependencyObject, IPasteTarget?>? targetOf = null, Func<IPasteTarget?>? pageTarget = null)
        => new(root, clipboard, failed, targetOf, pageTarget);

    /// <summary>
    /// Offers the application's clipboard to every window as it loads — a dialog, a tool window — as well as those
    /// <see cref="Attach"/> routes, so what any of them shows can copy and paste. <paramref name="clipboard"/> is asked each time,
    /// so it may be made after this is called; a window loaded before then is offered none.
    /// </summary>
    public static void ServeEveryWindow(Func<IShellClipboard?> clipboard)
        => EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window && GetClipboard(window) is null && clipboard() is { } offered) SetClipboard(window, offered);
        }));

    /// <summary>
    /// Puts <paramref name="payload"/> on the clipboard <paramref name="source"/> reaches: for a control with no view-model to
    /// ask. Whether it was written: false when another application held the clipboard open, or <paramref name="source"/> is
    /// shown nowhere that offers one.
    /// </summary>
    public static Task<bool> Write(DependencyObject source, IDragPayload payload)
        => GetClipboard(source)?.WriteAsync(payload) ?? Task.FromResult(false);

    private void OnCanPaste(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = TargetFor(e.OriginalSource as DependencyObject) is not null;
        e.Handled = true;
    }

    private void OnPaste(object sender, ExecutedRoutedEventArgs e)
    {
        if (TargetFor(e.OriginalSource as DependencyObject) is not { } target) return;
        e.Handled = true;
        _ = PasteAsync(target);
    }

    private async Task PasteAsync(IPasteTarget target)
    {
        try
        {
            var pasted = await _clipboard.ReadAsync([.. target.PasteKinds]);
            if (target.PasteKinds.Any(pasted.Contains)) await target.PasteAsync(pasted);
        }
        catch (Exception failure)
        {
            _failed(failure);
        }
    }

    /// <summary>
    /// The target nearest <paramref name="origin"/> that can take what the clipboard holds; the active page's when nothing nearer
    /// can; none when the paste began in a surface that pastes for itself.
    /// </summary>
    internal IPasteTarget? TargetFor(DependencyObject? origin)
    {
        IPasteTarget? last = null;
        for (var at = origin; at is not null && !ReferenceEquals(at, _root); at = DropHitTesting.Parent(at))
        {
            if (at is IPasteTarget self && Takes(self, ref last)) return self;
            if (_targetOf?.Invoke(at) is { } stood && Takes(stood, ref last)) return stood;
            if (PastesItself(at)) return null;
        }
        return _pageTarget?.Invoke() is { } page && Takes(page, ref last) ? page : null;
    }

    private bool Takes(IPasteTarget target, ref IPasteTarget? last)
    {
        if (ReferenceEquals(target, last)) return false;
        last = target;
        return target.PasteKinds.Any(_clipboard.Kinds.Contains);
    }

    /// <summary>A text box, or a control that binds the Paste command itself — an editor — takes its own pastes.</summary>
    private static bool PastesItself(DependencyObject element)
        => element is TextBoxBase
        || element is UIElement { CommandBindings.Count: > 0 } control
           && control.CommandBindings.OfType<CommandBinding>().Any(binding => binding.Command == ApplicationCommands.Paste);
}
