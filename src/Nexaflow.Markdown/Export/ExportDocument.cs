using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Export;

/// <summary>
/// A document as it is shown, in no format at all: what each block and run of words is, and what it looks like where how it
/// looks is not the reader's to choose — the colour an alert is in, the number an item is drawn with, the picture a diagram
/// is. Made by walking a document laid out, and written by a writer for each format (HTML, plain text, and Word and ODF when
/// they are wanted), so a new format is a writer and nothing else.
///
/// <para>
/// Only what a writer spells is here: nothing in it reads source or works anything out again, colours are values and
/// pictures are their encoded bytes, and no type in it is WPF's — so a writer can be written, and tested, without a desktop.
/// </para>
/// </summary>
/// <param name="Blocks">The blocks it is written in, in order.</param>
public sealed record ExportDocument(IReadOnlyList<ExportBlock> Blocks);

/// <summary>A block of a document: something set on lines of its own.</summary>
public abstract record ExportBlock;

/// <summary>A run of a line: words, and whatever a sentence holds besides.</summary>
public abstract record ExportInline;

// ── Blocks ──────────────────────────────────────────────────────────────

/// <summary>A heading.</summary>
/// <param name="Rank">How deep it is, one to six.</param>
/// <param name="Anchor">The name a link into the document reaches it by, where it has one.</param>
public sealed record ExportHeading(int Rank, string? Anchor, IReadOnlyList<ExportInline> Words) : ExportBlock;

/// <summary>A paragraph — and how its source said it is set, where it said (<see cref="ParagraphLook"/>).</summary>
public sealed record ExportParagraph(IReadOnlyList<ExportInline> Words, ParagraphLook? Look = null) : ExportBlock;

/// <summary>Words standing on their own rather than set as a paragraph: what a table's cell holds, where it holds a run of words.</summary>
public sealed record ExportWords(IReadOnlyList<ExportInline> Words) : ExportBlock;

/// <summary>A rule across the page.</summary>
public sealed record ExportRule : ExportBlock;

/// <summary>Where the writer's next page starts.</summary>
public sealed record ExportPageBreak : ExportBlock;

/// <summary>A quotation — or an alert, which says what it is for (<see cref="Callout"/>).</summary>
public sealed record ExportQuote(IReadOnlyList<ExportBlock> Blocks, ExportCallout? Callout = null) : ExportBlock;

/// <summary>What an alert says it is: the name written in it, the word it is drawn under, and the colour it is drawn in.</summary>
/// <param name="Name">The name written in its marker — <c>NOTE</c>, <c>WARNING</c>.</param>
/// <param name="Label">The word drawn for it — <c>Note</c>.</param>
/// <param name="Ink">Its colour, as <c>0xAARRGGBB</c>.</param>
public sealed record ExportCallout(string Name, string Label, uint Ink);

/// <summary>A list.</summary>
/// <param name="Counting">How it counts, where it counts at all; null for a list of bullets.</param>
/// <param name="Loose">Whether its items are set apart, each paragraph a paragraph — or run in, as a tight list's are.</param>
public sealed record ExportList(IReadOnlyList<ExportItem> Items, MarkdownNumbering? Counting, bool Loose) : ExportBlock;

/// <summary>One thing in a list.</summary>
/// <param name="Marker">What it is drawn with — a bullet, or its number in the list's alphabet.</param>
/// <param name="Done">Whether its box is ticked, for an item that has one.</param>
public sealed record ExportItem(IReadOnlyList<ExportBlock> Blocks, string Marker, bool? Done = null);

/// <summary>A table, row by row — every one of them drawn across all its columns, written in it or not.</summary>
/// <param name="Columns">How many columns it is drawn with: as many as its widest row covers.</param>
public sealed record ExportTable(IReadOnlyList<ExportRow> Rows, int Columns) : ExportBlock;

/// <summary>One row of a table — the head, or one of the rows under it.</summary>
/// <param name="Covered">How many of its columns a cell from a row above goes on down through.</param>
public sealed record ExportRow(IReadOnlyList<ExportCell> Cells, bool Head, int Covered = 0);

/// <summary>One cell of a row: what it holds, which way its column is set, and how many columns and rows it covers.</summary>
/// <param name="Align"><see cref="MarkdownAligns"/>, or null where nothing says.</param>
public sealed record ExportCell(IReadOnlyList<ExportBlock> Blocks, string? Align = null, int Across = 1, int Down = 1);

/// <summary>Terms and what they mean, in the order they were written (<see cref="ExportTerm"/>, <see cref="ExportDescription"/>).</summary>
public sealed record ExportDefinitions(IReadOnlyList<ExportBlock> Entries) : ExportBlock;

/// <summary>A term being explained.</summary>
public sealed record ExportTerm(IReadOnlyList<ExportInline> Words) : ExportBlock;

/// <summary>What a term means.</summary>
public sealed record ExportDescription(IReadOnlyList<ExportBlock> Blocks) : ExportBlock;

/// <summary>Something set apart with a caption (<see cref="ExportCaption"/>) among its blocks.</summary>
public sealed record ExportFigure(IReadOnlyList<ExportBlock> Blocks) : ExportBlock;

/// <summary>What a figure calls itself.</summary>
public sealed record ExportCaption(IReadOnlyList<ExportInline> Words) : ExportBlock;

/// <summary>What stands at the foot of the page.</summary>
public sealed record ExportFooter(IReadOnlyList<ExportBlock> Blocks) : ExportBlock;

/// <summary>Code, shown as the characters it is.</summary>
/// <param name="Text">Its lines, without the line ending closing the last.</param>
/// <param name="Language">The language it says it is written in, where it says one.</param>
public sealed record ExportCode(string Text, string? Language = null) : ExportBlock;

/// <summary>Markup written by hand, as written — a format that understands it takes it as it is, and any other shows nothing of it.</summary>
public sealed record ExportMarkup(string Text) : ExportBlock;

/// <summary>Content in another language that is drawn rather than shown as its characters — a diagram, a formula on a line of its own.</summary>
public sealed record ExportDrawn(ExportDrawing Drawing) : ExportBlock;

// ── Runs of a line ──────────────────────────────────────────────────────

/// <summary>Characters that are only themselves.</summary>
public sealed record ExportText(string Text) : ExportInline;

/// <summary>Words set the way markdown marks them (<see cref="ExportStyle"/>).</summary>
public sealed record ExportStyled(ExportStyle Style, IReadOnlyList<ExportInline> Words) : ExportInline;

/// <summary>The ways markdown marks words.</summary>
public enum ExportStyle
{
    Emphasis,
    Strong,
    Strike,
    Mark,
    Insert,
    Sub,
    Sup,
    Citation,
}

/// <summary>Words set the way their source spells out in so many words — a font, a size, a colour (<see cref="TextLook"/>).</summary>
public sealed record ExportLooked(TextLook Look, IReadOnlyList<ExportInline> Words) : ExportInline;

/// <summary>Code in the middle of a sentence, as the characters it is.</summary>
public sealed record ExportCodeSpan(string Text) : ExportInline;

/// <summary>Words that go somewhere.</summary>
/// <param name="Destination">Where, as it means — escapes and entities read.</param>
/// <param name="Title">What it says about itself when rested on, where it says anything.</param>
public sealed record ExportLink(string Destination, string? Title, IReadOnlyList<ExportInline> Words) : ExportInline;

/// <summary>A picture named by where it is.</summary>
/// <param name="Words">The words written instead of it — what a format that cannot show it says in its place.</param>
/// <param name="Size">The size it asks to be drawn at, where it asks.</param>
public sealed record ExportImage(string Source, string? Title, IReadOnlyList<ExportInline> Words, MarkdownPictureSize? Size = null) : ExportInline;

/// <summary>The end of a line: one the writer asked to keep, or one a format may reflow.</summary>
public sealed record ExportBreak(bool Hard) : ExportInline;

/// <summary>A tab: the rest of the line moves on to the next stop.</summary>
public sealed record ExportTab : ExportInline;

/// <summary>A word somebody said what it stands for.</summary>
public sealed record ExportAbbreviation(string Text, string Meaning) : ExportInline;

/// <summary>Markup written by hand in a sentence.</summary>
public sealed record ExportMarkupSpan(string Text) : ExportInline;

/// <summary>Content in another language drawn in a sentence — a formula.</summary>
public sealed record ExportDrawnSpan(ExportDrawing Drawing) : ExportInline;

// ── What another language draws ─────────────────────────────────────────

/// <summary>
/// What another language drew, in every form an occasion might want it in: its source, which a format that draws the language
/// itself takes (a browser that runs Mermaid), and the picture it is drawn as, where the occasion asked for pictures.
/// </summary>
/// <param name="Language">The language it is written in.</param>
/// <param name="Source">What is written in it.</param>
/// <param name="Picture">What it is drawn as, where the walk was asked to keep pictures.</param>
public sealed record ExportDrawing(string Language, string Source, ExportPicture? Picture = null);

/// <summary>A picture, encoded: its PNG bytes, and the size it is drawn at.</summary>
/// <param name="Width">How wide it is drawn, in device-independent pixels.</param>
/// <param name="Height">How tall.</param>
public sealed record ExportPicture(byte[] Png, double Width, double Height);
