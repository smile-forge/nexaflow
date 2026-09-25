using System;
using System.Linq;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// The picture a diagram's <c>@{ img: … }</c> names, found and hung on the line that names it — what
/// <see cref="WithImages"/> does for a document's <c>![alt](where)</c>, and resolved the same way.
///
/// <para>
/// A stage rather than something the builder does, because where a name resolves to is the host's to say and a fact about
/// this showing of the block. A name nothing could be found for is left as it was, and the node is drawn as a picture that
/// is missing.
/// </para>
/// </summary>
/// <param name="find">What this showing of the block resolves a name against (<see cref="DiagramRenderOptions.Pictures"/>).</param>
public sealed class WithDiagramPictures(Func<string, ImageSource?>? find) : IAstStage
{
    /// <summary>The role a found picture is hung under.</summary>
    public const string Picture = "picture";

    public string Name => "mermaid:pictures";

    public ContentNode Run(ContentNode tree) => find is null ? tree : AstRewrite.Each(tree, Found);

    /// <summary>The picture hung on a metadata line, or null where nothing was found for it.</summary>
    public static ImageSource? Of(ContentPart? property) => property?.Node.HeldAs(Picture) as ImageSource;

    private ContentNode Found(ContentNode node)
    {
        if (node.Kind != MermaidKinds.Property || node.HeldAs(Picture) is not null) return node;
        if (!string.Equals(node.Children.FirstOrDefault(child => child.Role == Roles.Name)?.Text, "img", StringComparison.OrdinalIgnoreCase)) return node;

        var named = MermaidText.Bare(node.Children.FirstOrDefault(child => child.Role == MermaidRoles.Value)?.Text).Trim();

        return named.Length > 0 && find!(named) is { } picture ? node.Holding(MermaidKinds.Property, Picture, picture) : node;
    }
}
