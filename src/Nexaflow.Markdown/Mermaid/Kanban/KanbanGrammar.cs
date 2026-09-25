using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Kanban.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Kanban;

/// <summary>
/// What a <c>kanban</c> block says beyond the lines every diagram shares: its columns and cards, each a node, and the
/// <c>::icon(…)</c> and <c>:::class</c> lines decorating the node above them.
///
/// <para>
/// The rules are Mermaid's. A node is read as every outline diagram's is (<see cref="MermaidOutline"/>), and then its metadata,
/// <c>@{ assigned: knsv, ticket: MC-2037, priority: 'High' }</c>. The first node's indentation is a column's; a node indented further is a card in the column above
/// it. Which a node is, is a fact about the block rather than its line, so it is the stage's (<see cref="ResolveColumns"/>).
/// </para>
/// </summary>
public sealed class KanbanGrammar : IMermaidGrammar
{
    public const string DataMark = "@{";

    /// <summary>What a card's metadata sets.</summary>
    public static readonly IReadOnlyList<string> Keys = ["assigned", "ticket", "priority", "label", "icon", "shape"];

    /// <summary>A card's priorities, the most urgent first.</summary>
    public static readonly IReadOnlyList<string> Priorities = ["Very High", "High", "Medium", "Low", "Very Low"];

    private const string Shape = "A column or a card is its id, its title in brackets, or both, and any metadata after: id3[Update the database]@{ assigned: knsv }.";

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        if (line.Sees(MermaidOutline.ClassMark)) return MermaidOutline.Decoration(line, MermaidOutline.ClassMark, null, KanbanRoles.Class, KanbanKinds.Class);
        if (line.Sees(MermaidOutline.IconMark)) return MermaidOutline.Decoration(line, MermaidOutline.IconMark, ")", KanbanRoles.Icon, KanbanKinds.Icon);
        return Node(line);
    }

    /// <inheritdoc/>
    /// <remarks>Under a column, a card with its title still to write, indented under it; anywhere else, one as far in as the line above.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) =>
        above is KanbanColumnNode ? ("  [\"\"]", 4) : ("[\"\"]", 2);

    /// <inheritdoc/>
    /// <remarks>
    /// A title in brackets is put in quotes to hold a quote, a bracket closing it or a comment; a bare id that is its own title is
    /// written as a title in quotes to hold anything an id cannot (<see cref="MermaidOutline.Escaping"/>). An icon's name runs to its
    /// bracket and metadata to its brace, so neither can hold the one closing it.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        var closing = part.Role switch { KanbanRoles.Icon => ")", KanbanRoles.Data => "}", _ => null };
        if (closing is not null && text.Contains(closing, StringComparison.Ordinal))
        {
            var kept = text.Replace(closing, string.Empty, StringComparison.Ordinal);
            return new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        return MermaidOutline.Escaping(part, caret, text, KanbanRoles.Id, Stops);
    }

    /// <inheritdoc/>
    /// <remarks>Whether each node is a column or a card, by its indentation over the whole block (<see cref="ResolveColumns"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing)
    {
        var config = KanbanConfig.Read(block.Config);
        return [new ResolveColumns(config), new WithConfig<KanbanConfig>(config)];
    }

    /// <inheritdoc/>
    /// <remarks>Where a title is still to write, between its quotes.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind == MermaidKinds.Quoted;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>A column or a card: <c>id3[Update the database]@{ assigned: knsv }</c>, <c>Todo</c>, <c>[In progress]</c>.</summary>
    private static ContentNode Node(MermaidLine line)
    {
        if (!MermaidOutline.Node(line, KanbanRoles.Id, KanbanRoles.Title, "([){}@", out var titled)) return line.Shown(Shape);

        if (line.Sees(DataMark)) Data(line);
        line.Space();

        return line.Done && (titled || line.Rest.Length == 0) ? line.Read(KanbanKinds.Node) : line.Shown(Shape);
    }

    /// <summary>A node's metadata: <c>@{ ticket: MC-2037, assigned: 'knsv' }</c> — held with the reason where it will not read, or is never closed.</summary>
    private static void Data(MermaidLine line)
    {
        const string Written = "Metadata is written key: value, a comma between each: @{ assigned: knsv, priority: 'High' }.";

        line.Open();
        line.Token(DataMark, Roles.Open);
        line.Space();

        var read = line.Done || line.Sees("}") || line.Properties(Keys, ends: '}', what: "Metadata");
        if (!read) line.Words(KanbanRoles.Data, Written, until: "}");

        line.Space();
        var closed = line.Token("}", Roles.Close);
        line.Close(KanbanKinds.Data, trouble: closed ? null : "Metadata is closed with }.");
    }

    /// <summary>Whether a character ends a bare id.</summary>
    private static bool Stops(char character) => character is '(' or '[' or ')' or '{' or '}' or '@' or '"' or '%';
}
