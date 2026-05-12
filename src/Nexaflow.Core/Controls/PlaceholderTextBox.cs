using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Multi-line TextBox with placeholder hint and Enter-to-send support.
/// The placeholder is drawn on top of the text area when empty and unfocused.
/// Enter (no Shift) fires SendCommand; Shift+Enter inserts a newline.
/// </summary>
public class PlaceholderTextBox : TextBox
{
    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PlaceholderTextBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SendCommandProperty =
        DependencyProperty.Register(nameof(SendCommand), typeof(ICommand), typeof(PlaceholderTextBox));

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

    // ── Constructor ───────────────────────────────────────────────────────

    public PlaceholderTextBox()
    {
        AcceptsReturn  = false;
        TextWrapping   = TextWrapping.Wrap;
        PreviewKeyDown += OnPreviewKeyDown;
        TextChanged    += (_, _) => InvalidateVisual();
        GotFocus       += (_, _) => InvalidateVisual();
        LostFocus      += (_, _) => InvalidateVisual();
    }

    // ── Placeholder rendering ─────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (!string.IsNullOrEmpty(Text) || IsFocused) return;

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var ft = new FormattedText(
            Placeholder,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x70)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        ft.MaxTextWidth  = Math.Max(1, ActualWidth  - Padding.Left - Padding.Right);
        ft.MaxTextHeight = Math.Max(1, ActualHeight - Padding.Top  - Padding.Bottom);
        dc.DrawText(ft, new Point(Padding.Left, Padding.Top));
    }

    // ── Enter → send ──────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
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
