using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;

namespace Nexaflow.Markdown.Pipeline.Stages;

/// <summary>
/// What every <c>{{…}}</c> in the content stands for, worked out against the data it is being shown alongside and
/// hung underneath the binding that names it.
///
/// <para>
/// A stage rather than something a builder does, because binding is nobody's business but the host's: what a
/// document is shown against is a fact about this showing of it, settled between reading the source and drawing
/// it. A builder is handed a tree that already knows what it says.
/// </para>
/// <para>
/// Hung underneath, so the characters somebody typed are all still there: the block prints as it was written, and
/// a press can reveal the binding and put the caret in it.
/// </para>
/// </summary>
public sealed class WithBindings(IDataContext data) : IAstStage
{
    /// <inheritdoc/>
    public string Name => "bindings";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, Bound);

    private ContentNode Bound(ContentNode node) =>
        node.Kind == Kinds.Bound && node.Part(Roles.Name) is { Text.Length: > 0 } path
            ? node.Saying(Kinds.Bound, ContentWords.Value, BoundText.Says(path.Text, data))
            : node;
}
