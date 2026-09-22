using System;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// The picture an <c>![alt](where)</c> names, found and hung on the image that named it.
///
/// <para>
/// A stage rather than something the builder does, because finding a picture is nobody's business but the
/// host's: where a name resolves to depends on the document — one read from a language pack brings its
/// pictures along, one on disk has a folder — and that is a fact about this showing of the block, settled
/// between reading the source and drawing it. A builder is handed a tree that already has the picture in it.
/// </para>
/// <para>
/// What it hangs is the image itself (<see cref="ContentNode.Held"/>) rather than somewhere to find one,
/// because there may be nowhere: a host can hand back an image it holds in memory and never had a path for.
/// </para>
/// <para>
/// An image nothing could be found for is left exactly as it was, and the builder draws the words the writer
/// wrote instead of it — which is what alt text is for, and the only thing to show when the picture is gone.
/// </para>
/// </summary>
/// <param name="find">What this showing of the document resolves a name against (<see cref="MarkdownPictures.Found"/>).</param>
public sealed class WithImages(Func<string, ImageSource?>? find) : IAstStage
{
    /// <summary>The role a found picture is hung under.</summary>
    public const string Picture = "picture";

    /// <inheritdoc/>
    public string Name => "markdown:images";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree) => find is null ? tree : AstRewrite.Each(tree, Found);

    /// <summary>The picture hung on a part, or null where nothing was found for it.</summary>
    public static ImageSource? Of(ContentPart? part)
    {
        if (part is null) return null;

        foreach (var child in part.Children)
            foreach (var held in child.Children)
                if (held.Node.Role == Picture && held.Node.Held is ImageSource picture)
                    return picture;

        return null;
    }

    private ContentNode Found(ContentNode node)
    {
        if (node.Kind != MarkdownKinds.Image) return node;

        // Already answered, which is what makes running this twice the same as running it once.
        foreach (var child in node.Children)
            foreach (var held in child.Children)
                if (held.Role == Picture) return node;

        if (Said(node) is not { Length: > 0 } named) return node;

        var picture = find!(named);

        return picture is null ? node : node.Holding(MarkdownKinds.Image, Picture, picture);
    }

    /// <summary>Where an image says it points, which is the only part of it that names a picture.</summary>
    private static string? Said(ContentNode node)
    {
        foreach (var child in node.Children)
            if (child.Role == MarkdownRoles.Destination) return child.Text;

        return null;
    }
}
