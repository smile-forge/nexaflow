using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.Features.Common.Search;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// One match found in a rendered surface — its 0-based ordinal (the id an <see cref="ISearchable"/> page
/// round-trips) and the visible text of the paragraph it sits in (a preview for the model).
/// </summary>
public readonly record struct RenderedMatch(int Ordinal, string Preview);

/// <summary>
/// Finds and highlights matches in the <em>rendered</em> text of a <see cref="RichTextBox"/> — the visible
/// words, not the markdown source. It is what makes "search here" mean the same thing whether the markdown
/// page is showing its source box or its rendered surface, and it is shared so the email body (also a
/// rendered markdown surface) highlights identically.
/// <para>
/// Highlighting is reversible without rebuilding the document: each match's original run background is
/// captured before it is painted and restored on <see cref="Clear"/>, so the surface — and the user's
/// scroll position — is left exactly as it was.
/// </para>
/// </summary>
public sealed class RenderedMarkdownSearch(RichTextBox surface)
{
    private readonly record struct Hit(
        TextRange Range, object? OriginalBackground, object? OriginalForeground, int Ordinal, string Preview);

    private readonly List<Hit> _hits = [];
    private int _current = -1;

    // Themed, resolved lazily. Every occurrence gets the search wash; the focused one gets the accent, so
    // stepping through matches is visible. Literals only as a last resort if a theme omits the token.
    private static Brush MatchBrush   => Resource("Search.Match")     ?? Brushes.Khaki;
    private static Brush CurrentBrush => Resource("AccentBrush")      ?? Brushes.DodgerBlue;
    private static Brush CurrentText  => Resource("OnAccentBrush")    ?? Brushes.White;

    private static Brush? Resource(string key) =>
        Application.Current?.TryFindResource(key) as Brush;

    public int Count => _hits.Count;

    /// <summary>Finds every match, paints them, and focuses the first. Returns the matches as data so the
    /// page can hand ids and previews to the model.</summary>
    public IReadOnlyList<RenderedMatch> Run(TextSearchMatcher matcher)
    {
        Clear();

        var ordinal = 0;
        foreach (var para in Paragraphs(surface.Document))
        {
            var (text, map) = MapParagraph(para);
            if (text.Length == 0) continue;

            foreach (var line in matcher.ScanLines(text))
                foreach (var (index, length) in matcher.Occurrences(line.Text))
                {
                    var start = map.PointerAt(line.Offset + index);
                    var end   = map.PointerAt(line.Offset + index + length);
                    if (start is null || end is null) continue;

                    var range = new TextRange(start, end);
                    _hits.Add(new Hit(
                        range,
                        range.GetPropertyValue(TextElement.BackgroundProperty),
                        range.GetPropertyValue(TextElement.ForegroundProperty),
                        ordinal++,
                        line.Text.Trim()));
                }
        }

        Paint(-1);
        if (_hits.Count > 0) Navigate(0);
        return [.. _hits.Select(h => new RenderedMatch(h.Ordinal, h.Preview))];
    }

    /// <summary>Restores every painted run and drops the match set — no document rebuild, so scroll is
    /// preserved.</summary>
    public void Clear()
    {
        foreach (var hit in _hits) Restore(hit);
        _hits.Clear();
        _current = -1;
    }

    private static void Restore(Hit hit)
    {
        hit.Range.ApplyPropertyValue(TextElement.BackgroundProperty, hit.OriginalBackground);
        hit.Range.ApplyPropertyValue(TextElement.ForegroundProperty, hit.OriginalForeground);
    }

    /// <summary>Focuses match <paramref name="index"/> (wrapping), scrolling it into view.</summary>
    public void Navigate(int index)
    {
        if (_hits.Count == 0) return;
        _current = ((index % _hits.Count) + _hits.Count) % _hits.Count;
        Paint(_current);
        ScrollIntoView(_hits[_current].Range.Start);
    }

    // Scrolls the match roughly a third down the viewport. GetCharacterRect gives the caret rect in the
    // control's own coordinates (negative above the view, past the height below it), so adding the current
    // offset converts it to an absolute content position — reliable where Paragraph.BringIntoView is not
    // (a match inside a list item or table cell has no owning Paragraph to bring into view).
    private void ScrollIntoView(TextPointer at)
    {
        var rect = at.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
        {
            (at.Parent as FrameworkContentElement)?.BringIntoView();
            return;
        }
        var target = surface.VerticalOffset + rect.Top - surface.ViewportHeight / 3;
        surface.ScrollToVerticalOffset(Math.Max(0, target));
    }

    public void Step(int delta) => Navigate((_current < 0 ? 0 : _current) + delta);

    /// <summary>Each match's normalised (0–1) vertical position in the document, for a minimap tick strip.
    /// Empty until the surface has laid out (its extent is known).</summary>
    public IReadOnlyList<double> Positions()
    {
        var extent = surface.ExtentHeight;
        if (extent <= 0 || _hits.Count == 0) return [];

        var list = new List<double>(_hits.Count);
        foreach (var hit in _hits)
        {
            var rect = hit.Range.Start.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) continue;
            list.Add(System.Math.Clamp((surface.VerticalOffset + rect.Top) / extent, 0, 1));
        }
        return list;
    }

    /// <summary>Restricts the painted set to the matches whose ordinal is in <paramref name="keep"/> — the
    /// page's way of narrowing to a subset the model chose. Returns how many survived.</summary>
    public int Restrict(IReadOnlySet<int> keep)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
            if (!keep.Contains(_hits[i].Ordinal))
            {
                Restore(_hits[i]);
                _hits.RemoveAt(i);
            }
        _current = -1;
        Paint(-1);
        if (_hits.Count > 0) Navigate(0);
        return _hits.Count;
    }

    // Re-colours every match: the focused one accented (with the on-accent text colour so it stays legible),
    // the rest washed with the search colour over their original text colour.
    private void Paint(int currentIndex)
    {
        for (var i = 0; i < _hits.Count; i++)
        {
            var focused = i == currentIndex;
            _hits[i].Range.ApplyPropertyValue(TextElement.BackgroundProperty, focused ? CurrentBrush : MatchBrush);
            _hits[i].Range.ApplyPropertyValue(TextElement.ForegroundProperty,
                focused ? CurrentText : _hits[i].OriginalForeground);
        }
    }

    // ── FlowDocument text mapping ──────────────────────────────────────────────

    /// <summary>Maps a character offset within a paragraph's visible text back to a live text pointer.</summary>
    private sealed class OffsetMap
    {
        private readonly List<(int Global, TextPointer Start, int Length)> _runs = [];

        public void Add(int global, TextPointer start, int length) => _runs.Add((global, start, length));

        public TextPointer? PointerAt(int offset)
        {
            foreach (var (global, start, length) in _runs)
                if (offset >= global && offset <= global + length)
                    // Within a text run, GetPositionAtOffset counts characters exactly — the whole reason
                    // matches are mapped per run rather than off a whole-document symbol offset.
                    return start.GetPositionAtOffset(offset - global, LogicalDirection.Forward);
            return null;
        }
    }

    /// <summary>
    /// A paragraph's visible text plus the map back to pointers. Built by walking the paragraph's own text
    /// runs, so the string and the pointers share one coordinate system; a hard line break inside the
    /// paragraph becomes a newline, so a match can't straddle it.
    /// </summary>
    private static (string Text, OffsetMap Map) MapParagraph(Paragraph para)
    {
        var sb  = new System.Text.StringBuilder();
        var map = new OffsetMap();

        var p = para.ContentStart;
        var end = para.ContentEnd;
        while (p is not null && p.CompareTo(end) < 0)
        {
            switch (p.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    var run = p.GetTextInRun(LogicalDirection.Forward);
                    map.Add(sb.Length, p, run.Length);
                    sb.Append(run);
                    p = p.GetPositionAtOffset(run.Length, LogicalDirection.Forward);
                    break;

                case TextPointerContext.ElementStart
                    when p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak:
                    if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                    break;

                default:
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                    break;
            }
        }
        return (sb.ToString(), map);
    }

    /// <summary>Every <see cref="Paragraph"/> in the document, in reading order — descending through the
    /// sections, lists and tables the markdown renderer produces, so a match in a quote, a bullet or a table
    /// cell is found like any other.</summary>
    private static IEnumerable<Paragraph> Paragraphs(FlowDocument doc) => InBlocks(doc.Blocks);

    private static IEnumerable<Paragraph> InBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
            foreach (var para in InBlock(block))
                yield return para;
    }

    private static IEnumerable<Paragraph> InBlock(Block block) => block switch
    {
        Paragraph p => [p],
        Section s   => InBlocks(s.Blocks),
        List l      => l.ListItems.SelectMany(li => InBlocks(li.Blocks)),
        Table t     => t.RowGroups.SelectMany(g => g.Rows)
                                   .SelectMany(r => r.Cells)
                                   .SelectMany(c => InBlocks(c.Blocks)),
        _           => [],
    };
}
