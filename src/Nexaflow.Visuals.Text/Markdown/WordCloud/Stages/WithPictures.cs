using System;
using System.Linq;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.WordCloud;

namespace Nexaflow.Visuals.Text.Markdown.WordCloud.Stages;

/// <summary>
/// The picture a <c>mask:</c> names, found and hung under the setting that named it.
///
/// <para>
/// A stage rather than something the builder does, because finding a picture is nobody's business but the host's:
/// where a name resolves to depends on the document — one read from a zip brings its pictures along, one on disk
/// has a folder — and that is a fact about this showing of the block, settled between reading the source and
/// drawing it. A builder is handed a tree that already has the picture in it.
/// </para>
/// <para>
/// What it hangs is the image itself (<see cref="ContentNode.Held"/>) rather than somewhere to find one, because
/// there may be nowhere: a host can hand back an image it holds in memory and never had a path for.
/// </para>
/// </summary>
public sealed class WithPictures(Func<string, ImageSource?>? find) : IAstStage
{
    /// <summary>The role a resolved picture is hung under.</summary>
    public const string Picture = "picture";

    /// <summary>The setting whose value names one.</summary>
    private const string Mask = "mask";

    /// <inheritdoc/>
    public string Name => "pictures";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree) => find is null ? tree : AstRewrite.Each(tree, Found);

    private ContentNode Found(ContentNode node)
    {
        if (node.Kind != WordCloudKinds.Setting) return node;
        if (Said(node, WordCloudKinds.Key) != Mask) return node;
        if (Said(node, WordCloudKinds.Value) is not { Length: > 0 } named) return node;

        // A host that throws looking for a picture has said there is none, which is what a reader is told.
        try
        {
            return find!(named) is { } picture ? node.Holding(WordCloudKinds.Setting, Picture, picture) : node;
        }
        catch
        {
            return node;
        }
    }

    private static string? Said(ContentNode node, string kind) =>
        node.Children.FirstOrDefault(child => child.Kind == kind)?.Text;
}
