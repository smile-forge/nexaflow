using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Common;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Multi-line TextBox with placeholder hint, local spell-checking and Enter-to-send.
/// The placeholder is drawn on top of the text area when empty and unfocused.
/// When focused with text present, <see cref="CompletionText"/> is drawn as grey
/// "ghost" text after the caret; Tab accepts it. Enter (no Shift) fires SendCommand;
/// Shift+Enter inserts a newline.
/// </summary>
public class PlaceholderTextBox : TextBox
{
    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PlaceholderTextBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SendCommandProperty =
        DependencyProperty.Register(nameof(SendCommand), typeof(ICommand), typeof(PlaceholderTextBox));

    public static readonly DependencyProperty CompletionTextProperty =
        DependencyProperty.Register(nameof(CompletionText), typeof(string), typeof(PlaceholderTextBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }
    public ICommand? SendCommand
    {
        get => (ICommand?)GetValue(SendCommandProperty);
        set => SetValue(SendCommandProperty, value);
    }

    /// <summary>Inline completion remainder drawn after the caret; Tab accepts it.</summary>
    public string? CompletionText
    {
        get => (string?)GetValue(CompletionTextProperty);
        set => SetValue(CompletionTextProperty, value);
    }

    /// <summary>
    /// Optional hook consulted on each key press before the built-in handling, so the active page's
    /// feature can claim a key (e.g. the terminal's Up/Down history, Tab path completion). Wired by the
    /// shell window; receives (key, modifiers, current text, caret) and returns whether it handled the
    /// key and any replacement text. A non-empty <see cref="CompletionText"/> still wins Tab first.
    /// </summary>
    public Func<Key, ModifierKeys, string, int, ChatKeyResult>? KeyInterceptor { get; set; }

    // ── Constructor ───────────────────────────────────────────────────────

    public PlaceholderTextBox()
    {
        AcceptsReturn  = true;   // Shift+Enter / paste insert newlines; bare Enter sends (see OnPreviewKeyDown)
        TextWrapping   = TextWrapping.Wrap;
        SpellCheck.IsEnabled = true;   // local WPF spell-checker (red squiggle)
        Language       = System.Windows.Markup.XmlLanguage.GetLanguage(
                             System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        PreviewKeyDown += OnPreviewKeyDown;
        TextChanged    += (_, _) => InvalidateVisual();
        GotFocus       += (_, _) => InvalidateVisual();
        LostFocus      += (_, _) => InvalidateVisual();
Loaded         += OnLoaded;
Unloaded       += OnUnloaded;
    }

    // ── Rendering: placeholder (empty/unfocused) + ghost completion ───────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var dpi      = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Parked counts as focused throughout: the box still owns the input, it just isn't
        // holding keyboard focus while the user is away (see "Idle caret parking" below).
        bool focused = IsFocused || _parked;

        // Placeholder - only when empty and unfocused.
        if (string.IsNullOrEmpty(Text) && !focused)
        {
            var ph = new FormattedText(
                Placeholder,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface, FontSize,
                new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x70)), dpi);

            ph.MaxTextWidth  = Math.Max(1, ActualWidth  - Padding.Left - Padding.Right);
            ph.MaxTextHeight = Math.Max(1, ActualHeight - Padding.Top  - Padding.Bottom);
            dc.DrawText(ph, new Point(Padding.Left, Padding.Top));
            return;
        }

        // Parked: WPF's caret went with the keyboard focus, so stand in for it. Static - drawing
        // it here rather than animating it is the entire point.
        if (_parked)
        {
            var caretRect = GetRectFromCharacterIndex(Math.Min(_parkedCaret, Text.Length));
            if (!caretRect.IsEmpty)
                dc.DrawRectangle(CaretBrush ?? Foreground, null,
                    new Rect(caretRect.X, caretRect.Top, SystemParameters.CaretWidth, caretRect.Height));
        }

        // Ghost completion - focused, text present, suggestion available.
        if (focused && !string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(CompletionText))
        {
            var caret = GetRectFromCharacterIndex(Text.Length);
            if (!caret.IsEmpty)
            {
                var ghost = new FormattedText(
                    CompletionText,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, FontSize,
                    new SolidColorBrush(Color.FromRgb(0x6A, 0x72, 0x90)), dpi);
                dc.DrawText(ghost, new Point(caret.X, caret.Top));
            }
        }
    }

    // ── Spell-check context menu ──────────────────────────────────────────
    // WPF's built-in editing context menu ignores our themed ContextMenu/MenuItem
    // styles (it uses internal EditorContextMenu/EditorMenuItem types), so it renders
    // with default light chrome. We build our own menu from regular ContextMenu/MenuItem
    // instances, which DO pick up the global styles, and populate spelling suggestions
    // via the SpellingError API.

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        ContextMenu = BuildSpellContextMenu();
        base.OnContextMenuOpening(e);
    }

    private ContextMenu BuildSpellContextMenu()
    {
        var menu = new ContextMenu();

        var index = ResolveSpellIndex();
        var error = index >= 0 ? GetSpellingError(index) : null;

        if (error is not null)
        {
            var hasSuggestion = false;
            foreach (var suggestion in error.Suggestions)
            {
                hasSuggestion = true;
                var pick = suggestion;
                var item = new MenuItem { Header = pick, FontWeight = FontWeights.SemiBold };
                item.Click += (_, _) => error.Correct(pick);
                menu.Items.Add(item);
            }

            if (!hasSuggestion)
                menu.Items.Add(new MenuItem { Header = "No suggestions", IsEnabled = false });

            menu.Items.Add(new Separator());
            var ignore = new MenuItem { Header = "Ignore All" };
            ignore.Click += (_, _) => error.IgnoreAll();
            menu.Items.Add(ignore);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(CommandItem("Cut",        ApplicationCommands.Cut));
        menu.Items.Add(CommandItem("Copy",       ApplicationCommands.Copy));
        menu.Items.Add(CommandItem("Paste",      ApplicationCommands.Paste));
        menu.Items.Add(new Separator());
        menu.Items.Add(CommandItem("Select All", ApplicationCommands.SelectAll));

        return menu;
    }

    private MenuItem CommandItem(string header, RoutedUICommand command)
        => new() { Header = header, Command = command, CommandTarget = this };

    /// <summary>Character index under the mouse (for right-click), falling back to the caret.</summary>
    private int ResolveSpellIndex()
    {
        var idx = GetCharacterIndexFromPoint(Mouse.GetPosition(this), snapToText: true);
        return idx >= 0 ? idx : CaretIndex;
    }

    // ── Key handling: Tab accepts completion, Enter sends ─────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && !string.IsNullOrEmpty(CompletionText))
        {
            var suggestion = CompletionText!;
            CompletionText = null;
            AppendText(suggestion);
            CaretIndex = Text.Length;
            e.Handled = true;
            return;
        }

        // The active page's feature gets first crack at the remaining keys (terminal Up/Down history,
        // Tab path completion when there's no ghost to accept).
        if (KeyInterceptor is not null)
        {
            var result = KeyInterceptor(e.Key, Keyboard.Modifiers, Text, CaretIndex);
            if (result.Handled)
            {
                if (result.NewText is not null)
                {
                    Text       = result.NewText;
                    CaretIndex = result.NewCaretIndex < 0
                        ? Text.Length
                        : Math.Min(result.NewCaretIndex, Text.Length);
                }
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter
            && !Keyboard.IsKeyDown(Key.LeftShift)
            && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            if (SendCommand?.CanExecute(null) == true)
                SendCommand.Execute(null);
        }
    }

    // ── Idle caret parking ────────────────────────────────────────────────
    //
    // WPF draws its caret as a CaretElement in the adorner layer and blinks it with a
    // RepeatBehavior.Forever animation on the element's Opacity. Any live animation clock holds
    // MediaContext in per-frame render mode, so one focused text box makes the WHOLE window
    // re-render ~35x a second forever - measured at 0.5-0.9% of a core with the shell otherwise
    // completely idle, and it is the only thing the idle shell was doing.
    //
    // Nothing suppresses that per-control. CaretBrush=Transparent, IsReadOnly +
    // IsReadOnlyCaretVisible=false and user32 HideCaret were all measured and all leave the clock
    // running (HideCaret hides the *Win32* caret, which WPF keeps only as an invisible stub for
    // accessibility and IME positioning - it is not what you see). SetCaretBlinkTime does stop it
    // but is window-station-global, so an app calling it would silently override the user's own
    // accessibility preference everywhere. Dropping keyboard focus is the only lever left.
    //
    // So: once the USER (not the app) has been idle for IdleThreshold, park keyboard focus on
    // FocusPark and draw a static caret in OnRender instead. No clock, no frames, 0.00%. The first
    // sign of life hands focus straight back - a keystroke, a click in the box, the window being
    // activated, or the poll seeing fresh input.

    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleThreshold    = TimeSpan.FromMinutes(2);

    private DispatcherTimer? _idleTimer;
    private Window?          _window;
    private bool             _parked;
    private int              _parkedCaret;

    public static readonly DependencyProperty FocusParkProperty =
        DependencyProperty.Register(nameof(FocusPark), typeof(IInputElement), typeof(PlaceholderTextBox));

    /// <summary>
    /// A focusable element that keyboard focus is parked on while the user is idle. It must sit
    /// OUTSIDE this control - focus anywhere within the box keeps WPF's caret alive - and it must
    /// not be the Window, which delegates focus straight back here. Unset disables idle parking.
    /// </summary>
    public IInputElement? FocusPark
    {
        get => (IInputElement?)GetValue(FocusParkProperty);
        set => SetValue(FocusParkProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can fire again after a re-parent; only wire up once.
        if (_idleTimer is not null) { _idleTimer.Start(); return; }

        _window = Window.GetWindow(this);
        if (_window is not null) _window.Activated += OnWindowActivated;

        _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = IdlePollInterval };
        _idleTimer.Tick += OnIdleTick;
        _idleTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _idleTimer?.Stop();
        Unpark(refocus: false);
    }

    private void OnIdleTick(object? sender, EventArgs e)
    {
        if (SystemIdleTime() >= IdleThreshold) Park();
        else                                   Unpark(refocus: true);
    }

    /// <summary>The user came back to the window - always give the caret back.</summary>
    private void OnWindowActivated(object? sender, EventArgs e) => Unpark(refocus: true);

    private void Park()
    {
        // Only meaningful while we actually hold focus; otherwise there is no caret to stop.
        if (_parked || FocusPark is null || !IsKeyboardFocusWithin) return;

        // The park target sits at Focusable=false the rest of the time so it can never become
        // the window's default or tab focus; it is a focus candidate only while we are parked.
        FocusPark.Focusable = true;

        _parkedCaret = CaretIndex;
        _parked      = true;                       // set before moving focus: the LostFocus
        Keyboard.Focus(FocusPark);                 // re-render must already see the parked state

        if (IsKeyboardFocusWithin)
        {
            // The target refused the focus (collapsed, detached, ...). Leave everything as it
            // was rather than sitting half-parked, drawing a caret on top of a real one.
            _parked = false;
            FocusPark.Focusable = false;
            return;
        }

        if (_window is not null) _window.PreviewKeyDown += OnWindowKeyWhileParked;
        InvalidateVisual();
    }

    private void Unpark(bool refocus)
    {
        if (!_parked) return;

        _parked = false;
        if (_window is not null) _window.PreviewKeyDown -= OnWindowKeyWhileParked;

        if (refocus)
        {
            Focus();
            CaretIndex = Math.Min(_parkedCaret, Text.Length);
        }

        if (FocusPark is not null) FocusPark.Focusable = false;
        InvalidateVisual();
    }

    /// <summary>
    /// While parked the box has no focus, so keystrokes arrive at the window instead. Take focus
    /// back on the first one - WPF raises TextInput after this tunnels, so the character that woke
    /// us still lands in the box rather than being swallowed.
    /// </summary>
    private void OnWindowKeyWhileParked(object sender, KeyEventArgs e)
    {
        // A shortcut (Ctrl+W, Alt+Tab) is not the user coming back to type at us; let it through
        // untouched rather than stealing focus into the input box.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            return;

        Unpark(refocus: true);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        // Unpark without refocusing: the click itself focuses us and puts the caret where it landed.
        Unpark(refocus: false);
        base.OnPreviewMouseDown(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>
    /// Time since the last system-wide keyboard/mouse input. System-wide is what we want: the
    /// user typing in another app is still "here", and should not come back to a dead caret.
    /// </summary>
    private static TimeSpan SystemIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // Both are GetTickCount values, so unsigned subtraction stays correct across the ~49-day wrap.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }
}
