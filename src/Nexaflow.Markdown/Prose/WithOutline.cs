using System.Globalization;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// Promotes every heading of a markdown document into its outline (<see cref="ContentOutline"/>): ranked by its level, and called by its
/// words GitHub's way — lower-cased, spaces made hyphens and punctuation dropped.
///
/// <para>
/// <strong>What a heading is called, not the name it answers to.</strong> Two headings saying the same are told apart only by the order
/// they stand in, which no block read alone can see; the outline numbers repeats over the whole document, so this stage says only what
/// one heading's own words call it, and a heading worked over again says the same whatever was written round it.
/// </para>
/// <para>
/// A heading written in a quote or a list item is promoted too, and named, though it heads no section: only the document's own blocks
/// do (<see cref="OutlineEntry.Heads"/>).
/// </para>
/// </summary>
public sealed class WithOutline : IAstStage
{
    /// <inheritdoc/>
    public string Name => "markdown:outline";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree) => Promoted(tree);

    /// <summary>
    /// What a heading written as <paramref name="written"/> says, as the outline titles it — so a heading named by its words as written,
    /// markup and all, as a heading path records them, is found by the title its outline gives it.
    /// </summary>
    public static string Titled(string written) =>
        MarkdownParser.Worked($"# {written.Trim()}\n").Children.Select(OutlineMark.Of).OfType<OutlineMark>().FirstOrDefault()?.Title ?? written.Trim();

    /// <summary>A piece with every heading in it marked with its rank and what it is called.</summary>
    private static ContentNode Promoted(ContentNode node)
    {
        if (node.IsLeaf || node.IsDerived || Elsewhere(node)) return node;

        if (node.Kind == MarkdownKinds.Heading) return node.Holding(Kinds.Outline, Roles.Derived, Marked(node));

        List<ContentNode>? parts = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var promoted = Promoted(child);

            if (parts is null && ReferenceEquals(promoted, child)) continue;

            parts ??= [.. node.Children.Take(at)];
            parts.Add(promoted);
        }

        return parts is null ? node : node.With(parts);
    }

    /// <summary>
    /// Whether a piece is written in something whose characters say nothing markdown defines: another language a fence or a formula
    /// holds, code, raw HTML, front matter.
    /// </summary>
    private static bool Elsewhere(ContentNode node) =>
        node is BlockNode { Language: not MarkdownParser.Language }
        || node.Kind is MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.FrontMatter;

    /// <summary>How deep a heading is, one to six.</summary>
    private static int Rank(ContentNode heading) =>
        int.TryParse(heading.Said(MarkdownRoles.Rank), CultureInfo.InvariantCulture, out var said) ? Math.Clamp(said, 1, 6) : 1;

    /// <summary>
    /// What a heading's mark says: its rank, what its words call it — <c>section</c> where they call it nothing — and those words on one
    /// line, as an outline shows it.
    /// </summary>
    private static OutlineMark Marked(ContentNode heading)
    {
        var said = new StringBuilder();
        Plain(heading, said);

        var plain = said.ToString();
        var called = Anchored(plain);

        return new OutlineMark(Rank(heading), called.Length > 0 ? called : "section",
                               string.Join(' ', plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
    }

    /// <summary>
    /// What a heading saying <paramref name="said"/> is called, GitHub's way: its letters and digits lower-cased, its hyphens and
    /// underscores kept, each space a hyphen, and everything else dropped.
    /// </summary>
    private static string Anchored(string said)
    {
        var called = new StringBuilder(said.Length);

        foreach (var character in said)
            if (char.IsLetterOrDigit(character) || character is '-' or '_') called.Append(char.ToLowerInvariant(character));
            else if (character == ' ') called.Append('-');

        return called.ToString();
    }

    /// <summary>
    /// What a heading says, as plain text: its words, what its escapes and entities stand for, the words of its links and the
    /// insides of its code — none of the marks that spell any of them.
    /// </summary>
    private static void Plain(ContentNode node, StringBuilder said)
    {
        if (node.IsDerived) return;

        switch (node.Kind)
        {
            case MarkdownKinds.Word when node.IsLeaf && node.Role is Roles.Element or Roles.Body:
                said.Append(node.Text);
                return;

            case MarkdownKinds.Escape when node.IsLeaf:
                said.Append(node.Text.AsSpan(1));
                return;

            case MarkdownKinds.Entity when node.IsLeaf:
                said.Append(MarkdownEntities.Decoded(node.Text) ?? node.Text);
                return;

            // A heading is named by what was written, as GitHub names it: typography and emoji are how it is drawn, so drawing them
            // moves no anchor anybody links to.
            case MarkdownKinds.Typographic or MarkdownKinds.Shortcode when node.IsLeaf:
                said.Append(node.Text);
                return;

            case MarkdownKinds.Break:
                said.Append('\n');
                return;

            // What a code span holds, without the marks of the containers its lines are written in.
            case MarkdownKinds.Code:
                if (node.Part(Roles.Body) is { } code)
                    said.Append(Spanned(code.IsLeaf ? code.Text : string.Concat(code.Children.Where(part => part.Role != Roles.Trivia).Select(part => part.Text))));
                return;

            case MarkdownKinds.Html:
                return;

            // A bare address says itself.
            case Kinds.Link when node.Part(Roles.Body) is null:
                if (node.Part(Roles.Destination) is { IsLeaf: true } address) said.Append(address.Text);
                return;
        }

        foreach (var child in node.Children)
            if (child.Role is not (Roles.Open or Roles.Close or Roles.Trivia or Roles.Destination or MarkdownRoles.Title))
                Plain(child, said);
    }

    /// <summary>What a code span holds: one space taken off each end where both have one and it is not all space, every line ending a space.</summary>
    private static string Spanned(string code)
    {
        var flat = code.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

        return flat.Length >= 2 && flat[0] == ' ' && flat[^1] == ' ' && !string.IsNullOrWhiteSpace(flat) ? flat[1..^1] : flat;
    }
}
