using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Core.Controls;

/// <summary>
/// A label + editable value row for the Options overlay.
/// Implemented as a ContentControl so it participates in the visual tree normally.
/// </summary>
public class OptionRow : ContentControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(OptionRow),
            new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty RowValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(OptionRow),
            new PropertyMetadata(string.Empty, OnChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(RowValueProperty);
        set => SetValue(RowValueProperty, value);
    }

    public OptionRow() { Loaded += (_, _) => Rebuild(); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((OptionRow)d).Rebuild();

    private void Rebuild()
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text              = Label,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x78, 0x80, 0xA0)),
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        Style? boxStyle = null;
        try { boxStyle = (Style?)Application.Current.FindResource("DarkTextBox"); } catch { }

        var val = new TextBox
        {
            Text     = Value,
            Style    = boxStyle,
            Padding  = new Thickness(8, 5, 8, 5),
            FontSize = 12
        };
        Grid.SetColumn(val, 1);

        grid.Children.Add(lbl);
        grid.Children.Add(val);
        Content = grid;
    }
}
