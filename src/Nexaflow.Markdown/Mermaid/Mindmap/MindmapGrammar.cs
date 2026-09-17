using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Mindmap.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Mindmap;

/// <summary>
/// What a <c>mindmap</c> block says beyond the lines every diagram shares: its nodes, and the <c>::icon(…)</c> and
/// <c>:::class</c> lines decorating the node above them.
///
/// <para>
/// The rules are Mermaid's. A node is read as every outline diagram's is (<see cref="MermaidOutline"/>): its id, its title in
/// brackets, or both. Which brackets say which shape is the mindmap's own — <c>[…]</c> a square, <c>(…)</c> a rounded square,
/// <c>((…))</c> a circle, <c>)…(</c> a cloud, <c>))…((</c> a bang, <c>{{…}}</c> a hexagon, and a bare id no border at all
/// (<see cref="MindmapTree"/>). The first node is the root and every later one hangs off the nearest node before it indented
/// less, which is the whole block's business rather than a line's (<see cref="ResolveRoot"/>).
/// </para>
/// </summary>
public sealed class MindmapGrammar : IMermaidGrammar
{
    private const string Shape = "A node is its id, its title in brackets, or both: root((mindmap)), id[I am a square], Origins.";

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        if (line.Sees(MermaidOutline.ClassMark)) return MermaidOutline.Decoration(line, MermaidOutline.ClassMark, null, MindmapRoles.Class, MindmapKinds.Class);
        if (line.Sees(MermaidOutline.IconMark)) return MermaidOutline.Decoration(line, MermaidOutline.IconMark, ")", MindmapRoles.Icon, MindmapKinds.Icon);

        if (!MermaidOutline.Node(line, MindmapRoles.Id, MindmapRoles.Title, "([){}", out var titled)) return line.Shown(Shape);

        line.Space();
        return line.Done && (titled || line.Rest.Length == 0) ? line.Read(MindmapKinds.Node) : line.Shown(Shape);
    }

    /// <inheritdoc/>
    /// <remarks>Under a node, another as far in as it — a child of the same parent, with its title still to write.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => above?.Kind == MindmapKinds.Node ? ("[\"\"]", 2) : null;

    /// <inheritdoc/>
    /// <remarks>
    /// A title in brackets is put in quotes to hold a quote, a bracket closing it or a comment; a bare id that is its own title is
    /// written as a title in quotes to hold anything an id cannot (<see cref="MermaidOutline.Escaping"/>). An icon's name runs to
    /// its bracket, so it cannot hold one.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role == MindmapRoles.Icon && text.Contains(')'))
        {
            var named = text.Replace(")", string.Empty, StringComparison.Ordinal);
            return new MermaidWriting(caret, caret, named, caret + named.Length);
        }

        return MermaidOutline.Escaping(part, caret, text, MindmapRoles.Id, Stops);
    }

    /// <inheritdoc/>
    /// <remarks>Whether every node hangs off the root, by its indentation over the whole block (<see cref="ResolveRoot"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [new ResolveRoot()];

    /// <inheritdoc/>
    /// <remarks>Where a title is still to write, between its quotes.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind == MermaidKinds.Quoted;

    /// <summary>Whether a character ends a bare id.</summary>
    private static bool Stops(char character) => character is '(' or '[' or ')' or '{' or '}' or '"' or '%';
}
