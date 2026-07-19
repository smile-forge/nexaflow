using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// A compact, read-only preview of a text-ish page for the AI conversation's context-preview panel:
/// a title, a one-line meta subtitle, and a scrollable monospace snippet of the content. Reusable by any
/// <c>IContextPreview</c> implementer whose page is essentially text (text/code/markdown/json/log/…).
/// <para>
/// It is a <em>fresh, cheap</em> element — a summary, not a second working editor — so it never re-hosts the
/// page's live content (an element can't have two parents). Fully themed via <c>DynamicResource</c>
/// (<c>TextBrush</c> foreground, <c>BorderBrush</c> separator); no hard-coded colours.
/// </para>
/// </summary>
public sealed class ReadOnlyTextPreview : UserControl
{
    /// <param name="title">Usually the file name.</param>
    /// <param name="meta">One-line summary — e.g. "1,240 lines · UTF-8 · unsaved edits".</param>
    /// <param name="body">A snippet of the content. Cap it before passing — the panel stays light.</param>
    public ReadOnlyTextPreview(string title, string meta, string body)
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetRow(titleBlock, 0);

        var metaBlock = new TextBlock
        {
            Text = meta,
            Opacity = 0.7,
            Margin = new Thickness(0, 2, 0, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        metaBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetRow(metaBlock, 1);

        var bodyBlock = new TextBlock
        {
            Text = body,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
        };
        bodyBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var scroller = new ScrollViewer
        {
            Content = bodyBlock,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var body_border = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 6, 0, 0),
            Child = scroller,
        };
        body_border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Grid.SetRow(body_border, 2);

        root.Children.Add(titleBlock);
        root.Children.Add(metaBlock);
        root.Children.Add(body_border);
        Content = root;
    }
}
