using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// Hangs on each picture whose bytes are written into the source (<see cref="EmbeddedPicture"/>) what they decode to, under
/// the role <see cref="WithImages"/> hangs a found picture under — so the builder draws a picture written into the document
/// exactly as it draws one a document names, and needs to know nothing of where it came from.
/// </summary>
/// <param name="textSize">The size the source's ordinary text is drawn at, which a picture's asked-for size is measured in.</param>
public sealed class WithEmbeddedPictures(double textSize) : IAstStage
{
    /// <inheritdoc/>
    public string Name => "markdown:embedded-pictures";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, Decoded);

    private ContentNode Decoded(ContentNode node)
    {
        if (node.Kind != MarkdownKinds.Image || node.IsDerived) return node;

        // Already answered, which is what makes running this twice the same as running it once.
        foreach (var child in node.Children)
            foreach (var held in child.Children)
                if (held.Role == WithImages.Picture) return node;

        if (node.Children.Select(child => child.Held).OfType<EmbeddedPicture>().FirstOrDefault() is not { } embedded) return node;

        return EmbeddedPictures.Drawn(embedded, textSize) is { } picture
            ? node.Holding(MarkdownKinds.Image, WithImages.Picture, picture)
            : node;
    }
}
