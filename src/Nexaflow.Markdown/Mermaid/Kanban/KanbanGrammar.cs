using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Kanban.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Kanban;

/// <summary>
/// What a <c>kanban</c> block says beyond the lines every diagram shares: its columns and cards, each a node, and the
/// <c>::icon(…)</c> and <c>:::class</c> lines decorating the node above them.
///
/// <para>
/// The rules are Mermaid's. A node is its id, its title in brackets — <c>[…]</c>, <c>(…)</c>, <c>((…))</c>, <c>{{…}}</c>,
/// <c>)…(</c>, <c>))…((</c> — or both, a bare id being its title too; then its metadata, <c>@{ assigned: knsv, ticket: MC-2037,
/// priority: 'High' }</c>. The first node's indentation is a column's; a node indented further is a card in the column above
/// it. Which a node is, is a fact about the block rather than its line, so it is the stage's (<see cref="ResolveColumns"/>).
/// </para>
/// </summary>
public sealed class KanbanGrammar : IMermaidGrammar
{
    public const string IconMark = "::icon(";
    public const string ClassMark = ":::";
    public const string DataMark = "@{";

    /// <summary>What a card's metadata sets.</summary>
    public static readonly IReadOnlyList<string> Keys = ["assigned", "ticket", "priority", "label", "icon", "shape"];

    /// <summary>A card's priorities, the most urgent first.</summary>
    public static readonly IReadOnlyList<string> Priorities = ["Very High", "High", "Medium", "Low", "Very Low"];

    /// <summary>The brackets a title is written in, each opening with its closing, the longest first.</summary>
    public static readonly IReadOnlyList<(string Open, string Close)> Brackets =
        [("((", "))"), ("{{", "}}"), ("))", "(("), ("-)", "(-"), ("(-", "-)"), ("[", "]"), ("(", ")"), (")", "(")];

    private const string Shape = "A column or a card is its id, its title in brackets, or both, and any metadata after: id3[Update the database]@{ assigned: knsv }.";

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        if (line.Sees(ClassMark)) return Decoration(line, ClassMark, null, KanbanRoles.Class, KanbanKinds.Class);
        if (line.Sees(IconMark)) return Decoration(line, IconMark, ")", KanbanRoles.Icon, KanbanKinds.Icon);
        return Node(line);
    }

    /// <inheritdoc/>
    /// <remarks>Under a column, a card with its title still to write, indented under it; anywhere else, one as far in as the line above.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) =>
        above?.Kind == KanbanKinds.Node && above.Said(KanbanRoles.Column) is not null ? ("  [\"\"]", 4) : ("[\"\"]", 2);

    /// <inheritdoc/>
    /// <remarks>
    /// A title in brackets is put in quotes to hold a quote, a bracket closing it or a comment. A bare id that is its own title is
    /// written as a title in quotes to hold anything an id cannot; an id beside a title cannot hold those at all, so they are not written.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        // An icon's name runs to its bracket, and metadata to its brace, so neither can hold the one closing it.
        var closing = part.Role switch { KanbanRoles.Icon => ")", KanbanRoles.Data => "}", _ => null };
        if (closing is not null && text.Contains(closing, StringComparison.Ordinal))
        {
            var kept = text.Replace(closing, string.Empty, StringComparison.Ordinal);
            return new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }
        if (part.Parent is not { Kind: MermaidKinds.Name, Role: KanbanRoles.Id } id || !text.Any(Stops)) return null;

        if (id.Parent?.Children.Any(child => child.Kind == MermaidKinds.Label) == true)
        {
            var kept = new string([.. text.Where(character => !Stops(character))]);
            return new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var head = "[\"" + MermaidText.Quoted(said[..at] + text);
        return new MermaidWriting(part.Start, part.Start + said.Length, head + MermaidText.Quoted(said[at..]) + "\"]", part.Start + head.Length);
    }

    /// <inheritdoc/>
    /// <remarks>Whether each node is a column or a card, by its indentation over the whole block (<see cref="ResolveColumns"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [new ResolveColumns()];

    /// <inheritdoc/>
    /// <remarks>Where a title is still to write, between its quotes.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind == MermaidKinds.Quoted;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>A column or a card: <c>id3[Update the database]@{ assigned: knsv }</c>, <c>Todo</c>, <c>[In progress]</c>.</summary>
    private static ContentNode Node(MermaidLine line)
    {
        var titled = Bracket(line) is not null;
        if (!titled)
        {
            line.Open();
            line.Words(KanbanRoles.Id, until: "([){}@");
            line.Close(MermaidKinds.Name, KanbanRoles.Id);
            line.Space();
        }

        if (Bracket(line) is var (open, close))
        {
            if (!line.Label(open, close, KanbanRoles.Title)) return line.Shown(Shape);
            titled = true;
            line.Space();
        }

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

    /// <summary>A line decorating the node above it: <c>::icon(fa fa-book)</c>, <c>:::urgent</c>.</summary>
    private static ContentNode Decoration(MermaidLine line, string mark, string? close, string role, string kind)
    {
        line.Token(mark, Roles.Open);
        line.Words(role, until: close);
        line.Space();
        if (close is not null && !line.Token(close, Roles.Close) && !line.Done) return line.Shown($"An icon is written {IconMark}name).");

        return line.Done ? line.Read(kind) : line.Shown($"An icon is written {IconMark}name).");
    }

    /// <summary>The brackets a title opens with next, where one does.</summary>
    private static (string Open, string Close)? Bracket(MermaidLine line)
    {
        foreach (var bracket in Brackets)
            if (line.Sees(bracket.Open))
                return bracket;

        return null;
    }

    /// <summary>Whether a character ends a bare id.</summary>
    private static bool Stops(char character) => character is '(' or '[' or ')' or '{' or '}' or '@' or '"' or '%';
}
