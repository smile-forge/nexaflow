using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The toolbar over the rendered block the pointer is on: the block's edge tinted, and the host's buttons in its top right
/// corner — faint until the pointer comes near them, so they can be found without sitting over the drawing.
///
/// <para>
/// An adorner over the text box rather than a control in the block, because nothing inside a text box sees the pointer
/// reliably: the host says which block the pointer is on (<see cref="Hover"/>), and the buttons, drawn above the text box,
/// are pressed like any others.
/// </para>
/// </summary>
internal sealed class BlockToolbar : Adorner
{
    /// <summary>How far in from the block's top right corner the buttons sit.</summary>
    private const double Inset = 6;

    /// <summary>How near the buttons the pointer has to come before they show in full.</summary>
    private const double Near = 24;

    /// <summary>How much of the buttons shows while the pointer is on the block but not near them.</summary>
    internal const double Faint = 0.3;

    /// <summary>How much the edge round the block shows: a tint, not a frame.</summary>
    private const double Tint = 0.55;

    private readonly Border _edge;
    private readonly StackPanel _buttons;
    private readonly VisualCollection _children;
    private readonly Func<ContentElement, RenderedBlock> _describe;
    private readonly Style _style = ButtonStyle();
    private Rect _bounds;

    /// <param name="host">The text box the blocks are in.</param>
    /// <param name="describe">What a button pressed on a block is handed for it.</param>
    public BlockToolbar(UIElement host, Func<ContentElement, RenderedBlock> describe) : base(host)
    {
        _describe = describe;

        _edge = new Border
        {
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(7),
            IsHitTestVisible = false,
            Opacity = Tint,
        };
        _edge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

        _buttons = new StackPanel { Orientation = Orientation.Horizontal };

        _children = new VisualCollection(this) { _edge, _buttons };
        Visibility = Visibility.Collapsed;
    }

    /// <summary>The block the toolbar is over, or null while it is hidden.</summary>
    public ContentElement? Block { get; private set; }

    /// <summary>The buttons, in the order the host gave them.</summary>
    public IReadOnlyList<Button> Buttons => [.. _buttons.Children.OfType<Button>()];

    /// <summary>How much of the buttons shows: <see cref="Faint"/> until the pointer comes near them, and all of them then.</summary>
    public double ButtonOpacity => _buttons.Opacity;

    /// <summary>Puts the host's buttons on the toolbar, in place of any there were.</summary>
    public void Offer(IReadOnlyList<BlockAction> actions)
    {
        _buttons.Children.Clear();

        foreach (var action in actions)
        {
            var button = new Button
            {
                Content = action.Label,
                ToolTip = action.ToolTip,
                Style = _style,
                Margin = new Thickness(3, 0, 0, 0),
                Focusable = false,
            };
            AutomationProperties.SetAutomationId(button, action.AutomationId);
            AutomationProperties.SetName(button, action.Label);
            button.Click += (_, _) => { if (Block is { } block) action.Pressed(_describe(block)); };

            _buttons.Children.Add(button);
        }

        if (actions.Count == 0) Hide();
        InvalidateMeasure();
    }

    /// <summary>Shows the toolbar over <paramref name="block"/> — or hides it, where the pointer is on no block.</summary>
    /// <param name="pointInHost">Where the pointer is, which is how near the buttons it has come.</param>
    public void Hover(ContentElement? block, Point pointInHost)
    {
        if (block is null || _buttons.Children.Count == 0 || !block.IsDescendantOf(AdornedElement))
        {
            Hide();
            return;
        }

        Block = block;
        Visibility = Visibility.Visible;
        Follow();

        var reach = new Rect(ButtonsAt(), _buttons.DesiredSize);
        reach.Inflate(Near, Near);
        _buttons.Opacity = reach.Contains(pointInHost) ? 1 : Faint;
    }

    /// <summary>Moves with the block, where it has moved — scrolled, or grown as it was written in.</summary>
    public void Follow()
    {
        if (Block is not { } block) return;

        if (!block.IsDescendantOf(AdornedElement))
        {
            Hide();
            return;
        }

        var bounds = block.TransformToAncestor(AdornedElement).TransformBounds(new Rect(block.RenderSize));
        _buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (bounds == _bounds) return;

        _bounds = bounds;
        InvalidateMeasure();
        InvalidateArrange();
    }

    public void Hide()
    {
        Block = null;
        Visibility = Visibility.Collapsed;
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size constraint)
    {
        _edge.Measure(_bounds.Size);
        _buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var edge = _bounds;
        edge.Inflate(2, 2);
        _edge.Arrange(edge);
        _buttons.Arrange(new Rect(ButtonsAt(), _buttons.DesiredSize));

        // Only over the text box: a block scrolled half out of it does not take its toolbar out with it.
        Clip = new RectangleGeometry(new Rect(AdornedElement.RenderSize));
        return finalSize;
    }

    /// <summary>The block's top right corner — or, where its top is scrolled out of sight, the top right of what is still in view.</summary>
    private Point ButtonsAt() =>
        new(_bounds.Right - Inset - _buttons.DesiredSize.Width, Math.Max(_bounds.Top, 0) + Inset);

    /// <summary>A small button drawn in the theme's own surface, edge and ink.</summary>
    private static Style ButtonStyle()
    {
        var face = new FrameworkElementFactory(typeof(Border), "face");
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        face.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        face.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 3));
        face.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        face.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        face.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = face };
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Surface2Brush"), "face"));
        template.Triggers.Add(over);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        return style;
    }
}
