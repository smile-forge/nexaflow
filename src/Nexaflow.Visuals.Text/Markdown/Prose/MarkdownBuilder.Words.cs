using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// Setting the words of a block: what the constructs a writer spelled with punctuation are drawn as, and where the
/// lines break.
///
/// <para>
/// <strong>A run of text is one piece with a position between any two of its letters</strong>, not a piece per letter.
/// So a line is built by gathering the constructs into runs, breaking the runs into lines, and then joining back up
/// everything on one line that is set the same way — which is how <c>a **bold** word</c> comes out as three pieces
/// rather than eleven.
/// </para>
/// <para>
/// Most runs are the source: what is drawn is what was written, at the offset it was written at, so the caret lands
/// exactly where it looks like it will. The ones that are not say so, and pressing one shows what was written instead
/// — which is what an entity is, and what a list's renumbered marker is.
/// </para>
/// </summary>
public sealed partial class MarkdownBuilder
{
    /// <summary>How a stretch is set. Everything about the drawing; nothing about the source.</summary>
    private readonly record struct Face
    {
        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public bool Strike { get; init; }

        public bool Underline { get; init; }

        public bool Mono { get; init; }

        /// <summary>How big, against the reader's text size.</summary>
        public double Scale { get; init; }

        /// <summary>How far off the line it sits, against the text size — negative is up.</summary>
        public double Lift { get; init; }

        public Brush? Ink { get; init; }

        /// <summary>What is washed behind it, for a highlighter or a code span.</summary>
        public Brush? Wash { get; init; }

        /// <summary>Ordinary body text.</summary>
        public static Face Plain => new() { Scale = 1 };
    }

    /// <summary>
    /// A stretch set one way, standing for one part of the source.
    /// </summary>
    /// <param name="Maps">
    /// Whether what is drawn is what was written, at the offsets it was written at. False for the few things that are
    /// not — an entity, a marker drawn as the number the item is — and a press on one of those shows the source.
    /// </param>
    private readonly record struct Run(string Text, ContentPart Part, Face Face, bool Maps, LayoutIntent? Act = null);

    /// <summary>Sets what a block says, breaking lines at the room it was given.</summary>
    private void Text(LayoutBuilder into, ContentPart words, double x, double room, Face face)
    {
        var runs = new List<Run>();
        Gather(words, face, runs);

        if (runs.Count == 0)
        {
            _y += Glyphs(" ", face).Height;
            Reached(x);

            return;
        }

        Lines(into, runs, x, Math.Max(room, 1));
    }

    // ── What each construct is set as ───────────────────────────────────────

    private void Gather(ContentPart part, Face face, List<Run> runs)
    {
        if (part.Derived) return;

        switch (part.Kind)
        {
            case MarkdownKinds.Strong: Inside(part, face with { Bold = true }, runs); return;
            case MarkdownKinds.Emphasis: Inside(part, face with { Italic = true }, runs); return;
            case MarkdownKinds.Strike: Inside(part, face with { Strike = true }, runs); return;
            case MarkdownKinds.Insert: Inside(part, face with { Underline = true }, runs); return;
            case MarkdownKinds.Mark: Inside(part, face with { Wash = Style.Marked }, runs); return;

            case MarkdownKinds.Sub: Inside(part, face with { Scale = face.Scale * 0.72, Lift = 0.22 }, runs); return;
            case MarkdownKinds.Sup: Inside(part, face with { Scale = face.Scale * 0.72, Lift = -0.34 }, runs); return;

            // Nothing inside a code span is read, so nothing inside it is set: what is there is what was typed.
            case MarkdownKinds.Code when part.Part(Roles.Body) is { } code:
                runs.Add(new Run(code.Text, code, face with { Mono = true, Wash = Style.CodeBg }, Maps: true));
                return;

            case MarkdownKinds.Link:
            case MarkdownKinds.Image:
                Linked(part, face, runs);
                return;

            // Written as the characters that could hold it, drawn as the character it stands for — so what is drawn is
            // not what was written, and a press on it says so.
            case MarkdownKinds.Entity:
                runs.Add(new Run(WebUtility.HtmlDecode(part.Text), part, face, Maps: false));
                return;

            // The item draws its own box; the three characters it stands for are the item's, not its words'.
            case MarkdownKinds.Task:
                return;
        }

        if (part.Children.Count > 0)
        {
            Inside(part, face, runs);

            return;
        }

        if (part.Role is Roles.Open or Roles.Close or Roles.Name or Roles.Trivia)
        {
            // Machinery is not drawn. Where it ran over a line ending, though, it stood between two words, and two
            // words with nothing between them are one word.
            if (part.Text.Contains('\n')) runs.Add(new Run(" ", part, face, Maps: false));

            return;
        }

        if (part.Text.Length > 0) runs.Add(new Run(Flowed(part.Text), part, face, Maps: true));
    }

    private void Inside(ContentPart part, Face face, List<Run> runs)
    {
        foreach (var child in part.Children) Gather(child, face, runs);
    }

    /// <summary>
    /// A link, or a picture named by one. Where it points is not drawn — it is machinery — but the press that follows
    /// it is declared here, and it is the host at the far end of that which decides what following one means.
    /// </summary>
    private void Linked(ContentPart part, Face face, List<Run> runs)
    {
        var where = part.Part(MarkdownRoles.Destination)?.Text;
        var says = part.Part(MarkdownRoles.Title)?.Text;
        var act = where is { Length: > 0 } ? new LayoutIntent(LayoutVerbs.Navigate, where, says) : (LayoutIntent?)null;

        var linked = face with { Underline = true, Ink = Style.Accent };
        var body = part.Part(Roles.Body);

        if (body is null || body.Length == 0)
        {
            // A bare or bracketed url is its own words.
            runs.Add(new Run(where ?? part.Print(), part, linked, Maps: where is { Length: > 0 }, Act: act));

            return;
        }

        var at = runs.Count;
        Gather(body, linked, runs);

        // Whatever the words turned out to be, the whole of them answers the press.
        for (var index = at; index < runs.Count; index++) runs[index] = runs[index] with { Act = act };
    }

    /// <summary>
    /// A line ending inside a run of words is a space: markdown reflows what was written to the room it has. Swapped
    /// character for character rather than collapsed, so every offset still lands where it did and the run still says
    /// it is the source.
    /// </summary>
    private static string Flowed(string text) =>
        text.Contains('\n') || text.Contains('\r') ? text.Replace('\n', ' ').Replace('\r', ' ') : text;

    // ── Where the lines break ───────────────────────────────────────────────

    private void Lines(LayoutBuilder into, IReadOnlyList<Run> runs, double x, double room)
    {
        var line = new List<(Run Run, string Text)>();
        var width = 0.0;

        foreach (var (run, text, breaks) in Chunks(runs))
        {
            if (breaks)
            {
                Row(into, line, x);
                width = 0;

                continue;
            }

            var measured = Glyphs(text, run.Face).Width;

            if (width > 0 && width + measured > room)
            {
                Row(into, line, x);
                width = 0;
            }

            line.Add((run, text));
            width += measured;
        }

        Row(into, line, x);
    }

    /// <summary>
    /// The runs cut where a line may break: a word with whatever space followed it, so the space goes at the end of a
    /// line rather than at the start of the next.
    /// </summary>
    private static IEnumerable<(Run Run, string Text, bool Breaks)> Chunks(IReadOnlyList<Run> runs)
    {
        foreach (var run in runs)
        {
            if (run.Part.Kind == MarkdownKinds.Break && run.Text.Length == 0)
            {
                yield return (run, string.Empty, true);

                continue;
            }

            var at = 0;

            while (at < run.Text.Length)
            {
                var end = at;
                while (end < run.Text.Length && !char.IsWhiteSpace(run.Text[end])) end++;
                if (end == at) end++;
                while (end < run.Text.Length && char.IsWhiteSpace(run.Text[end])) end++;

                yield return (run, run.Text[at..end], false);
                at = end;
            }
        }
    }

    /// <summary>
    /// One line, set. Everything on it that is set the same way and stands for the same part is joined back into one
    /// piece, because a run of text is one piece — and the pieces are sat on a shared baseline, which is what keeps a
    /// superscript beside its word rather than above its own line.
    /// </summary>
    private void Row(LayoutBuilder into, List<(Run Run, string Text)> line, double x)
    {
        if (line.Count == 0) return;

        var groups = new List<(Run Run, FormattedText Glyphs)>();
        var at = 0;

        while (at < line.Count)
        {
            var run = line[at].Run;
            var text = new StringBuilder();

            while (at < line.Count && line[at].Run.Equals(run)) text.Append(line[at++].Text);

            groups.Add((run, Glyphs(text.ToString(), run.Face)));
        }

        line.Clear();

        var baseline = groups.Max(group => group.Glyphs.Baseline);
        var height = groups.Max(group => group.Glyphs.Height + Math.Abs(group.Run.Face.Lift) * Style.TextSize);
        var cursor = x;

        foreach (var (run, glyphs) in groups)
        {
            var top = _y + baseline - glyphs.Baseline + (run.Face.Lift * Style.TextSize);

            Set(into, run, glyphs, cursor, top);
            cursor += glyphs.Width;
        }

        _y += height;
        Reached(cursor);
    }

    /// <summary>One run of one line, with whatever is washed behind it and whatever a press on it means.</summary>
    private void Set(LayoutBuilder into, Run run, FormattedText glyphs, double x, double top)
    {
        if (run.Face.Wash is { } wash)
        {
            var pad = Style.TextSize * 0.12;

            into.Open(MarkdownPieces.Block, run.Part, new Point(x - pad, top));
            into.Draw(new WashMark(new Rect(0, 0, glyphs.Width + (pad * 2), glyphs.Height), wash));
            into.Close();
        }

        if (run.Act is not { } act)
        {
            LayoutText.Words(into, glyphs, new Point(x, top), Math.Max(glyphs.Width, 1), TextAlignment.Left,
                             run.Part, MarkdownPieces.Words, maps: run.Maps, writes: !run.Maps,
                             ink: run.Face.Ink ?? Style.Text);

            return;
        }

        // What answers a press is the piece round the words rather than the words, so what was pressed and what was
        // read are the same question asked of the same piece.
        into.Open(MarkdownPieces.Block, run.Part, new Point(x, top));
        into.Acts(new LayoutActions { Click = act });

        LayoutText.Words(into, glyphs, default, Math.Max(glyphs.Width, 1), TextAlignment.Left,
                         run.Part, MarkdownPieces.Words, maps: run.Maps, writes: !run.Maps,
                         ink: run.Face.Ink ?? Style.Text);

        into.Close();
    }

    // ── Type ────────────────────────────────────────────────────────────────

    private FormattedText Glyphs(string text, Face face)
    {
        var glyphs = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(face.Mono ? new FontFamily(MonoFont) : Style.TextFont,
                         face.Italic ? FontStyles.Italic : FontStyles.Normal,
                         face.Bold ? FontWeights.Bold : FontWeights.Normal,
                         FontStretches.Normal),
            Math.Max(1, Style.TextSize * (face.Scale <= 0 ? 1 : face.Scale)),
            face.Ink ?? Style.Text,
            LayoutText.Density);

        if (face.Strike || face.Underline)
        {
            var decorations = new TextDecorationCollection();
            if (face.Strike) decorations.Add(TextDecorations.Strikethrough);
            if (face.Underline) decorations.Add(TextDecorations.Underline);

            glyphs.SetTextDecorations(decorations);
        }

        return glyphs;
    }
}
