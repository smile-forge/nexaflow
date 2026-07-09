using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Rebuilds a block's markdown source from its rendered <see cref="Paragraph"/> — the write half of
/// Word-style ("type into the rendered line") editing in <see cref="InlineMarkdownEditor"/>.
///
/// Reconstruction is structural: formatting comes from the inline tree (bold/italic spans,
/// strikethrough decorations, mono-font code runs, <see cref="Hyperlink"/>s), and literal text is
/// escaped so markdown syntax the user <em>types</em> stays plain text. It intentionally understands
/// ONLY constructs whose rendering is losslessly reversible; anything else (task-list glyphs,
/// sub/superscript, highlights, images, math, non-standard emphasis like <c>__x__</c>…) makes the
/// serialize fail or mismatch, and the editor falls back to raw-source editing for that block.
///
/// The editor MUST verify <c>serialize(pristine paragraph) == block source</c> before trusting an
/// edit session — that round-trip proof is what guarantees Word-style editing can never corrupt a
/// document: we only edit natively where reconstruction is demonstrably exact.
/// </summary>
public static class MarkdownInlineSerializer
{
    /// <summary>
    /// Serializes the paragraph's inlines back to markdown. With <paramref name="upTo"/> set, emits only
    /// the content before that pointer (open markers left unclosed) — the result is a string prefix of
    /// the full serialization, which is how the editor maps a caret to a source offset for block splits.
    /// <paramref name="escapeLeadingMarker"/> guards a plain paragraph against typed text that would
    /// re-parse as a block construct (<c># </c>, <c>- </c>, <c>&gt;</c>…); pass false when the block has
    /// its own prefix (a heading). Returns false when the paragraph contains a construct that cannot be
    /// losslessly reconstructed.
    /// </summary>
    public static bool TrySerialize(Paragraph para, TextPointer? upTo, bool escapeLeadingMarker, out string markdown)
    {
        markdown = string.Empty;
        var sb = new StringBuilder();
        bool stopped = false;
        if (!Walk(para.Inlines, sb, upTo, ref stopped)) return false;
        var s = sb.ToString();
        if (escapeLeadingMarker && LeadingBlockMarker.IsMatch(s)) s = "\\" + s;
        markdown = s;
        return true;
    }

    private static bool Walk(InlineCollection inlines, StringBuilder sb, TextPointer? upTo, ref bool stopped)
    {
        foreach (var inline in inlines)
        {
            if (stopped) return true;
            switch (inline)
            {
                case Run r:
                    if (!EmitRun(r, sb, upTo, ref stopped)) return false;
                    break;

                case Hyperlink h:
                {
                    if ((h.Tag as string ?? h.NavigateUri?.OriginalString) is not { } url) return false;
                    var inner = new StringBuilder();
                    if (!Walk(h.Inlines, inner, upTo, ref stopped)) return false;
                    var text = inner.ToString();
                    if (!stopped && (url == text || url == "mailto:" + text))
                        sb.Append(text);                                   // bare autolink (www/email)
                    else
                    {
                        sb.Append('[').Append(text);
                        if (!stopped) sb.Append("](").Append(url).Append(')');
                    }
                    break;
                }

                case Span s:
                {
                    if (SpanMarkers(s) is not { } markers) return false;
                    foreach (var m in markers) sb.Append(m);
                    if (!Walk(s.Inlines, sb, upTo, ref stopped)) return false;
                    if (!stopped)
                        for (int i = markers.Count - 1; i >= 0; i--) sb.Append(markers[i]);
                    break;
                }

                default:
                    return false;   // LineBreak, InlineUIContainer, Figure… — not reconstructable
            }
        }
        return true;
    }

    /// <summary>The markdown delimiters a span contributes (<c>**</c> / <c>*</c> / <c>~~</c>, possibly
    /// several), or null when the span carries formatting we can't reverse (highlight, sub/superscript,
    /// citation…). An unstyled span is transparent (empty list).</summary>
    private static List<string>? SpanMarkers(Span s)
    {
        if (HasLocal(s, TextElement.BackgroundProperty) || HasLocal(s, TextElement.FontSizeProperty)
         || HasLocal(s, TextElement.ForegroundProperty) || HasLocal(s, Inline.BaselineAlignmentProperty)
         || HasLocal(s, TextElement.FontFamilyProperty))
            return null;

        var markers = new List<string>(2);
        if (s is Bold || (HasLocal(s, TextElement.FontWeightProperty) && s.FontWeight == FontWeights.Bold))
            markers.Add("**");
        else if (HasLocal(s, TextElement.FontWeightProperty)) return null;

        if (s is Italic || (HasLocal(s, TextElement.FontStyleProperty) && s.FontStyle == FontStyles.Italic))
            markers.Add("*");
        else if (HasLocal(s, TextElement.FontStyleProperty)) return null;

        if (HasLocal(s, Inline.TextDecorationsProperty))
        {
            if (IsStrikethrough(s.TextDecorations)) markers.Add("~~");
            else return null;                       // underline (++x++) etc. — no lossless markdown form
        }
        return markers;
    }

    private static bool EmitRun(Run r, StringBuilder sb, TextPointer? upTo, ref bool stopped)
    {
        string text = r.Text;
        if (upTo is not null && upTo.CompareTo(r.ContentStart) >= 0 && upTo.CompareTo(r.ContentEnd) <= 0)
        {
            text = text[..Math.Clamp(r.ContentStart.GetOffsetToPosition(upTo), 0, text.Length)];
            stopped = true;
        }

        // Inline code: the mono font is the marker; its colour/size locals are part of that style.
        if (HasLocal(r, TextElement.FontFamilyProperty))
        {
            if (!ReferenceEquals(r.FontFamily, BlockRenderer.MonoFont)) return false;
            EmitCode(text, sb, closed: !stopped);
            return true;
        }

        var markers = new List<string>(2);
        if (HasLocal(r, TextElement.FontWeightProperty))
        {
            if (r.FontWeight == FontWeights.Bold) markers.Add("**");
            else return false;
        }
        if (HasLocal(r, TextElement.FontStyleProperty))
        {
            if (r.FontStyle == FontStyles.Italic) markers.Add("*");
            else return false;
        }
        if (HasLocal(r, TextElement.ForegroundProperty) || HasLocal(r, TextElement.BackgroundProperty)
         || HasLocal(r, TextElement.FontSizeProperty) || HasLocal(r, Inline.BaselineAlignmentProperty))
            return false;   // task-list glyphs, math fallback… — not reconstructable

        // A custom decoration (the abbreviation's dotted underline) renders the literal label — plain
        // text; genuine strikethrough is a marker.
        if (HasLocal(r, Inline.TextDecorationsProperty) && IsStrikethrough(r.TextDecorations))
            markers.Add("~~");

        foreach (var m in markers) sb.Append(m);
        sb.Append(Escape(text));
        if (!stopped)
            for (int i = markers.Count - 1; i >= 0; i--) sb.Append(markers[i]);
        return true;
    }

    private static void EmitCode(string content, StringBuilder sb, bool closed)
    {
        int longest = 0, run = 0;
        foreach (char c in content) { run = c == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
        string fence = new('`', longest + 1);
        bool pad = content.StartsWith('`') || content.EndsWith('`');   // CommonMark: pad so the fence stays distinct
        sb.Append(fence);
        if (pad) sb.Append(' ');
        sb.Append(content);
        if (closed)
        {
            if (pad) sb.Append(' ');
            sb.Append(fence);
        }
    }

    /// <summary>
    /// Backslash-escapes characters that would give typed text markdown meaning, so Word-style input is
    /// always literal. Deliberately minimal — every escape here makes a pristine line containing that
    /// character fail the round-trip check (falling back to source editing), so common prose characters
    /// stay unescaped: <c>_</c> only at a word boundary (intraword <c>snake_case</c> can't open emphasis),
    /// <c>&lt;</c> only when HTML-ish, <c>&amp;</c> only when it reads as an entity.
    /// </summary>
    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool esc = c switch
            {
                '\\' or '`' or '*' or '~' or '[' or ']' => true,
                '_' => !(i > 0 && char.IsLetterOrDigit(text[i - 1])
                         && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1])),
                '<' => i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] is '/' or '!' or '?'),
                '&' => EntityStart.IsMatch(text.AsSpan(i)),
                _   => false,
            };
            if (esc) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsStrikethrough(TextDecorationCollection? td)
        => td is { Count: 1 } && td[0].Location == TextDecorationLocation.Strikethrough && td[0].Pen is null;

    private static bool HasLocal(DependencyObject o, DependencyProperty p)
        => o.ReadLocalValue(p) != DependencyProperty.UnsetValue;

    private static readonly Regex EntityStart =
        new(@"^&([a-zA-Z][a-zA-Z0-9]{1,31}|#\d{1,7}|#[xX][0-9a-fA-F]{1,6});", RegexOptions.Compiled);

    // Typed-at-line-start sequences that would re-parse as a block construct in a plain paragraph.
    // A pristine paragraph can never match (it would have parsed as that construct instead).
    private static readonly Regex LeadingBlockMarker =
        new(@"^(#{1,6}[ \t]|>|[-+*][ \t]|\d{1,9}[.)][ \t]|(=+|-+)[ \t]*$)", RegexOptions.Compiled);
}
