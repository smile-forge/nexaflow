using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Markdig.Renderers.Html;
using WpfBlock = System.Windows.Documents.Block;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// In-page links: <c>[Searching](#searching)</c> jumps to the heading "Searching". Every heading gets a GitHub-style
/// id as the document is parsed (<see cref="MarkdownPipelineFactory"/>: lower-case, spaces to hyphens, punctuation
/// dropped, a repeat numbered <c>-1</c>, <c>-2</c>…), the rendered heading carries it (<see cref="IdProperty"/>), and a
/// surface that scrolls resolves a <c>#id</c> link to it itself — before its host's link handler is asked, since a
/// bare anchor means nothing outside the document it sits in.
/// </summary>
public static class MarkdownAnchors
{
    // Room left above a heading scrolled to the top, so it doesn't sit hard against the edge.
    private const double TopGap = 8;

    /// <summary>The anchor id a rendered heading answers to.</summary>
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(string), typeof(MarkdownAnchors), new PropertyMetadata(null));

    public static string? GetId(DependencyObject element) => (string?)element.GetValue(IdProperty);

    public static void SetId(DependencyObject element, string? value) => element.SetValue(IdProperty, value);

    /// <summary>The id the pipeline gave <paramref name="heading"/>, or null.</summary>
    public static string? IdOf(Markdig.Syntax.HeadingBlock heading) => heading.TryGetAttributes()?.Id;

    /// <summary>True for a link into the same document (<c>#id</c>), with the id it names, unescaped.</summary>
    public static bool IsInPage(string? url, out string anchor)
    {
        anchor = "";
        if (url is not { Length: > 1 } || url[0] != '#') return false;
        anchor = Uri.UnescapeDataString(url[1..]);
        return true;
    }

    /// <summary>The block in <paramref name="document"/> carrying <paramref name="anchor"/>, looked for through sections,
    /// lists and tables too, since a heading can sit inside any of them.</summary>
    public static WpfBlock? Find(FlowDocument document, string anchor) => Find(document.Blocks, anchor);

    /// <summary>
    /// Scrolls <paramref name="surface"/> so the heading carrying <paramref name="anchor"/> sits at the top of the view;
    /// false when its document has no such heading. A surface that does not scroll itself — an outer scroller does —
    /// or has not laid out yet is asked to bring the heading into view instead.
    /// </summary>
    public static bool ScrollTo(RichTextBox surface, string anchor)
    {
        if (surface.Document is not { } document || Find(document, anchor) is not { } target) return false;

        var rect = target.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty || surface.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
            target.BringIntoView();
        else
            surface.ScrollToVerticalOffset(Math.Max(0, surface.VerticalOffset + rect.Top - TopGap));
        return true;
    }

    private static WpfBlock? Find(BlockCollection blocks, string anchor)
    {
        foreach (var block in blocks)
        {
            if (string.Equals(GetId(block), anchor, StringComparison.OrdinalIgnoreCase)) return block;

            var inner = block switch
            {
                Section section => Find(section.Blocks, anchor),
                List list       => list.ListItems.Select(item => Find(item.Blocks, anchor)).FirstOrDefault(b => b is not null),
                Table table     => table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells)
                                        .Select(cell => Find(cell.Blocks, anchor)).FirstOrDefault(b => b is not null),
                _               => null,
            };
            if (inner is not null) return inner;
        }
        return null;
    }
}
