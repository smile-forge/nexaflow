using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// The block somebody is changing the markup of, made the characters it is written with rather than what they read as
/// (<see cref="MarkdownKinds.Written"/>).
///
/// <para>
/// Asked of the innermost block holding the stretch shown as typed: a block that holds blocks lets the one being written in
/// answer — unless the whole of it is being shown, when the marks it holds its blocks with are what is being written. Not
/// where the stretch lies inside another language's source: that language shows its own (<see cref="ContentNesting.Nests"/>).
/// </para>
/// <para>
/// A stage, so the characters are a piece of the tree the builder is handed — the characters and the line break closing them,
/// each a piece of its own — and nothing drawing the page ever prints a block to find them.
/// </para>
/// </summary>
/// <param name="zone">The stretch shown as typed.</param>
/// <param name="at">Where the content starts in whatever holds it, which is what the stretch is counted from.</param>
public sealed class ShowBlocksAsWritten(RawZone zone, int at) : IAstStage
{
    public string Name => "markdown:shown-as-written";

    public ContentNode Run(ContentNode tree)
    {
        var shown = new HashSet<ContentNode>(ReferenceEqualityComparer.Instance);
        Walk(ContentPart.Of(tree, at), shown);

        return shown.Count == 0 ? tree : AstRewrite.Each(tree, node => shown.Contains(node) ? Written(node) : node);
    }

    /// <summary>The blocks a holder holds, each asked in turn — what a list item's marker and its box are not.</summary>
    private void Walk(ContentPart holder, HashSet<ContentNode> shown)
    {
        foreach (var block in holder.Children)
            if (!block.Derived && block.Role != Roles.Trivia && block.Kind is not (MarkdownKinds.Task or MarkdownKinds.Marker))
                Asked(block, shown);
    }

    private void Asked(ContentPart block, HashSet<ContentNode> shown)
    {
        if (!(zone.Start < block.End && block.Start < zone.End) || ContentNesting.Nests(block, zone)) return;

        if (!Holds(block) || Opened(block)) shown.Add(block.Node);
        else Walk(block.Kind == MarkdownKinds.List ? block : block.Part(Roles.Body) ?? block, shown);
    }

    /// <summary>Whether a block holds blocks, so there is something further in to ask.</summary>
    private static bool Holds(ContentPart block) =>
        block.Kind is MarkdownKinds.Quote or MarkdownKinds.Alert or MarkdownKinds.List or MarkdownKinds.Item
            or MarkdownKinds.Definition or MarkdownKinds.Described or MarkdownKinds.Figure or MarkdownKinds.Footer;

    /// <summary>Whether the whole of a block is being shown, as far as its characters reach before the line break closing them.</summary>
    private bool Opened(ContentPart block) =>
        zone.Start <= block.Start && zone.End >= block.Start + block.Print().TrimEnd('\n', '\r').Length;

    /// <summary>A block as the characters it is written with, and the line breaks closing them as a piece of their own.</summary>
    private static ContentNode Written(ContentNode block)
    {
        var text = block.Print();
        var says = text.TrimEnd('\n', '\r');

        return ContentNode.Branch(MarkdownKinds.Written,
                                  says.Length < text.Length
                                      ? [ContentNode.Shown(says, role: Roles.Body), ContentNode.Leaf(Kinds.Space, text[says.Length..], Roles.Trivia)]
                                      : [ContentNode.Shown(says, role: Roles.Body)],
                                  block.Role);
    }
}
