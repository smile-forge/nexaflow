using System.Globalization;
using System.Text;

using Nexaflow.Markdown.Export;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents;

/// <summary>What HTML is being written for: where a browser will run what a page holds, and where it will only be pasted.</summary>
public sealed record HtmlWriting
{
    /// <summary>Whether every heading carries the name a link reaches it by, as its <c>id</c>.</summary>
    public bool Anchors { get; init; }

    /// <summary>
    /// Whether what another language drew is written for a script to draw where the page is shown — a Mermaid diagram as its
    /// source for mermaid.js, a formula for a typesetter — rather than as its code. A picture, where the document holds one,
    /// is put in either way.
    /// </summary>
    public bool Scripts { get; init; }
}

/// <summary>
/// A document as HTML: a fragment of a page's body, written the way CommonMark's own renderer writes it — the one check on the
/// reading that does not go through Markdig is that every example of CommonMark's specification comes out as the HTML it
/// gives — and the extensions the way Markdig's renderers write them.
///
/// <para>
/// What the layout decided is written as it was decided: an alert carries the colour it is drawn in and the word it is drawn
/// under, and what another language drew is the picture it is drawn as, where the document holds one. A host that means
/// something of its own by a link or a picture — a page among others, a picture stored beside it — says so by what it makes
/// of them (<see cref="WriteLink"/>, <see cref="Source"/>).
/// </para>
/// </summary>
public class HtmlDocumentWriter(HtmlWriting? writing = null) : IDocumentWriter
{
    private readonly HtmlWriting _writing = writing ?? new HtmlWriting();

    /// <inheritdoc/>
    public void Write(ExportDocument document, Stream into)
    {
        var bytes = Encoding.UTF8.GetBytes(this.Written(document));
        into.Write(bytes, 0, bytes.Length);
    }

    /// <summary><paramref name="document"/> as HTML.</summary>
    public string Written(ExportDocument document)
    {
        var html = new StringBuilder();
        this.Blocks(document.Blocks, html, tight: false);

        return html.ToString();
    }

    /// <summary>A run of words as HTML on its own — what a line written somewhere that is not a document comes to.</summary>
    public string Written(IReadOnlyList<ExportInline> words)
    {
        var html = new StringBuilder();
        this.Inlines(words, html);

        return html.ToString();
    }

    // ── Blocks ──────────────────────────────────────────────────────────────

    /// <param name="tight">Whether these are the blocks of an item of a tight list, whose paragraphs are run into it.</param>
    private void Blocks(IReadOnlyList<ExportBlock> blocks, StringBuilder html, bool tight, bool? box = null)
    {
        foreach (var block in blocks)
        {
            this.Block(block, html, tight, box);
            box = null;
        }
    }

    /// <param name="box">The box of the item these blocks are in, still to be written in front of its first words.</param>
    private void Block(ExportBlock block, StringBuilder html, bool tight, bool? box)
    {
        switch (block)
        {
            case ExportParagraph paragraph when tight:
                Box(box, html);
                this.Inlines(paragraph.Words, html);
                return;

            case ExportParagraph paragraph:
                Cr(html);
                html.Append("<p>");
                Box(box, html);
                this.Inlines(paragraph.Words, html);
                html.Append("</p>");
                Cr(html);
                return;

            case ExportWords words:
                Box(box, html);
                this.Inlines(words.Words, html);
                return;

            case ExportHeading heading:
                Cr(html);
                html.Append("<h").Append(heading.Rank);
                if (_writing.Anchors && heading.Anchor is { Length: > 0 } anchor) html.Append(" id=\"").Append(Escaped(anchor)).Append('"');
                html.Append('>');
                this.Inlines(heading.Words, html);
                html.Append("</h").Append(heading.Rank).Append('>');
                Cr(html);
                return;

            case ExportRule:
                Cr(html);
                html.Append("<hr />");
                Cr(html);
                return;

            case ExportPageBreak:
                Cr(html);
                html.Append("<div style=\"page-break-after: always;\"></div>");
                Cr(html);
                return;

            case ExportQuote { Callout: { } callout } alert:
                Cr(html);
                html.Append("<div class=\"markdown-alert markdown-alert-").Append(Escaped(callout.Name.ToLowerInvariant()))
                    .Append("\" style=\"border-left: 4px solid ").Append(Colour(callout.Ink)).Append("; padding-left: 1em;\">");
                Cr(html);
                html.Append("<p class=\"markdown-alert-title\" style=\"color: ").Append(Colour(callout.Ink)).Append(";\">")
                    .Append(Escaped(callout.Label)).Append("</p>");
                Cr(html);
                this.Blocks(alert.Blocks, html, tight: false);
                Cr(html);
                html.Append("</div>");
                Cr(html);
                return;

            case ExportQuote quote:
                Cr(html);
                html.Append("<blockquote>");
                Cr(html);
                this.Blocks(quote.Blocks, html, tight: false);
                Cr(html);
                html.Append("</blockquote>");
                Cr(html);
                return;

            case ExportList list:
                this.Listed(list, html);
                return;

            case ExportTable table:
                this.Tabled(table, html);
                return;

            case ExportDefinitions definitions:
                Cr(html);
                html.Append("<dl>");
                Cr(html);
                this.Blocks(definitions.Entries, html, tight: false);
                html.Append("</dl>");
                Cr(html);
                return;

            case ExportTerm term:
                html.Append("<dt>");
                this.Inlines(term.Words, html);
                html.Append("</dt>");
                Cr(html);
                return;

            case ExportDescription description:
                html.Append("<dd>");
                this.Blocks(description.Blocks, html, tight: description.Blocks is [ExportParagraph]);
                html.Append("</dd>");
                Cr(html);
                return;

            case ExportFigure figure:
                Cr(html);
                html.Append("<figure>");
                Cr(html);
                this.Blocks(figure.Blocks, html, tight: false);
                html.Append("</figure>");
                Cr(html);
                return;

            case ExportCaption caption:
                Cr(html);
                html.Append("<figcaption>");
                this.Inlines(caption.Words, html);
                html.Append("</figcaption>");
                Cr(html);
                return;

            case ExportFooter footer:
                Cr(html);
                html.Append("<footer>");
                Cr(html);
                this.Blocks(footer.Blocks, html, tight: footer.Blocks is [ExportParagraph]);
                html.Append("</footer>");
                Cr(html);
                return;

            case ExportCode code:
                Coded(code.Text, code.Language, html);
                return;

            case ExportMarkup markup:
                Cr(html);
                html.Append(markup.Text);
                Cr(html);
                return;

            case ExportDrawn drawn:
                this.Drawn(drawn.Drawing, html, block: true);
                return;
        }
    }

    /// <summary>
    /// A list: numbered from where it starts and in the alphabet it counts in, and each item's paragraphs run into it where
    /// the list is tight.
    /// </summary>
    private void Listed(ExportList list, StringBuilder html)
    {
        var tag = list.Counting is null ? "ul" : "ol";

        Cr(html);
        html.Append('<').Append(tag);

        if (list.Counting is { } counting)
        {
            if (counting.Bullet is 'a' or 'A' or 'i' or 'I') html.Append(" type=\"").Append(counting.Bullet).Append('"');
            if (counting.Start != 1) html.Append(" start=\"").Append(counting.Start.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        if (list.Items.Any(item => item.Done is not null)) html.Append(" class=\"contains-task-list\"");

        html.Append('>');
        Cr(html);

        foreach (var item in list.Items)
        {
            html.Append(item.Done is null ? "<li>" : "<li class=\"task-list-item\">");

            // An item holding nothing but a box still shows the box.
            if (item.Blocks.Count == 0) Box(item.Done, html);
            else this.Blocks(item.Blocks, html, tight: !list.Loose, item.Done);

            html.Append("</li>");
            Cr(html);
        }

        html.Append("</").Append(tag).Append('>');
        Cr(html);
    }

    /// <summary>A table: the head's rows, then the body's, each cell set the way its column is and spanning what it covers.</summary>
    private void Tabled(ExportTable table, StringBuilder html)
    {
        Cr(html);
        html.Append("<table>");
        Cr(html);

        var head = table.Rows.TakeWhile(row => row.Head).ToList();
        var body = table.Rows.Skip(head.Count).ToList();

        if (head.Count > 0) this.Rows("thead", "th", head, table.Columns, html);
        if (body.Count > 0) this.Rows("tbody", "td", body, table.Columns, html);

        html.Append("</table>");
        Cr(html);
    }

    /// <param name="columns">How many columns every row is drawn across — what a row written short is made up to with empty cells.</param>
    private void Rows(string group, string cell, List<ExportRow> rows, int columns, StringBuilder html)
    {
        html.Append('<').Append(group).Append('>');
        Cr(html);

        foreach (var row in rows)
        {
            html.Append("<tr>");
            Cr(html);

            foreach (var written in row.Cells)
            {
                html.Append('<').Append(cell);
                if (written.Across > 1) html.Append(" colspan=\"").Append(written.Across.ToString(CultureInfo.InvariantCulture)).Append('"');
                if (written.Down > 1) html.Append(" rowspan=\"").Append(written.Down.ToString(CultureInfo.InvariantCulture)).Append('"');
                if (written.Align is { } align) html.Append(" style=\"text-align: ").Append(align).Append(";\"");
                html.Append('>');

                this.Blocks(written.Blocks, html, tight: false);

                html.Append("</").Append(cell).Append('>');
                Cr(html);
            }

            for (var empty = row.Covered + row.Cells.Sum(written => written.Across); empty < columns; empty++)
            {
                html.Append('<').Append(cell).Append("></").Append(cell).Append('>');
                Cr(html);
            }

            html.Append("</tr>");
            Cr(html);
        }

        html.Append("</").Append(group).Append('>');
        Cr(html);
    }

    /// <summary>Code as the characters it is, saying the language it is in where it says one.</summary>
    private static void Coded(string text, string? language, StringBuilder html)
    {
        Cr(html);
        html.Append("<pre><code");
        if (language is { Length: > 0 }) html.Append(" class=\"language-").Append(Escaped(language)).Append('"');
        html.Append('>');

        if (text.Length > 0) html.Append(Escaped(text)).Append('\n');

        html.Append("</code></pre>");
        Cr(html);
    }

    /// <summary>
    /// What another language drew: the picture it is drawn as, where the document holds one; for a page that runs scripts, the
    /// source a script draws it from; and otherwise the code it is written in.
    /// </summary>
    private void Drawn(ExportDrawing drawing, StringBuilder html, bool block)
    {
        if (drawing.Picture is { } picture)
        {
            if (block) Cr(html);

            html.Append("<img src=\"data:image/png;base64,").Append(Convert.ToBase64String(picture.Png))
                .Append("\" alt=\"").Append(Escaped(drawing.Source)).Append('"')
                .Append(" width=\"").Append(Pixels(picture.Width)).Append("\" height=\"").Append(Pixels(picture.Height)).Append("\" />");

            if (block) Cr(html);
            return;
        }

        if (_writing.Scripts && Scripted(drawing, block) is { } scripted)
        {
            if (block) Cr(html);
            html.Append(scripted);
            if (block) Cr(html);
            return;
        }

        if (block) Coded(drawing.Source, drawing.Language, html);
        else html.Append("<code class=\"language-").Append(Escaped(drawing.Language)).Append("\">").Append(Escaped(drawing.Source)).Append("</code>");
    }

    /// <summary>A drawing as a script on the page draws it from — mermaid.js a diagram, a typesetter a formula — or null where none would.</summary>
    private static string? Scripted(ExportDrawing drawing, bool block) => drawing.Language.ToLowerInvariant() switch
    {
        "mermaid" when block => $"<pre class=\"mermaid\">{Escaped(drawing.Source)}</pre>",
        "latex" or "math" or "tex" => block
            ? $"<div class=\"math\">\n\\[\n{Escaped(drawing.Source)}\n\\]</div>"
            : $"<span class=\"math\">\\({Escaped(drawing.Source)}\\)</span>",
        _ => null,
    };

    // ── Runs of a line ──────────────────────────────────────────────────────

    private void Inlines(IReadOnlyList<ExportInline> words, StringBuilder html)
    {
        foreach (var word in words) this.Inline(word, html);
    }

    private void Inline(ExportInline word, StringBuilder html)
    {
        switch (word)
        {
            case ExportText text:
                html.Append(Escaped(text.Text));
                return;

            case ExportStyled styled:
                var tag = Tag(styled.Style);
                html.Append('<').Append(tag).Append('>');
                this.Inlines(styled.Words, html);
                html.Append("</").Append(tag).Append('>');
                return;

            case ExportLooked looked:
                html.Append("<span style=\"").Append(Escaped(Looks(looked.Look))).Append("\">");
                this.Inlines(looked.Words, html);
                html.Append("</span>");
                return;

            case ExportCodeSpan code:
                html.Append("<code>").Append(Escaped(code.Text)).Append("</code>");
                return;

            case ExportLink link:
                this.WriteLink(link, html);
                return;

            case ExportImage image:
                html.Append("<img src=\"").Append(Escaped(Encoded(this.Source(image)))).Append("\" alt=\"").Append(Escaped(Plain(image.Words))).Append('"');
                if (image.Title is { } title) html.Append(" title=\"").Append(Escaped(title)).Append('"');
                if (image.Size is { } size)
                {
                    html.Append(" width=\"").Append(Pixels(size.Width)).Append('"');
                    if (size.Height is { } height) html.Append(" height=\"").Append(Pixels(height)).Append('"');
                }

                html.Append(" />");
                return;

            case ExportBreak { Hard: true }:
                html.Append("<br />\n");
                return;

            case ExportBreak:
                html.Append('\n');
                return;

            case ExportTab:
                html.Append('\t');
                return;

            case ExportAbbreviation abbreviation:
                html.Append("<abbr title=\"").Append(Escaped(abbreviation.Meaning)).Append("\">").Append(Escaped(abbreviation.Text)).Append("</abbr>");
                return;

            case ExportMarkupSpan markup:
                html.Append(markup.Text);
                return;

            case ExportDrawnSpan drawn:
                this.Drawn(drawn.Drawing, html, block: false);
                return;
        }
    }

    /// <summary>
    /// A link: its words, going where it goes. What a host makes of a destination of its own — another of its pages, a place
    /// in its window — is its to write instead.
    /// </summary>
    protected virtual void WriteLink(ExportLink link, StringBuilder html)
    {
        html.Append("<a href=\"").Append(Escaped(Encoded(link.Destination))).Append('"');
        if (link.Title is { } title) html.Append(" title=\"").Append(Escaped(title)).Append('"');
        html.Append('>');
        this.WriteWords(link.Words, html);
        html.Append("</a>");
    }

    /// <summary>Runs of words, as a host writing a link of its own writes the link's words.</summary>
    protected void WriteWords(IReadOnlyList<ExportInline> words, StringBuilder html) => this.Inlines(words, html);

    /// <summary>Where a picture is to be found — where it says, unless the host keeps its pictures somewhere of its own.</summary>
    protected virtual string Source(ExportImage image) => image.Source;

    private static string Tag(ExportStyle style) => style switch
    {
        ExportStyle.Emphasis => "em",
        ExportStyle.Strong => "strong",
        ExportStyle.Strike => "del",
        ExportStyle.Mark => "mark",
        ExportStyle.Insert => "ins",
        ExportStyle.Sub => "sub",
        ExportStyle.Sup => "sup",
        _ => "cite",
    };

    /// <summary>How a run its source set in so many words is set, as a style.</summary>
    private static string Looks(TextLook look)
    {
        var said = new StringBuilder();

        if (look.Font is { Length: > 0 } font) said.Append("font-family: '").Append(font.Replace("'", string.Empty)).Append("'; ");
        if (look.Scale is > 0 and not 1) said.Append("font-size: ").Append((look.Scale * 100).ToString("0.#", CultureInfo.InvariantCulture)).Append("%; ");
        if (look.Bold) said.Append("font-weight: bold; ");
        if (look.Italic) said.Append("font-style: italic; ");
        if (look.Underline || look.Strike)
            said.Append("text-decoration: ").Append(look.Underline && look.Strike ? "underline line-through" : look.Underline ? "underline" : "line-through").Append("; ");
        if (look.Capitals) said.Append("text-transform: uppercase; ");
        if (look.Script != TextScript.None) said.Append("vertical-align: ").Append(look.Script == TextScript.Sub ? "sub" : "super").Append("; ");
        if (look.Ink is { } ink) said.Append("color: ").Append(Colour(ink)).Append("; ");
        if (look.Wash is { } wash) said.Append("background-color: ").Append(Colour(wash)).Append("; ");

        return said.ToString().TrimEnd();
    }

    // ── Spelling ────────────────────────────────────────────────────────────

    /// <summary>A line ending, unless the HTML already ends with one — so blocks stand on lines of their own.</summary>
    private static void Cr(StringBuilder html)
    {
        if (html.Length > 0 && html[^1] != '\n') html.Append('\n');
    }

    /// <summary>The box of an item somebody can tick, where the item has one.</summary>
    private static void Box(bool? done, StringBuilder html)
    {
        if (done is not { } ticked) return;

        html.Append(ticked ? "<input disabled=\"disabled\" type=\"checkbox\" checked=\"checked\" /> " : "<input disabled=\"disabled\" type=\"checkbox\" /> ");
    }

    /// <summary>The words a picture is shown instead of, as the characters they are with every mark taken off.</summary>
    private static string Plain(IReadOnlyList<ExportInline> words)
    {
        var said = new StringBuilder();
        Flat(words, said);

        return said.ToString();

        static void Flat(IReadOnlyList<ExportInline> words, StringBuilder said)
        {
            foreach (var word in words)
            {
                switch (word)
                {
                    case ExportText text: said.Append(text.Text); break;
                    case ExportCodeSpan code: said.Append(code.Text); break;
                    case ExportStyled styled: Flat(styled.Words, said); break;
                    case ExportLooked looked: Flat(looked.Words, said); break;
                    case ExportLink link: Flat(link.Words, said); break;
                    case ExportImage image: Flat(image.Words, said); break;
                    case ExportAbbreviation abbreviation: said.Append(abbreviation.Text); break;
                    case ExportBreak: said.Append('\n'); break;
                    case ExportTab: said.Append('\t'); break;
                    case ExportDrawnSpan drawn: said.Append(drawn.Drawing.Source); break;
                }
            }
        }
    }

    /// <summary>Characters as HTML says them: the four that would be read as markup spelled as entities.</summary>
    private static string Escaped(string text)
    {
        if (text.AsSpan().IndexOfAny("&<>\"") < 0) return text;

        var said = new StringBuilder(text.Length + 8);

        foreach (var character in text)
            said.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => character.ToString(),
            });

        return said.ToString();
    }

    /// <summary>The characters an address may hold as themselves; anything else is written as the bytes of its UTF-8, in hexadecimal.</summary>
    private const string Kept = ";/?:@&=+$,-_.!~*'()#";

    /// <summary>
    /// An address as a link is written: its host in the letters a name server reads (<see cref="IdnMapping"/>), every other
    /// character that may not stand in one written as its bytes, and a percent sign that already starts such a byte left as it is.
    /// </summary>
    private static string Encoded(string address)
    {
        address = Hosted(address);

        var said = new StringBuilder(address.Length);
        Span<byte> bytes = stackalloc byte[4];

        for (var at = 0; at < address.Length; at++)
        {
            var character = address[at];

            if (char.IsAsciiLetterOrDigit(character) || Kept.Contains(character))
            {
                said.Append(character);
                continue;
            }

            if (character == '%' && at + 2 < address.Length && char.IsAsciiHexDigit(address[at + 1]) && char.IsAsciiHexDigit(address[at + 2]))
            {
                said.Append(character);
                continue;
            }

            var length = char.IsSurrogatePair(address, at)
                ? Encoding.UTF8.GetBytes(address.AsSpan(at, 2), bytes)
                : Encoding.UTF8.GetBytes(address.AsSpan(at, 1), bytes);

            for (var index = 0; index < length; index++) said.Append('%').Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));

            if (char.IsSurrogatePair(address, at)) at++;
        }

        return said.ToString();
    }

    /// <summary>
    /// An address with the host it names in ASCII — a name written in other letters as the punycode a name server is asked for —
    /// or as it was, where it names no host or one that is no name.
    /// </summary>
    private static string Hosted(string address)
    {
        var scheme = address.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return address;

        var from = scheme + 3;
        var to = address.AsSpan(from).IndexOfAny("/?#");
        var host = to < 0 ? address[from..] : address.Substring(from, to);
        if (Ascii.IsValid(host)) return address;

        try
        {
            return string.Concat(address.AsSpan(0, from), new IdnMapping().GetAscii(host), to < 0 ? "" : address.AsSpan(from + to));
        }
        catch (ArgumentException)
        {
            return address;
        }
    }

    /// <summary>A colour, <c>0xAARRGGBB</c>, as a style says one.</summary>
    private static string Colour(uint argb) => (argb >> 24) == 0xFF
        ? $"#{argb & 0xFFFFFF:X6}"
        : string.Create(CultureInfo.InvariantCulture, $"rgba({(argb >> 16) & 0xFF}, {(argb >> 8) & 0xFF}, {argb & 0xFF}, {(argb >> 24) / 255.0:0.###})");

    private static string Pixels(double size) => Math.Round(size).ToString(CultureInfo.InvariantCulture);
}
