using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The little menu a right-click puts over a diagram: what the piece under the pointer — or everything picked out —
/// says can be done to it, named.
///
/// <para>
/// Its own control rather than entries in the document's own menu, because that menu's actions are a closed set about
/// text — cut, copy, a heading — and none of them mean anything to a node. The host shows whichever menu the block
/// under the pointer offers, so the two never have to know about each other.
/// </para>
/// <para>
/// Every button carries an automation id, because a button is the thing a journey clicks and an id is the only handle
/// that survives a copy change.
/// </para>
/// </summary>
internal sealed class DiagramRibbon : UserControl
{
    public DiagramRibbon(IReadOnlyList<LayoutIntent> offers, Action<LayoutIntent> invoke)
    {
        Offers = offers;

        var rows = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2) };

        // Adding something is browsing, and there are usually many; doing something to what is there is not,
        // and a reader reaching for one of those knows what they want. So the first are gathered behind one
        // button and the second stand where they can be reached.
        var adds = offers.Where(offer => offer.Offer == LayoutOffer.Insert).ToList();

        if (adds.Count > 0) rows.Children.Add(Inserting(adds, invoke));

        foreach (var offer in offers.Where(offer => offer.Offer != LayoutOffer.Insert)) rows.Children.Add(Offering(offer, invoke));

        Content = new Border
        {
            Child = rows,
            CornerRadius = new CornerRadius(4),
            Background = Brush("PanelBrush", Colors.Transparent),
            BorderBrush = Brush("BorderBrush", Colors.Transparent),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>Everything that can be added, behind the one button a reader opens to look through them.</summary>
    private static FrameworkElement Inserting(IReadOnlyList<LayoutIntent> adds, Action<LayoutIntent> invoke)
    {
        var menu = new Menu { Margin = new Thickness(1), Background = Brushes.Transparent };
        var insert = new MenuItem { Header = Inserts, MinWidth = 140 };

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

    /// <summary>One thing that may be done, standing on its own.</summary>
    private static FrameworkElement Offering(LayoutIntent offer, Action<LayoutIntent> invoke)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = Names(offer) },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinWidth = 140,
            Margin = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
        };

        if (offer.Target is { Length: > 0 } target) button.ToolTip = new TextBlock { Text = target };

        AutomationProperties.SetAutomationId(button, "Diagram_Ribbon_" + offer.Verb);

        var meant = offer;
        button.Click += (_, _) => invoke(meant);

        return button;
    }

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
        _ => null,
    };

    /// <summary>The face the marks are drawn in.</summary>
    public static FontFamily IconFont { get; } = new("Segoe MDL2 Assets");

    private static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
