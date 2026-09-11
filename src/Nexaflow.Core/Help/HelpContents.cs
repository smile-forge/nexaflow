using System.Text.RegularExpressions;
using Markdig.Syntax;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Help;

/// <summary>
/// Gives a help page its menu: a <b>Topics</b> list of its <c>##</c> sections, placed where the introduction ends, and a
/// link back to that list at the end of every section. Generated as the page loads rather than written into it, so every
/// page — and every translation — has one that cannot drift from its headings. The links are ordinary in-page links,
/// which the renderer resolves itself (<see cref="MarkdownAnchors"/>). A page with fewer than two sections is left as it
/// is: a list of one is not a menu.
/// </summary>
internal static partial class HelpContents
{
    // Stand-ins for the generated lines until the finished page's ids are known — invisible separators around a name,
    // which no page will ever contain.
    private const string ListSlot = "⁣help-topics⁣";
    private const string BackSlot = "⁣help-back⁣";

    public static string AddTopics(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n').ToList();
        var sections = Sections(string.Join('\n', lines));
        if (sections.Count < 2) return markdown;

        // Bottom-up, so each insertion leaves the line numbers above it true. A back link where each section ends —
        // before the rule and blank lines leading into the next — and one at the very end of the page.
        Insert(lines, EndBefore(lines, lines.Count), BackSlot);
        for (var i = sections.Count - 1; i >= 1; i--)
            Insert(lines, EndBefore(lines, sections[i].Line), BackSlot);

        // Then the list itself, under a heading of its own for the back links to land on, where the introduction ends.
        Insert(lines, EndBefore(lines, sections[0].Line), $"## {Str.Get("Help.Topics")}", "", ListSlot);

        // Ids come from the finished page, so a repeated title gets the "-1" the renderer will give it.
        var text     = string.Join('\n', lines);
        var headings = Sections(text);   // the list's heading first, then the page's own sections
        var list     = headings.Skip(1).Select(h => $"- [{Escape(Title(lines[h.Line]))}](#{h.Id})");
        var back     = $"[{Str.Get("Help.Topics.Back")}](#{headings[0].Id})";
        return text.Replace(ListSlot, string.Join('\n', list)).Replace(BackSlot, back);
    }

    // The page's top-level "##" headings — not one quoted, listed or fenced — with the ids the pipeline gives them.
    private static List<(int Line, string Id)> Sections(string markdown)
        => Markdig.Markdown.Parse(markdown, MarkdownPipelineFactory.Default)
            .OfType<HeadingBlock>()
            .Where(h => h.Level == 2)
            .Select(h => (h.Line, MarkdownAnchors.IdOf(h) ?? ""))
            .ToList();

    // Where the section that ends before line `next` really ends: back past the blank lines and any rule leading in.
    private static int EndBefore(List<string> lines, int next)
    {
        var at = next;
        while (at > 0 && (lines[at - 1].Trim().Length == 0 || ThematicBreak().IsMatch(lines[at - 1]))) at--;
        return at;
    }

    private static void Insert(List<string> lines, int at, params string[] block)
        => lines.InsertRange(at, ["", .. block, ""]);

    // A heading's text as written — inline markup and all — whether ATX ("## Title ##") or setext ("Title" over "---").
    private static string Title(string headingLine)
        => headingLine.Trim().TrimStart('#').Trim().TrimEnd('#').Trim();

    private static string Escape(string text) => text.Replace("[", "\\[").Replace("]", "\\]");

    [GeneratedRegex(@"^ {0,3}([-*_])( *\1){2,} *$")]
    private static partial Regex ThematicBreak();
}
