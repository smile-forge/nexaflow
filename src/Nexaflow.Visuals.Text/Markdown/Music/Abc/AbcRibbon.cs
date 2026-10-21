using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>What a reader can do to the notes in front of them without knowing a key for it.</summary>
public enum AbcAction
{
    OctaveUp,
    OctaveDown,
    Longer,
    Shorter,
    Sharpen,
    Flatten,
}

/// <summary>
/// The little ribbon a right-click puts over a score: the same gestures the keys reach, named.
///
/// <para>
/// Its own control rather than an entry in the document's formatting bar, because that bar's actions are a
/// closed set about text — bold, a heading, a quote — and none of them mean anything to a note. The host
/// shows whichever ribbon the block under the pointer offers, in the popup it already had, so the two
/// never have to know about each other.
/// </para>
/// <para>
/// Every button carries an automation id, because a button is the thing a journey clicks and an id is the
/// only handle that survives a copy change or an icon-only label.
/// </para>
/// </summary>
public sealed class AbcRibbon : UserControl
{
    public AbcRibbon(Action<AbcAction> invoke)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };

        Add(row, "Abc_Ribbon_OctaveUp", "↑♪", Str.Get("Text.Abc.OctaveUp"), AbcAction.OctaveUp);
        Add(row, "Abc_Ribbon_OctaveDown", "↓♪", Str.Get("Text.Abc.OctaveDown"), AbcAction.OctaveDown);
        Add(row, "Abc_Ribbon_Longer", "➕", Str.Get("Text.Abc.Longer"), AbcAction.Longer);
        Add(row, "Abc_Ribbon_Shorter", "➖", Str.Get("Text.Abc.Shorter"), AbcAction.Shorter);
        Add(row, "Abc_Ribbon_Sharpen", "♯", Str.Get("Text.Abc.Sharpen"), AbcAction.Sharpen);
        Add(row, "Abc_Ribbon_Flatten", "♭", Str.Get("Text.Abc.Flatten"), AbcAction.Flatten);

        Content = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(4),
            Background = Brush("PanelBrush", Colors.Transparent),
            BorderBrush = Brush("BorderBrush", Colors.Transparent),
            BorderThickness = new Thickness(1),
        };

        void Add(Panel into, string id, string glyph, string tip, AbcAction action)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = glyph, FontSize = 14, TextAlignment = TextAlignment.Center },
                MinWidth = 30,
                Margin = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                ToolTip = new TextBlock { Text = tip },
            };

            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => invoke(action);
            into.Children.Add(button);
        }
    }

    /// <summary>
    /// A theme brush by key, never a literal. A ribbon that painted its own background would be the one
    /// thing on the page a theme could not retune.
    /// </summary>
    private static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
