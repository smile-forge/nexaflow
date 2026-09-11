using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>How a <see cref="ModalCard"/>'s border reads.</summary>
public enum ModalTone
{
    /// <summary>An ordinary form.</summary>
    Neutral,

    /// <summary>A form that should draw the eye.</summary>
    Accent,

    /// <summary>A form whose confirm destroys something.</summary>
    Danger,
}

/// <summary>
/// The shared chrome of a tab-modal form: a scrim over the area it covers (it swallows clicks, so nothing
/// behind it can be reached) and a centred, themed card holding the <see cref="HeaderedContentControl.Header"/>
/// and the form. A <i>question</i> ΓÇö yes/no, one line of text ΓÇö is not a form: ask it through
/// <c>IShellServices.ConfirmAsync</c> / <c>ShowPrompt</c>, which are window-modal (arch review ┬ºE1).
/// <para>
/// Only the chrome is shared — <see cref="Control.Background"/> is the card's (the theme's surface unless a form was designed on another); the scrim is always the theme's <c>ScrimBrush</c>. The form's content, bindings and AutomationIds stay the feature's own, so a
/// journey clicks exactly what it clicked before. Put one in the view's root grid spanning the area it blocks,
/// bind <see cref="IsOpen"/>, and hand it the form's cancel command so Escape backs out:
/// <code>&lt;ctrl:ModalCard Grid.RowSpan="2" Header="Take snapshot" CardWidth="360"
///     IsOpen="{Binding TakeSnapshotVisible}" CancelCommand="{Binding CancelTakeSnapshotCommand}"&gt;
///     ΓÇªthe formΓÇª
/// &lt;/ctrl:ModalCard&gt;</code>
/// </para>
/// </summary>
public class ModalCard : HeaderedContentControl
{
    private readonly KeyBinding _escape = new() { Key = Key.Escape };

    static ModalCard()
        => DefaultStyleKeyProperty.OverrideMetadata(typeof(ModalCard), new FrameworkPropertyMetadata(typeof(ModalCard)));

    public ModalCard() => InputBindings.Add(_escape);

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(ModalCard),
        new FrameworkPropertyMetadata(false, (d, e) => ((ModalCard)d).OnIsOpenChanged((bool)e.NewValue)));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(ModalTone), typeof(ModalCard), new FrameworkPropertyMetadata(ModalTone.Neutral));

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand), typeof(ICommand), typeof(ModalCard),
        new FrameworkPropertyMetadata(null, (d, e) => ((ModalCard)d)._escape.Command = (ICommand?)e.NewValue));

    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(ModalCard), new FrameworkPropertyMetadata(double.NaN));

    public static readonly DependencyProperty CardMinWidthProperty = DependencyProperty.Register(
        nameof(CardMinWidth), typeof(double), typeof(ModalCard), new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty CardMaxWidthProperty = DependencyProperty.Register(
        nameof(CardMaxWidth), typeof(double), typeof(ModalCard), new FrameworkPropertyMetadata(double.PositiveInfinity));

    public static readonly DependencyProperty CardMaxHeightProperty = DependencyProperty.Register(
        nameof(CardMaxHeight), typeof(double), typeof(ModalCard), new FrameworkPropertyMetadata(double.PositiveInfinity));

    /// <summary>Shown while true; collapsed otherwise, so a closed card takes no layout and blocks nothing.</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>The card's border ΓÇö <see cref="ModalTone.Danger"/> for a form whose confirm destroys something.</summary>
    public ModalTone Tone
    {
        get => (ModalTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>What Escape runs ΓÇö normally the command the form's own Cancel button runs.</summary>
    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    /// <summary>The card's width; unset sizes it to its content.</summary>
    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CardMinWidth
    {
        get => (double)GetValue(CardMinWidthProperty);
        set => SetValue(CardMinWidthProperty, value);
    }

    public double CardMaxWidth
    {
        get => (double)GetValue(CardMaxWidthProperty);
        set => SetValue(CardMaxWidthProperty, value);
    }

    /// <summary>Caps the card's height; a form that can grow (a list, a tree) scrolls inside it.</summary>
    public double CardMaxHeight
    {
        get => (double)GetValue(CardMaxHeightProperty);
        set => SetValue(CardMaxHeightProperty, value);
    }

    // Focus moves into the card as it opens: Escape reaches the card only from inside it, and a keyboard
    // user should not have to tab through the page it now covers. Skipped when focus is already inside, so a
    // form that focuses its own first box keeps it.
    private void OnIsOpenChanged(bool open)
    {
        if (!open) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (IsOpen && !IsKeyboardFocusWithin)
                MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }, DispatcherPriority.Input);
    }
}
