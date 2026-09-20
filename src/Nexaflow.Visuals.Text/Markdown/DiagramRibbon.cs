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

        foreach (var offer in offers)
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
            rows.Children.Add(button);
        }

        Content = new Border
        {
            Child = rows,
            CornerRadius = new CornerRadius(4),
            Background = Brush("PanelBrush", Colors.Transparent),
            BorderBrush = Brush("BorderBrush", Colors.Transparent),
            BorderThickness = new Thickness(1),
        };
    }

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
        _ => intent.Tip is { Length: > 0 } said ? said : intent.Verb,
    };

    private static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
