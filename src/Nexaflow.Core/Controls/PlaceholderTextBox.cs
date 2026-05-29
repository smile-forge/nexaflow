using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    // ── Constructor ───────────────────────────────────────────────────────

    public PlaceholderTextBox()
    {
        AcceptsReturn  = false;
        TextWrapping   = TextWrapping.Wrap;
        SpellCheck.IsEnabled = true;   // local WPF spell-checker (red squiggle)
        Language       = System.Windows.Markup.XmlLanguage.GetLanguage(
                             System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        PreviewKeyDown += OnPreviewKeyDown;
        TextChanged    += (_, _) => InvalidateVisual();
        GotFocus       += (_, _) => InvalidateVisual();
        LostFocus      += (_, _) => InvalidateVisual();
    }

    // ── Rendering: placeholder (empty/unfocused) + ghost completion ───────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var dpi      = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Placeholder — only when empty and unfocused.
        if (string.IsNullOrEmpty(Text) && !IsFocused)
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

        // Ghost completion — focused, text present, suggestion available.
        if (IsFocused && !string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(CompletionText))
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

        if (e.Key == Key.Enter
            && !Keyboard.IsKeyDown(Key.LeftShift)
            && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            if (SendCommand?.CanExecute(null) == true)
                SendCommand.Execute(null);
        }
    }
}
