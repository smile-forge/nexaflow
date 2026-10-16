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
/// <item>headings take their level from their numbering (3 → ##, 3.2 → ###); the document's title, before any of them,
/// is #; others are ##;</item>
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
                        int level = Level(heading) ?? (titled ? 2 : 1);
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

    private static string Table(string html) => HtmlToMarkdown.Convert(html).Trim();

    /// <summary>A heading's level from its numbering — 1, A, 3.2, A.1 — or null when it has none.</summary>
    internal static int? Level(string heading)
        => Numbering().Match(heading) is { Success: true } m ? Math.Min(6, m.Groups["part"].Captures.Count + 2) : null;

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

    [GeneratedRegex(@"[.!?:;][""”’)\]*_]*$")]
    private static partial Regex SentenceEnd();
}
