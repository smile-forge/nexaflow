using System;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// How the host wants each link to look, asked once and hung on the link.
///
/// <para>
/// A host knows things about a link that the document does not: the help pane can see that
/// <c>locate:Chrome_HelpButton</c> points at a button in the app rather than at a page, and wants to say so
/// without disturbing the words the writer wrote. That is a fact about this showing of the document,
/// settled between reading the source and drawing it — the same place the picture behind an
/// <c>![](…)</c> and the language inside a fence are settled.
/// </para>
/// <para>
/// A stage rather than a call from the builder, because a builder turns a tree into a layout and asking
/// the host a question halfway through is not that. By the time it sees the link the answer is on it.
/// </para>
/// </summary>
/// <param name="asked">What the host makes of a link, given where it points and the words it was written as.</param>
public sealed class WithLinks(Func<string, string, LinkLook?>? asked) : IAstStage
{
    /// <summary>The role a host's answer is hung under.</summary>
    public const string Look = "look";

    /// <inheritdoc/>
    public string Name => "markdown:links";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree) => asked is null ? tree : AstRewrite.Each(tree, Asked);

    /// <summary>What the host said about a link, or null where it said nothing.</summary>
    public static LinkLook? Of(ContentPart? part)
    {
        if (part is null) return null;

        foreach (var child in part.Children)
            foreach (var held in child.Children)
                if (held.Node.Role == Look && held.Node.Held is LinkLook look)
                    return look;

        return null;
    }

    private ContentNode Asked(ContentNode node)
    {
        if (node.Kind != MarkdownKinds.Link) return node;

        foreach (var child in node.Children)
            foreach (var held in child.Children)
                if (held.Role == Look) return node;

        var where = MarkdownLinks.Goes(node) ?? node.Print();
        if (where.Length == 0) return node;

        LinkLook? look;

        try
        {
            look = asked!(where, Words(node));
        }
        catch
        {
            // A decoration is a flourish: a host that threw over one must not cost the document its link.
            return node;
        }

        return look is null ? node : AstRewrite.Holding(node, MarkdownKinds.Link, Look, look);
    }

    /// <summary>
    /// The words a link was written as — which for a bare or bracketed URL is the URL itself, because that is
    /// what a reader sees and what the builder draws. The angle brackets round an autolink are machinery and
    /// are no more its words than a paragraph's line ending is.
    /// </summary>
    private static string Words(ContentNode node)
    {
        foreach (var child in node.Children)
            if (child.Role == Roles.Body) return child.Print();

        return MarkdownLinks.Goes(node) ?? node.Print();
    }
}

/// <summary>
/// What a host wants a link to look like: asked for every link, and free to say nothing about any of it.
/// </summary>
/// <param name="Ink">What the words are drawn in, or null for the accent every other link uses.</param>
/// <param name="Underline">Whether it is underlined. False for a link that says what it is some other way.</param>
/// <param name="Before">A mark set in front of the words — a pin, for a link that points at the screen.</param>
/// <param name="After">A mark set behind them — an arrow, for one that leaves the document.</param>
/// <param name="Says">What resting on it says, where the host has something to say.</param>
public sealed record LinkLook(
    Brush? Ink = null,
    bool Underline = true,
    string? Before = null,
    string? After = null,
    string? Says = null)
{
    /// <summary>
    /// What the marks are drawn in, where they are glyphs the reading face does not have — an icon font.
    /// Null draws them in the same face as the words, which is right for an arrow and wrong for a pin.
    /// </summary>
    public FontFamily? MarkFont { get; init; }
}
