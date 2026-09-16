using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Reading a diagram's tree back: what a line states, the facts a stage hung under a part, the hole standing in one, and
/// what the shared shapes <see cref="MermaidLine"/> reads say — the questions every diagram's stages and model ask of it.
/// </summary>
public static class MermaidParts
{
    /// <summary>What a line states — the first thing on it that is not the space before it — or null for a part that is no line, or says nothing.</summary>
    public static ContentNode? Stated(this ContentNode line) =>
        line.Kind == MermaidKinds.Line ? line.Children.FirstOrDefault(child => child.Kind != Kinds.Space) : null;

    /// <inheritdoc cref="Stated(ContentNode)"/>
    public static ContentPart? Stated(this ContentPart line) =>
        line.Kind == MermaidKinds.Line ? line.Children.FirstOrDefault(child => child.Kind != Kinds.Space) : null;

    /// <summary>Whether a line starts with space rather than at the start of its row.</summary>
    public static bool Indented(this ContentNode line) => line.Children is [{ Kind: Kinds.Space }, _, ..];

    /// <summary>What a stage worked out about a part and hung underneath it (<see cref="AstRewrite.Saying(ContentNode, string, string, string)"/>), or null.</summary>
    public static string? Fact(this ContentPart part, string role) => part.Node.Said(role);

    /// <summary>The first part of <paramref name="kind"/> in or under a part — itself included — or null.</summary>
    public static ContentPart? Inner(this ContentPart? part, string kind) =>
        part?.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == kind);

    /// <inheritdoc cref="Inner(ContentPart?, string)"/>
    public static ContentNode? Inner(this ContentNode? node, string kind) =>
        node?.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == kind);

    /// <summary>The hole a stage put in a part with nothing written in it — in its quotes or brackets too — or null where there is none.</summary>
    public static ContentPart? Hole(this ContentPart? part) => part.Inner(Kinds.Hole);

    /// <summary>What a name, a label or a title says — its <see cref="MermaidKinds.Words"/>, without quotes or brackets — or null.</summary>
    public static ContentPart? Words(this ContentPart? part) => part.Inner(MermaidKinds.Words);

    /// <summary>What a name or a list's names say, as text: empty for a name still to be written.</summary>
    public static IReadOnlyList<string> SaidNames(this ContentNode? names) =>
        names is null ? []
        : names.Kind == MermaidKinds.Name ? [names.Inner(MermaidKinds.Words)?.Text ?? string.Empty]
        : [.. names.Children.Where(child => child.Kind == MermaidKinds.Name).Select(name => name.Inner(MermaidKinds.Words)?.Text ?? string.Empty)];

    /// <summary>The names a <see cref="MermaidKinds.Names"/> lists — or the one name, where it is one — in the order written.</summary>
    public static IReadOnlyList<ContentPart> Named(this ContentPart? names) =>
        names is null ? []
        : names.Kind == MermaidKinds.Name ? [names]
        : [.. names.Children.Where(child => child.Kind == MermaidKinds.Name)];

    /// <summary>The number written in a part — its <see cref="MermaidKinds.Number"/> — or null where none is written or it is wrong.</summary>
    public static double? Number(this ContentPart? part) =>
        part.Inner(MermaidKinds.Number) is { Trouble: null, Length: > 0 } number ? MermaidNumber.Read(number.Text) : null;
}
