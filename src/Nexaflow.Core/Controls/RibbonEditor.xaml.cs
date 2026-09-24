using System.Windows;
using System.Windows.Controls;
using Nexaflow.Core.ViewModels;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The ribbon editor's view. Everything it does is <see cref="RibbonEditorViewModel"/>'s; MainWindow creates it
/// only while the editor is open.
/// </summary>
public partial class RibbonEditor : UserControl
{
    public RibbonEditor() => InitializeComponent();

    public static readonly DependencyProperty RibbonWidthProperty =
        DependencyProperty.Register(nameof(RibbonWidth), typeof(double), typeof(RibbonEditor),
            new PropertyMetadata(double.NaN));

    /// <summary>The live ribbon's width, which the editing card matches.</summary>
    public double RibbonWidth
    {
        get => (double)GetValue(RibbonWidthProperty);
        set => SetValue(RibbonWidthProperty, value);
    }
}

/// <summary>A strip card's look: the ribbon button itself, or a divider.</summary>
public sealed class RibbonCardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Button { get; set; }
    public DataTemplate? Separator { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is RibbonEditorCard { IsSeparator: true } ? Separator : Button;
}
