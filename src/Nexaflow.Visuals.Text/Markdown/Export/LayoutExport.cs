using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Export;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown.Export;

/// <summary>
/// What an export is for: how wide the page is laid, in which palette, and whether what another language draws is kept as the
/// picture it is drawn as — which costs a painting each, and which copying to the clipboard has no use for.
/// </summary>
public sealed record ExportOccasion
{
    /// <summary>How wide the document is laid, in device-independent pixels — what a picture of a block is as wide as.</summary>
    public double Width { get; init; } = 640;

    /// <summary>The palette it is drawn in: a page is white unless somebody says otherwise.</summary>
    public StyleFormat Style { get; init; } = StyleFormat.Light;

    /// <summary>Whether what another language draws is painted, so a format that cannot draw the language puts the picture in.</summary>
    public bool Pictures { get; init; }

    /// <summary>How many pixels a picture gives each device-independent one.</summary>
    public double Scale { get; init; } = 1;
}

/// <summary>
/// A document laid out, as the format-neutral document a writer spells (<see cref="ExportDocument"/>).
///
/// <para>
/// <strong>What is written out is the document as it is shown.</strong> What each piece of it <em>is</em> — a heading and its
/// rank, a list and how it counts, a cell and how it is aligned, a link and where it goes — is the part of the reading it was
/// laid from. What it <em>looks like</em> is the layout's: the colour an alert is drawn in and the word over it, the number an
/// item is drawn with, the box ticked — and what another language drew there, which is the picture its pieces paint. So this
/// walks the reading in order and, for each part, looks at the pieces laid from it; nothing here reads source or works out
/// again what a stage or a builder already did.
/// </para>
/// </summary>
public static class LayoutExport
{
    /// <summary><paramref name="markdown"/>, laid out read-only for <paramref name="occasion"/> and walked.</summary>
    public static ExportDocument Exported(string markdown, ExportOccasion occasion)
    {
        var laid = new ContentEngine().Lay(null, EditState.For(markdown), occasion.Style, occasion.Width, readOnly: true);

        return Exported(laid, occasion);
    }

    /// <summary>A document already laid out, walked.</summary>
    public static ExportDocument Exported(Laid laid, ExportOccasion occasion) =>
        laid.Root.Part is ContentPart document ? new Walk(laid, occasion).Document(document) : new ExportDocument([]);

    /// <summary>One walk over one layout: the reading in order, and for each part, the pieces laid from it.</summary>
    private sealed class Walk
    {
        private readonly Laid _laid;
        private readonly ExportOccasion _occasion;
        private readonly Dictionary<ContentPart, List<Piece>> _pieces = new(ReferenceEqualityComparer.Instance);

        public Walk(Laid laid, ExportOccasion occasion)
        {
            _laid = laid;
            _occasion = occasion;

            foreach (var piece in laid.Root.SelfAndDescendants())
            {
                if (piece.Part is not ContentPart part) continue;

                if (!_pieces.TryGetValue(part, out var laidFrom)) _pieces[part] = laidFrom = [];
                laidFrom.Add(piece);
            }
        }

        public ExportDocument Document(ContentPart document) => new(this.Blocks(document));

        // ── Blocks ──────────────────────────────────────────────────────────

        /// <summary>Every block <paramref name="holder"/> holds — not the marks between them, and not what is on no page.</summary>
        private List<ExportBlock> Blocks(ContentPart holder)
        {
            var blocks = new List<ExportBlock>(holder.Children.Count);

            foreach (var part in holder.Children)
            {
                if (part.Derived || part.Role == Roles.Trivia) continue;
                if (part.Kind is MarkdownKinds.Task or MarkdownKinds.Marker or MarkdownKinds.FrontMatter or MarkdownKinds.Reference) continue;

                if (this.Block(part) is { } block) blocks.Add(block);
            }

            return blocks;
        }

        private ExportBlock? Block(ContentPart part) => part.Kind switch
        {
            MarkdownKinds.Heading => new ExportHeading(Rank(part), Held<string>(part, MarkdownKinds.Anchor), this.Words(Body(part))),
            MarkdownKinds.Rule => new ExportRule(),
            MarkdownKinds.Page => new ExportPageBreak(),
            MarkdownKinds.Quote => new ExportQuote(this.Blocks(Body(part))),
            MarkdownKinds.Alert => new ExportQuote(this.Blocks(Body(part)), this.Callout(part)),
            MarkdownKinds.List => this.Listed(part),
            MarkdownKinds.Item => new ExportList([this.Item(part)], null, Loose: false),
            MarkdownKinds.Table => this.Tabled(part),
            MarkdownKinds.Definition => new ExportDefinitions(this.Entries(Body(part).Children)),
            MarkdownKinds.Term => new ExportTerm(this.Words(Body(part))),
            MarkdownKinds.Described => new ExportDefinitions(this.Entries([part])),
            MarkdownKinds.Figure => new ExportFigure(this.Blocks(Body(part))),
            MarkdownKinds.Caption => new ExportCaption(this.Words(Body(part))),
            MarkdownKinds.Footer => new ExportFooter(this.Blocks(Body(part))),
            MarkdownKinds.Html => new ExportMarkup(Held(part)),
            MarkdownKinds.Code or MarkdownKinds.Written => new ExportCode(Held(part)),
            Kinds.Block => this.Fenced(part),
            _ => new ExportParagraph(this.Words(Body(part)), part.Node.HeldAs(MarkdownRoles.Typeset) as ParagraphLook),
        };

        /// <summary>
        /// The terms and what they mean, in the order they were written. The reading holds a term with what explains it as one
        /// piece and what explains it as another of the same kind inside it, so each is told by what it holds: one holding a term is
        /// the pair, and its own entries are the list's; any other is what a term means.
        /// </summary>
        private List<ExportBlock> Entries(IEnumerable<ContentPart> blocks)
        {
            var entries = new List<ExportBlock>();

            foreach (var part in blocks)
            {
                if (part.Derived || part.Role == Roles.Trivia) continue;

                if (part.Kind == MarkdownKinds.Term) entries.Add(new ExportTerm(this.Words(Body(part))));
                else if (part.Kind != MarkdownKinds.Described) entries.Add(this.Block(part) ?? new ExportDescription([]));
                else if (Body(part).Children.Any(child => child.Kind == MarkdownKinds.Term)) entries.AddRange(this.Entries(Body(part).Children));
                else entries.Add(new ExportDescription(this.Blocks(Body(part))));
            }

            return entries;
        }

        /// <summary>
        /// A fence: what its language drew, where it drew a picture of what was written — and otherwise the code it is, which is
        /// what a fence in a language that shows its characters is, and one that nothing could read or draw.
        /// </summary>
        private ExportBlock Fenced(ContentPart part)
        {
            var named = (part.Node as BlockNode)?.Language;

            return this.Drawing(part) is { } drawing
                ? new ExportDrawn(drawing)
                : new ExportCode(part.Part(Roles.Body) is { Kind: Kinds.Nested } body ? ContentNested.Own(body.Node) : Held(part),
                                 string.IsNullOrEmpty(named) ? null : named);
        }

        /// <summary>
        /// What another language drew of <paramref name="part"/>, where it drew a picture rather than showing what was written —
        /// its source, and the picture its pieces paint where the occasion keeps pictures — or null where it did not.
        /// </summary>
        private ExportDrawing? Drawing(ContentPart part)
        {
            if (ContentNested.Language(part) is not { } named || part.Part(Roles.Body) is not { } body) return null;
            if (ContentLanguages.For(named) is not { } language || language.Editing.ShowsWhatWasWritten) return null;
            if (this.Laid(part) is not { } drawn || Shown(drawn)) return null;

            return new ExportDrawing(named, ContentNested.Own(body.Node), _occasion.Pictures ? this.Picture(drawn) : null);
        }

        /// <summary>Whether a piece shows the characters it was laid from rather than a picture of what they meant.</summary>
        private static bool Shown(Piece drawn) =>
            drawn.SelfAndDescendants().Any(piece => piece.Kind is MarkdownPieces.Verbatim or LayoutText.SourceKind or SourceShown.Block);

        /// <summary>What a piece paints, as a picture of its own: as much of the page as it was laid over.</summary>
        private ExportPicture? Picture(Piece drawn)
        {
            var box = drawn.Bounds;

            if (LayoutPictures.Painted(_laid.Root, box, _occasion.Scale, _occasion.Style.Text, null) is not { } painted) return null;

            return new ExportPicture(LayoutPictures.Png(painted), box.Width, box.Height);
        }

        /// <summary>What an alert says it is, and the word and the colour it is drawn with.</summary>
        private ExportCallout Callout(ContentPart alert)
        {
            var marker = Body(alert).Children.FirstOrDefault(child => child.Kind == MarkdownKinds.Marker);
            var name = marker?.Part(Roles.Name)?.Text ?? string.Empty;
            var label = marker is null ? null : this.Drawn(marker, MarkdownPieces.Words);
            var bar = this.Laid(alert) is { } drawn ? drawn.Marks.ToArray().OfType<RuleMark>().FirstOrDefault()?.Foreground : null;

            return new ExportCallout(name, label ?? name, Argb(bar ?? _occasion.Style.Accent));
        }

        /// <summary>A list, each item numbered as it is drawn.</summary>
        private ExportList Listed(ContentPart list)
        {
            var body = Body(list);
            var counting = body.Children.Select(child => child.Node.Held).OfType<MarkdownNumbering>().FirstOrDefault();
            var loose = body.Children.Any(child => child.Kind == MarkdownKinds.Loose);
            var items = body.Children.Where(child => child.Kind == MarkdownKinds.Item).Select(this.Item).ToList();

            return new ExportList(items, counting, loose);
        }

        /// <summary>One item: what it holds, the marker it is drawn with, and its box where it has one.</summary>
        private ExportItem Item(ContentPart item)
        {
            var written = item.Children.FirstOrDefault(child => child.Role == Roles.Trivia && !child.Derived);
            var marker = (written is null ? null : this.Drawn(written, MarkdownPieces.Marker)) ?? this.Drawn(item, MarkdownPieces.Marker) ?? "•";
            var tick = item.Children.FirstOrDefault(child => child.Kind == MarkdownKinds.Task);

            return new ExportItem(this.Blocks(item), marker, tick is null ? null : tick.Part(MarkdownRoles.Done) is not null);
        }

        /// <summary>
        /// A table, row by row, each cell with what it holds and how it is set — and as wide as its widest row, as it is drawn: a
        /// row written short is drawn across every column all the same, and a cell written down through the rows under it covers
        /// its columns in each.
        /// </summary>
        private ExportTable Tabled(ContentPart table)
        {
            var rows = new List<ExportRow>();
            var down = new List<(int Until, int Across)>();
            var columns = 0;

            foreach (var row in Body(table).Children)
            {
                if (row.Kind != MarkdownKinds.Row) continue;

                var cells = new List<ExportCell>();
                var at = rows.Count;
                var covered = down.Where(cell => cell.Until > at).Sum(cell => cell.Across);

                foreach (var cell in row.Children)
                {
                    if (cell.Kind != MarkdownKinds.Cell) continue;

                    var held = Body(cell);
                    var spans = Held<MarkdownSpans>(cell, MarkdownKinds.Spans);
                    var written = new ExportCell(held.Kind == Kinds.Words ? [new ExportWords(this.Words(held))] : this.Blocks(held),
                                                 Held<string>(cell, MarkdownKinds.Aligned), spans?.Across ?? 1, spans?.Down ?? 1);

                    if (written.Down > 1) down.Add((at + written.Down, written.Across));
                    cells.Add(written);
                }

                columns = Math.Max(columns, covered + cells.Sum(cell => cell.Across));
                rows.Add(new ExportRow(cells, row.Role == MarkdownRoles.Head, covered));
            }

            return new ExportTable(rows, columns);
        }

        // ── Runs of a line ──────────────────────────────────────────────────

        /// <summary>The runs a part of words is written in, as they read.</summary>
        private List<ExportInline> Words(ContentPart words)
        {
            var runs = new List<ExportInline>();
            this.Gather(words, runs);

            return runs;
        }

        private void Gather(ContentPart part, List<ExportInline> runs)
        {
            if (part.Derived) return;

            switch (part.Kind)
            {
                case MarkdownKinds.Strong: this.Styled(ExportStyle.Strong, part, runs); return;
                case MarkdownKinds.Emphasis: this.Styled(ExportStyle.Emphasis, part, runs); return;
                case MarkdownKinds.Strike: this.Styled(ExportStyle.Strike, part, runs); return;
                case MarkdownKinds.Insert: this.Styled(ExportStyle.Insert, part, runs); return;
                case MarkdownKinds.Mark: this.Styled(ExportStyle.Mark, part, runs); return;
                case MarkdownKinds.Sub: this.Styled(ExportStyle.Sub, part, runs); return;
                case MarkdownKinds.Sup: this.Styled(ExportStyle.Sup, part, runs); return;
                case MarkdownKinds.Citation: this.Styled(ExportStyle.Citation, part, runs); return;

                case MarkdownKinds.Styled when part.Node.HeldAs(MarkdownRoles.Typeset) is TextLook look:
                    runs.Add(new ExportLooked(look, this.Inside(part)));
                    return;

                case MarkdownKinds.Encoded:
                    if (part.Node.HeldAs(MarkdownRoles.Decoded) is string { Length: > 0 } decoded) Text(decoded, runs);
                    return;

                case MarkdownKinds.Tab:
                    runs.Add(new ExportTab());
                    return;

                case MarkdownKinds.Code when part.Part(Roles.Body) is { } code:
                    runs.Add(new ExportCodeSpan(Spanned(code)));
                    return;

                case MarkdownKinds.Image:
                    runs.Add(new ExportImage(MarkdownLinks.Address(part.Node) ?? string.Empty, Titled(part),
                                             part.Part(Roles.Body) is { } alt ? this.Words(alt) : [], MarkdownPictureSize.Of(part.Node)));
                    return;

                case Kinds.Link:
                    this.Linked(part, runs);
                    return;

                case MarkdownKinds.Entity:
                    Text(MarkdownEntities.Decoded(part.Text) ?? part.Text, runs);
                    return;

                case MarkdownKinds.Escape when part.Text.Length > 1:
                    Text(part.Text[1..], runs);
                    return;

                case MarkdownKinds.Abbreviation when part.Part(Roles.Body) is { } word:
                    runs.Add(Held<string>(part, MarkdownKinds.Abbreviation) is { } meaning
                        ? new ExportAbbreviation(word.Text, meaning)
                        : new ExportText(word.Text));
                    return;

                case MarkdownKinds.Html:
                    runs.Add(new ExportMarkupSpan(Joined(part)));
                    return;

                case MarkdownKinds.Break:
                    if (!MarkdownLineEnds.Closes(part)) runs.Add(new ExportBreak(part.Role == MarkdownRoles.Hard));
                    return;

                case MarkdownKinds.Task:
                    return;

                case Kinds.Block:
                case MarkdownKinds.Formula:
                    runs.Add(this.Drawing(part) is { } drawing ? new ExportDrawnSpan(drawing) : new ExportCodeSpan(Joined(part)));
                    return;
            }

            if (part.Children.Count > 0)
            {
                foreach (var child in part.Children) this.Gather(child, runs);
                return;
            }

            if (part.Role == Roles.Silent) return;

            if (part.Role is Roles.Open or Roles.Close or Roles.Name or Roles.Trivia)
            {
                // Machinery is not written. Where it ran over a line ending, though, it stood between two words.
                if (part.Text.Contains('\n') && !MarkdownLineEnds.Closes(part)) Text(" ", runs);
                return;
            }

            if (part.Kind == Kinds.Space && part.Text.Contains('\n') && MarkdownLineEnds.Closes(part)) return;

            if (part.Text.Length > 0) Text(part.Text.Replace('\n', ' ').Replace('\r', ' '), runs);
        }

        /// <summary>The runs inside a construct, its marks left out.</summary>
        private List<ExportInline> Inside(ContentPart part)
        {
            var runs = new List<ExportInline>();
            foreach (var child in part.Children) this.Gather(child, runs);

            return runs;
        }

        private void Styled(ExportStyle style, ContentPart part, List<ExportInline> runs) => runs.Add(new ExportStyled(style, this.Inside(part)));

        /// <summary>
        /// A link: its words going where it goes — nowhere at all being somewhere, for a link written with nothing between its
        /// brackets — and an address written as a link to itself being its own words.
        /// </summary>
        private void Linked(ContentPart part, List<ExportInline> runs)
        {
            var where = MarkdownLinks.Address(part.Node);

            // Written with words in brackets, or by name: a link whatever its words and wherever it goes.
            if (part.Part(Roles.Open)?.Text is "[" or "![")
            {
                runs.Add(new ExportLink(where ?? string.Empty, Titled(part), part.Part(Roles.Body) is { } body ? this.Words(body) : []));
                return;
            }

            if (where is null)
            {
                foreach (var child in part.Children) this.Gather(child, runs);
                return;
            }

            runs.Add(new ExportLink(where, Titled(part), [new ExportText(MarkdownLinks.Goes(part.Node) ?? where)]));
        }

        // ── What the layout says ────────────────────────────────────────────

        /// <summary>The first piece laid from <paramref name="part"/>, or null where nothing was.</summary>
        private Piece? Laid(ContentPart part) => _pieces.TryGetValue(part, out var laid) && laid.Count > 0 ? laid[0] : null;

        /// <summary>The words a piece of <paramref name="kind"/> laid from <paramref name="part"/> draws — drawn, not written: a marker's number, an alert's word.</summary>
        private string? Drawn(ContentPart part, string kind)
        {
            if (!_pieces.TryGetValue(part, out var laid)) return null;

            foreach (var piece in laid)
                if (piece.Kind == kind && piece.Words is { } words) return words.Glyphs.Text;

            return null;
        }

        /// <summary>A brush's colour as <c>0xAARRGGBB</c> — the reader's accent where it is no single colour.</summary>
        private uint Argb(Brush brush)
        {
            var colour = brush is SolidColorBrush solid ? solid.Color
                : _occasion.Style.Accent is SolidColorBrush accent ? accent.Color
                : Colors.Black;

            return ((uint)colour.A << 24) | ((uint)colour.R << 16) | ((uint)colour.G << 8) | colour.B;
        }

        // ── What the reading says ───────────────────────────────────────────

        /// <summary>What a block was read into, or the block itself where nothing read it.</summary>
        private static ContentPart Body(ContentPart part) => part.Part(Roles.Body) ?? part;

        /// <summary>How deep a heading is, as the reader counted it.</summary>
        private static int Rank(ContentPart part) =>
            int.TryParse(part.Node.Said(MarkdownRoles.Rank), System.Globalization.CultureInfo.InvariantCulture, out var rank) ? Math.Clamp(rank, 1, 6) : 1;

        /// <summary>What a stage hung on <paramref name="part"/> as <paramref name="kind"/>, where it is a <typeparamref name="T"/>.</summary>
        private static T? Held<T>(ContentPart part, string kind) where T : class
        {
            foreach (var child in part.Children)
            {
                if (!child.Derived || child.Kind != kind) continue;
                if (child.Node.Held is T held) return held;

                foreach (var inner in child.Children)
                    if (inner.Node.Held is T found) return found;
            }

            return null;
        }

        /// <summary>
        /// The characters a block holds as written: its lines, without what a container put in front of each — and without the line
        /// ending closing the last, which the reading holds as a piece of its own (<see cref="MarkdownClosingLines"/>).
        /// </summary>
        private static string Held(ContentPart part)
        {
            if (part.Part(Roles.Body) is not { } body) return string.Empty;

            var said = new StringBuilder(body.Length);

            foreach (var leaf in body.Node.Leaves())
                if (leaf.Role != Roles.Trivia) said.Append(leaf.Text);

            return said.ToString();
        }

        /// <summary>
        /// The characters of a run written over several lines: what is written on each, and between two of them the line ending
        /// that parts them — without what a container put in front of the next.
        /// </summary>
        private static string Joined(ContentPart part)
        {
            var said = new StringBuilder();

            foreach (var leaf in part.Node.Leaves())
            {
                if (leaf.Role != Roles.Trivia) said.Append(leaf.Text);
                else if (leaf.Text.Contains('\n')) said.Append('\n');
            }

            return said.ToString();
        }

        /// <summary>
        /// What a code span says: its characters, a line ending among them being a space, and one space at either end taken off
        /// where both ends have one and it is not all spaces — the room a writer leaves so a backtick can open or close it.
        /// </summary>
        private static string Spanned(ContentPart code)
        {
            var said = Joined(code).Replace("\r\n", " ").Replace('\n', ' ');

            return said.Length >= 2 && said[0] == ' ' && said[^1] == ' ' && !string.IsNullOrWhiteSpace(said) ? said[1..^1] : said;
        }

        /// <summary>
        /// What a link or a picture says about itself when rested on, escapes and entities read — its own title, or its definition's
        /// where it is a link by name — or null where it says nothing.
        /// </summary>
        private static string? Titled(ContentPart link) =>
            link.Part(MarkdownRoles.Title) is { } title ? MarkdownLinkSyntax.Unescaped(Joined(title))
            : link.Node.HeldAs(MarkdownRoles.Title) as string;

        /// <summary>Characters onto the end of a run of words, joined to the characters before them.</summary>
        private static void Text(string text, List<ExportInline> runs)
        {
            if (runs.Count > 0 && runs[^1] is ExportText before) runs[^1] = new ExportText(before.Text + text);
            else runs.Add(new ExportText(text));
        }
    }
}
