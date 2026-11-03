using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// A slider for an edit value that has a neutral position: double-clicking it goes back there. A drag of its thumb
/// raises the ordinary <c>Thumb.DragStarted</c> / <c>DragCompleted</c>, which the edit view turns into one undo step.
/// Given a <see cref="Label"/>, it is one row of a panel: the name, the rail, and the value as <see cref="ValueFormat"/> writes
/// it — which a click turns into a box to type the value in, taken on Enter or on leaving it, Escape giving up the typing.
/// </summary>
[TemplatePart(Name = ValuePart, Type = typeof(TextBox))]
public class AdjustmentSlider : Slider
{
    public const string ValuePart = "PART_Value";

    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(AdjustmentSlider), new PropertyMetadata(0.0));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(AdjustmentSlider), new PropertyMetadata(null));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(AdjustmentSlider), new PropertyMetadata("0", (d, _) => ((AdjustmentSlider)d).ShowValue()));

    private static readonly DependencyPropertyKey ValueTextKey = DependencyProperty.RegisterReadOnly(
        nameof(ValueText), typeof(string), typeof(AdjustmentSlider), new PropertyMetadata("0"));

    public static readonly DependencyProperty ValueTextProperty = ValueTextKey.DependencyProperty;

    private TextBox? _valueBox;

    /// <summary>The neutral value a double-click restores.</summary>
    public double DefaultValue
    {
        get => (double)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    /// <summary>The value's name, at the head of its row; none for a slider laid out by its panel.</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>How the value is written at the end of its row — a .NET number format, in the user's culture.</summary>
    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    /// <summary>The value as <see cref="ValueFormat"/> writes it.</summary>
    public string ValueText => (string)GetValue(ValueTextProperty);

    public override void OnApplyTemplate()
    {
        if (_valueBox is not null)
        {
            _valueBox.PreviewMouseLeftButtonDown -= ValuePressed;
            _valueBox.GotKeyboardFocus -= ValueFocused;
            _valueBox.KeyDown -= ValueKeyed;
            _valueBox.LostKeyboardFocus -= ValueLeft;
        }
        base.OnApplyTemplate();
        _valueBox = GetTemplateChild(ValuePart) as TextBox;
        if (_valueBox is null) return;
        _valueBox.Text = ValueText;
        _valueBox.PreviewMouseLeftButtonDown += ValuePressed;
        _valueBox.GotKeyboardFocus += ValueFocused;
        _valueBox.KeyDown += ValueKeyed;
        _valueBox.LostKeyboardFocus += ValueLeft;
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        ShowValue();
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        // A double-click in the value selects what is typed there, never resets it.
        if (_valueBox is not null && _valueBox.IsKeyboardFocusWithin) return;
        Value = DefaultValue;
        e.Handled = true;
        base.OnMouseDoubleClick(e);
    }

    private void ShowValue()
    {
        SetValue(ValueTextKey, Value.ToString(ValueFormat, CultureInfo.CurrentCulture));
        if (_valueBox is { IsKeyboardFocusWithin: false } box) box.Text = ValueText;
    }

    /// <summary>The first press on the value takes it whole, ready to be typed over.</summary>
    private void ValuePressed(object sender, MouseButtonEventArgs e)
    {
        if (_valueBox is not { IsKeyboardFocusWithin: false } box) return;
        box.Focus();
        e.Handled = true;
    }

    private void ValueFocused(object sender, KeyboardFocusChangedEventArgs e) => _valueBox?.SelectAll();

    private void ValueKeyed(object sender, KeyEventArgs e)
    {
        if (_valueBox is not { } box) return;
        if (e.Key == Key.Enter) Take(box);
        else if (e.Key == Key.Escape) box.Text = ValueText;
        else return;
        Focus();   // back to the slider, its arrow keys at hand
        e.Handled = true;
    }

    private void ValueLeft(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_valueBox is { } box) Take(box);
    }

    /// <summary>What is typed in <paramref name="box"/> made the value, held to the slider's bounds; what is no number leaves it.</summary>
    private void Take(TextBox box)
    {
        if (TryRead(box.Text, out var typed)) Value = Math.Clamp(typed, Minimum, Maximum);
        box.Text = ValueText;
    }

    /// <summary>A number as a person types one at a value: in their culture or with a point, signed with a hyphen or a minus,
    /// and with whatever unit the value is written with — %, °, px — after it.</summary>
    public static bool TryRead(string text, out double value)
    {
        var digits = new string([.. text.Trim().Replace('−', '-').TakeWhile(c => char.IsDigit(c) || c is '-' or '+' or '.' or ',' || char.IsWhiteSpace(c))]).Trim();
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
