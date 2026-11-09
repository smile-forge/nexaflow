using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Pages;

/// <summary>
/// Pages read into blocks (<see cref="IPageReading"/>) written as one Markdown document — the text as one flow, as a
/// reader of the pages would take it:
/// <list type="bullet">
/// <item>a paragraph broken by a column, a page or a figure set into it is one paragraph again, the figures after it —
/// its rest the text that opens the next column or page, or that goes on in mid-sentence further down;</item>
/// <item>headings take their level from their numbering (3 → ##, 3.2 → ###); one naming a numbered item of its section
/// (Beispiel 1.4) goes under the section it stands in; the document's title, before any of them, is #; a box's title, said
/// again and again in a chapter (<see cref="BoxTitles"/>), leads its box in bold, opening no section; others are ##;</item>
/// <item>maths is <c>$…$</c> in a sentence and <c>$$…$$</c> set apart; tables are Markdown tables;</item>
/// <item>a figure is what <c>figure</c> makes of it — its picture, or a diagram drawn again — its caption under it;</item>
/// <item>footnotes are gathered at the end; what runs along the pages' edges — running heads, page numbers — is left out.</item>
/// </list>
/// </summary>
public static partial class PageMarkdown
{
    /// <param name="figure">The Markdown for the <c>block</c>-th block of the <c>page</c>-th page, a figure; null to leave
    /// it out — one not yet cut from its page.</param>
    public static string Write(IReadOnlyList<IReadOnlyList<PageBlock>> pages, Func<int, int, string?> figure)
    {
        var parts = new List<string>();
        var floats = new List<string>();     // figures, captions and tables met while a paragraph is still open
        var footnotes = new List<string>();
        int open = -1;                       // the paragraph a column, a page or a figure broke off, waiting for its rest
        (int Page, double Top) openAt = default;   // where its last block stands
        bool titled = false;
        int numbered = 2;                    // the level of the last heading its numbering placed
        var boxes = BoxTitles(pages);
        var steps = Steps(pages);

        void Close()
        {
            open = -1;
            parts.AddRange(floats);
            floats.Clear();
        }

        void Float(string part)
        {
            if (open >= 0) floats.Add(part);
            else parts.Add(part);
        }

        for (int p = 0; p < pages.Count; p++)
        {
            for (int b = 0; b < pages[p].Count; b++)
            {
                var block = pages[p][b];
                switch (block.Kind)
                {
                    case PageBlockKind.Margin:
                        break;

                    case PageBlockKind.Footnote:
                        if (Prose(block.Content) is { Length: > 0 } note) footnotes.Add(note);
                        break;

                    case PageBlockKind.Text:
                        string text = Prose(block.Content);
                        if (text.Length == 0) break;
                        // Its rest opens the next column or page, whatever it begins with; further down the same
                        // column, only what begins in mid-sentence is its rest.
                        if (open >= 0 && (p > openAt.Page || block.Top <= openAt.Top || Continues(text)))
                        {
                            parts[open] = Join(parts[open], text);
                            openAt = (p, block.Top);
                            if (Finished(parts[open])) Close();
                            break;
                        }
                        Close();
                        parts.Add(text);
                        if (!Finished(text))
                        {
                            open = parts.Count - 1;
                            openAt = (p, block.Top);
                        }
                        titled = true;
                        break;

                    case PageBlockKind.Title:
                        Close();
                        string heading = Prose(block.Content);
                        if (heading.Length == 0) break;
                        // A box's title leads what the box says: it opens no section.
                        if (boxes.Contains(heading))
                        {
                            titled = true;
                            parts.Add("**" + heading + "**");
                            break;
                        }

                        // A numbered item of the section it stands in — an example, an exercise, a step — goes under that section.
                        int level = steps.Contains((p, b)) ? Math.Min(6, numbered + 1)
                                  : Level(heading) is { } placed ? numbered = placed
                                  : Labelled().IsMatch(heading) ? Math.Min(6, numbered + 1)
                                  : titled ? 2 : 1;
                        titled = true;
                        parts.Add(new string('#', level) + " " + heading);
                        break;

                    case PageBlockKind.Equation:
                        Close();
                        if (Equation(block.Content) is { Length: > 0 } maths) parts.Add("$$\n" + maths + "\n$$");
                        break;

                    case PageBlockKind.Table:
                        if (Table(block.Content) is { Length: > 0 } table) Float(table);
                        break;

                    case PageBlockKind.Figure:
                        if (figure(p, b) is { Length: > 0 } drawn) Float(drawn);
                        break;

                    case PageBlockKind.Caption:
                        if (Prose(block.Content) is { Length: > 0 } caption) Float("*" + caption + "*");
                        break;
                }
            }
        }
        Close();
        if (footnotes.Count > 0) parts.Add("---\n\n" + string.Join("\n\n", footnotes));
        return string.Join("\n\n", parts) + "\n";
    }

    /// <summary>A block's text as Markdown prose: its lines one paragraph, the characters Markdown would read as marks
    /// written as themselves, its maths between dollars.</summary>
    internal static string Prose(string content)
    {
        var text = new StringBuilder();
        int at = 0;
        foreach (Match maths in InlineMaths().Matches(content))
        {
            text.Append(Escaped(content[at..maths.Index]));
            string tex = maths.Groups["tex"].Value.Trim();
            if (tex.Length > 0) text.Append('$').Append(tex).Append('$');
            at = maths.Index + maths.Length;
        }
        text.Append(Escaped(content[at..]));
        return Spaces().Replace(text.ToString(), " ").Trim();
    }

    private static string Escaped(string text) => MarkdownMarks().Replace(text, @"\$0");

    /// <summary>An equation's LaTeX without the <c>\[ \]</c> it is set apart by.</summary>
    internal static string Equation(string content)
    {
        string tex = content.Trim();
        if (tex.StartsWith(@"\[", StringComparison.Ordinal) && tex.EndsWith(@"\]", StringComparison.Ordinal)) tex = tex[2..^2];
        return tex.Trim();
    }

    /// <summary>A table as a Markdown table, the maths in its cells between dollars as in a sentence.</summary>
    private static string Table(string html)
        => HtmlToMarkdown.Convert(InlineMaths().Replace(html, maths => "$" + maths.Groups["tex"].Value.Trim() + "$")).Trim();

    /// <summary>A heading's level from its numbering — 1, A, 3.2, A.1 — or null when it has none.</summary>
    internal static int? Level(string heading)
        => Numbering().Match(heading) is { Success: true } m ? Math.Min(6, m.Groups["part"].Captures.Count + 2) : null;

    /// <summary>
    /// The headings that are the titles of boxes set into the text — a note, a rule to remember, a worked solution — rather than of
    /// sections: those with no numbering, nor a word and a number naming an item, read more than once in the same chapter. A section's
    /// name is said once in its chapter; a box's title, as often as the chapter has such boxes. A chapter is what the numbering of
    /// its sections says — the 1 of 1.13 — and what comes before any such section is one more.
    /// </summary>
    internal static HashSet<string> BoxTitles(IReadOnlyList<IReadOnlyList<PageBlock>> pages)
    {
        var read = new Dictionary<(string Heading, string Chapter), int>();
        string chapter = "";
        foreach (var block in pages.SelectMany(page => page).Where(block => block.Kind == PageBlockKind.Title))
        {
            string heading = Prose(block.Content);
            if (heading.Length == 0 || Labelled().IsMatch(heading)) continue;
            if (Numbering().Match(heading) is { Success: true } numbering)
            {
                if (numbering.Groups["part"].Captures.Count > 0) chapter = heading[..heading.IndexOf('.')];
                continue;
            }
            read[(heading, chapter)] = read.GetValueOrDefault((heading, chapter)) + 1;
        }
        return [.. read.Where(seen => seen.Value > 1).Select(seen => seen.Key.Heading)];
    }

    /// <summary>
    /// Where the headings numbered by one number that are steps of a section, not chapters — 1. Schritt, 4. Schritt within 3.10 —
    /// stand, by page and block. A chapter's number never goes back: one lower than the chapter its section numbering is in, or
    /// higher than the chapter of the next section numbered after it, cannot be a chapter's. One numbered in letters is not weighed.
    /// </summary>
    internal static HashSet<(int Page, int Block)> Steps(IReadOnlyList<IReadOnlyList<PageBlock>> pages)
    {
        var titles = new List<(int Page, int Block, int? Number, int? Chapter)>();
        for (int p = 0; p < pages.Count; p++)
            for (int b = 0; b < pages[p].Count; b++)
            {
                if (pages[p][b].Kind != PageBlockKind.Title || Numbering().Match(Prose(pages[p][b].Content)) is not { Success: true } numbering) continue;
                int? number = int.TryParse(numbering.Value.TrimEnd().Split('.', ' ')[0], out var n) ? n : null;
                titles.Add(numbering.Groups["part"].Captures.Count > 0 ? (p, b, null, number) : (p, b, number, null));
            }

        var steps = new HashSet<(int Page, int Block)>();
        int? chapter = null;
        for (int at = 0; at < titles.Count; at++)
        {
            if (titles[at].Chapter is { } within) chapter = within;
            if (titles[at].Number is not { } number) continue;

            int? next = titles.Skip(at + 1).Select(title => title.Chapter).FirstOrDefault(later => later is not null);
            if (number < chapter || next < number) steps.Add((titles[at].Page, titles[at].Block));
        }
        return steps;
    }

    /// <summary>A caption as the description of the picture it captions: its words as prose, the brackets of a picture's link spelled as themselves.</summary>
    internal static string Described(string caption) => Prose(caption).Replace("[", @"\[", StringComparison.Ordinal).Replace("]", @"\]", StringComparison.Ordinal);

    /// <summary>Whether a paragraph ends a sentence, so what comes after it is not its rest.</summary>
    private static bool Finished(string paragraph) => SentenceEnd().IsMatch(paragraph);

    /// <summary>Whether a paragraph goes on from the one broken off before it: it begins in mid-sentence.</summary>
    private static bool Continues(string paragraph) => paragraph.Length > 0 && (char.IsLower(paragraph[0]) || paragraph[0] is ',' or ';' or ')' or '$');

    /// <summary>A paragraph's two halves again — a word broken at the line with a hyphen made whole.</summary>
    private static string Join(string first, string rest)
        => first.Length > 1 && first[^1] == '-' && char.IsLetter(first[^2]) && char.IsLower(rest[0]) ? first[..^1] + rest : first + " " + rest;

    [GeneratedRegex(@"\\\((?<tex>.*?)\\\)", RegexOptions.Singleline)]
    private static partial Regex InlineMaths();

    [GeneratedRegex(@"[\\`*_$]")]
    private static partial Regex MarkdownMarks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(?:\d+|[A-Z])(?<part>\.\d+)*\.?\s+\S")]
    private static partial Regex Numbering();

    /// <summary>A heading naming a numbered item — a word and a number with its section's in it: Beispiel 1.4, Exercise 2.3.1.</summary>
    [GeneratedRegex(@"^\p{L}[\p{L}\p{M}'’-]*\s+(?:\d+|[A-Z])(?:\.\d+)+(?!\w)")]
    private static partial Regex Labelled();

    [GeneratedRegex(@"[.!?:;][""”’)\]*_]*$")]
    private static partial Regex SentenceEnd();
}
