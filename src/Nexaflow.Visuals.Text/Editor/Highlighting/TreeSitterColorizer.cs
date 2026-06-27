using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// Paints tree-sitter highlight spans onto the editor. Capture names map to theme tokens (resolved at
/// paint time so a theme switch retints code), never hard-coded colours. AvalonEdit only colourises
/// visible lines, so this runs per visible line against the current span set.
/// </summary>
internal sealed class TreeSitterColorizer : DocumentColorizingTransformer
{
    // capture name → syntax-palette resource key (per-theme TextSwatch.*)
    private static readonly Dictionary<string, string> CaptureToToken = new()
    {
        ["comment"]   = "TextSwatch.Comment",
        ["string"]    = "TextSwatch.String",
        ["number"]    = "TextSwatch.Number",
        ["keyword"]   = "TextSwatch.Keyword",
        ["type"]      = "TextSwatch.Type",
        ["constant"]  = "TextSwatch.Constant",
        ["function"]  = "TextSwatch.Function",
        ["parameter"] = "TextSwatch.Parameter",
        ["variable"]  = "TextSwatch.Parameter",
        ["tag"]       = "TextSwatch.Tag",         // html/markup element names
        ["attribute"] = "TextSwatch.Attribute",   // html attributes, css properties
    };

    private readonly Dictionary<string, Brush?> _brushCache = new();
    private IReadOnlyList<HighlightSpan> _spans = [];

    public void SetSpans(IReadOnlyList<HighlightSpan> spans) => _spans = spans;

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_spans.Count == 0) return;

        int lineStart = line.Offset, lineEnd = line.EndOffset;
        foreach (var span in _spans)
        {
            int spanEnd = span.Start + span.Length;
            if (spanEnd <= lineStart || span.Start >= lineEnd) continue; // not on this line

            int from = span.Start < lineStart ? lineStart : span.Start;
            int to   = spanEnd  > lineEnd   ? lineEnd   : spanEnd;
            if (from >= to) continue;

            var brush = BrushFor(span.Capture);
            if (brush is not null)
                ChangeLinePart(from, to, el => el.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private Brush? BrushFor(string capture)
    {
        if (_brushCache.TryGetValue(capture, out var cached)) return cached;

        Brush? brush = null;
        if (CaptureToToken.TryGetValue(capture, out var key))
            brush = Application.Current?.TryFindResource(key) as Brush;
        _brushCache[capture] = brush;
        return brush;
    }
}
