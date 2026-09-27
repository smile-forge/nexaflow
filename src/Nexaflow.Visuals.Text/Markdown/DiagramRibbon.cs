using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The mini ribbon a right-click puts over content: what the piece under the pointer — or everything picked out — says can be
/// done to it, and what the language drawn there offers.
///
/// <para>
/// Its own control rather than entries in the document's own menu, because that menu's actions are a closed set about text —
/// cut, copy, a heading — and none of them mean anything to a node. The host shows whichever ribbon the block under the pointer
/// offers, so the two never have to know about each other.
/// </para>
/// <para>
/// <strong>Pictures first, then words.</strong> Offers that are options of one choice (<see cref="LayoutIntent.Group"/>) are drawn
/// side by side, each as what it would make (<see cref="LayoutIntent.Shape"/>), the one the content says now picked out — choosing
/// another picks it out instead, and the ribbon stays open for the next. Anything else with a picture stands beside the rest of its
/// kind; what can be added is gathered behind one Insert button; what has no picture is named. Every button still says in words
/// what it does, to a reader hovering over it and to one hearing the screen read.
/// </para>
/// <para>
/// Every button carries an automation id, because a button is the thing a journey clicks and an id is the only handle that
/// survives a copy change.
/// </para>
/// </summary>
internal sealed class DiagramRibbon : UserControl
{
    public DiagramRibbon(IReadOnlyList<LayoutIntent> offers, Action<LayoutIntent> invoke)
    {
        Offers = offers;

        var rows = new StackPanel { Orientation = Orientation.Vertical };

        foreach (var choice in offers.Where(offer => offer.Group is not null).GroupBy(offer => offer.Group!))
            rows.Children.Add(Choosing(choice.Key, [.. choice], invoke));

        var loose = offers.Where(offer => offer.Group is null).ToList();
        var adds = loose.Where(offer => offer.Offer == LayoutOffer.Insert).ToList();
        var pictured = loose.Where(offer => offer.Offer != LayoutOffer.Insert && Pictured(offer)).ToList();
        var named = loose.Where(offer => offer.Offer != LayoutOffer.Insert && !Pictured(offer)).ToList();

        if (rows.Children.Count > 0 && loose.Count > 0) rows.Children.Add(Rule());

        // What has a picture is its picture, side by side. Adding something is browsing, and there are usually many; doing something
        // to what is there is not, and a reader reaching for one of those knows what they want. So the first are gathered behind one
        // button and the rest stand where they can be reached.
        if (pictured.Count > 0) rows.Children.Add(Beside(pictured.Select(offer => Offering(offer, invoke))));
        if (adds.Count > 0) rows.Children.Add(Inserting(adds, invoke));

        foreach (var offer in named) rows.Children.Add(Offering(offer, invoke));

        Content = new Border
        {
            Child = rows,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Background = Brush("SurfaceBrush", Colors.White),
            BorderBrush = Brush("BorderBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Color = Colors.Black, Opacity = 0.6 },
        };
    }

    /// <summary>One choice: its options side by side, each drawn as what it would make, the one the content says now picked out.</summary>
    private static FrameworkElement Choosing(string name, IReadOnlyList<LayoutIntent> options, Action<LayoutIntent> invoke)
    {
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<Button>();

        foreach (var option in options)
        {
            var button = Flat(new Thickness(6, 4, 6, 4));
            button.Content = Face(option, button);
            Pick(button, option.Current);
            Said(button, name + ": " + Names(option), "Diagram_Ribbon_" + option.Verb);

            var meant = option;
            button.Click += (_, _) =>
            {
                invoke(meant);
                foreach (var other in buttons) Pick(other, ReferenceEquals(other, button));
            };

            buttons.Add(button);
            segments.Children.Add(button);
        }

        return new Border
        {
            Child = segments,
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("BorderBrush", Colors.Gray),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    /// <summary>An option drawn as picked out, or not.</summary>
    private static void Pick(Button button, bool picked)
    {
        button.Background = picked ? Brush("AccentSubtleBrush", Colors.LightBlue) : Brushes.Transparent;
        button.Foreground = picked ? Brush("AccentBrush", Colors.Blue) : Brush("TextBrush", Colors.Black);
    }

    /// <summary>Everything that can be added, behind the one button a reader opens to look through them.</summary>
    private static FrameworkElement Inserting(IReadOnlyList<LayoutIntent> adds, Action<LayoutIntent> invoke)
    {
        var menu = new Menu { Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent };
        var insert = new MenuItem { Header = Inserts, MinWidth = 130 };

        foreach (var offer in adds)
        {
            var item = new MenuItem { Header = Names(offer) };
            var meant = offer;

            if (offer.Target is { Length: > 0 } target) item.ToolTip = new TextBlock { Text = target };

            AutomationProperties.SetAutomationId(item, "Diagram_Ribbon_Insert_" + offer.Verb);

            item.Click += (_, _) => invoke(meant);
            insert.Items.Add(item);
        }

        AutomationProperties.SetAutomationId(insert, "Diagram_Ribbon_Insert");
        menu.Items.Add(insert);

        return menu;
    }

    /// <summary>One thing that may be done, standing on its own: its picture where it has one, else its name.</summary>
    private static FrameworkElement Offering(LayoutIntent offer, Action<LayoutIntent> invoke)
    {
        var pictured = Pictured(offer);
        var button = Flat(pictured ? new Thickness(6, 4, 6, 4) : new Thickness(8, 5, 8, 5));
        button.Content = Face(offer, button);
        button.Foreground = Brush("TextBrush", Colors.Black);

        if (!pictured)
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 2);
        }

        Said(button, offer.Target is { Length: > 0 } target && !pictured ? target : Names(offer), "Diagram_Ribbon_" + offer.Verb);

        var meant = offer;
        button.Click += (_, _) => invoke(meant);

        return button;
    }

    /// <summary>Whether an intent is drawn as a picture — a shape of its own, or a mark — rather than named.</summary>
    private static bool Pictured(LayoutIntent intent) => intent.Shape is not null || Icon(intent) is not null;

    /// <summary>What an intent is drawn as on <paramref name="button"/>: its shape, in the button's ink; its mark; or its name.</summary>
    private static UIElement Face(LayoutIntent intent, Button button)
    {
        if (intent.Shape is { } shape)
        {
            var path = new System.Windows.Shapes.Path { Data = shape, Width = Side, Height = Side, Stretch = Stretch.None };
            path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = button });
            return path;
        }

        return Icon(intent) is { } mark
            ? new TextBlock { Text = mark, FontFamily = IconFont, FontSize = 14, Width = Side, TextAlignment = TextAlignment.Center }
            : new TextBlock { Text = Names(intent), FontSize = 12 };
    }

    /// <summary>How wide and high a picture on a button is.</summary>
    private const double Side = 16;

    /// <summary>What a reader hovering over a button, or hearing the screen read, is told it does — and the handle a journey clicks it by.</summary>
    private static void Said(Button button, string name, string id)
    {
        button.ToolTip = new TextBlock { Text = name };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, id);
    }

    /// <summary>Buttons side by side.</summary>
    private static FrameworkElement Beside(IEnumerable<FrameworkElement> buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var button in buttons) row.Children.Add(button);
        return row;
    }

    /// <summary>A button drawn flat: no chrome of its own, the surface under the pointer lit, pressed a shade deeper.</summary>
    private static Button Flat(Thickness padding) => new()
    {
        Padding = padding,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Cursor = System.Windows.Input.Cursors.Hand,
        Template = FlatTemplate.Value,
    };

    // A template belongs to the thread that made it, and every window's thread shows ribbons of its own.
    private static readonly System.Threading.ThreadLocal<ControlTemplate> FlatTemplate = new(() =>
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        surface.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        surface.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Padding)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.HorizontalAlignmentProperty,
            new System.Windows.Data.Binding(nameof(HorizontalContentAlignment)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };

        var over = new Trigger { Property = IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, Brush("Surface2Brush", Colors.LightGray), "Surface"));
        template.Triggers.Add(over);

        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Brush("BorderBrush", Colors.Gray), "Surface"));
        template.Triggers.Add(pressed);

        return template;
    });

    /// <summary>The line between the choices and what can be done.</summary>
    private static FrameworkElement Rule() => new Border
    {
        Height = 1,
        Margin = new Thickness(0, 0, 0, 6),
        Background = Brush("BorderBrush", Colors.Gray),
    };

    /// <summary>What the button everything addable sits behind is called.</summary>
    public const string Inserts = "Insert";

    /// <summary>What it is offering, in the order it offers them.</summary>
    public IReadOnlyList<LayoutIntent> Offers { get; }

    /// <summary>
    /// What an intent is called where a reader has to read it. The verbs the renderer knows are named here so every
    /// diagram words them the same way; anything else says what it said for itself, and falls back to its verb.
    /// </summary>
    public static string Names(LayoutIntent intent) => intent.Verb switch
    {
        LayoutVerbs.Navigate => "Open link",
        LayoutVerbs.Expand => "Show what is behind this",
        LayoutVerbs.Collapse => "Fold this away",
        LayoutVerbs.Copy => "Copy",
        LayoutVerbs.Paste => "Paste",
        LayoutVerbs.Save => "Save as a picture",
        _ => intent.Tip is { Length: > 0 } said ? said : intent.Verb,
    };

    /// <summary>
    /// The mark an intent is drawn as where there is room only for a mark — a block's corner — or null where it has none and
    /// is named instead. Segoe MDL2 Assets, which every Windows the app runs on carries.
    /// </summary>
    public static string? Icon(LayoutIntent intent) => intent.Verb switch
    {
        LayoutVerbs.Copy => "\uE8C8",
        LayoutVerbs.Save => "\uE74E",
        LayoutVerbs.Navigate => "\uE8A7",
        LayoutVerbs.Paste => "\uE77F",
        _ => null,
    };

    /// <summary>The face the marks are drawn in.</summary>
    public static FontFamily IconFont { get; } = new("Segoe MDL2 Assets");

    private static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
