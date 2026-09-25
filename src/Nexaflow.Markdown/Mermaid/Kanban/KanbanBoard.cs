using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Kanban;

/// <summary>A card: what its title says, and its metadata.</summary>
/// <param name="Part">The card as written — what pressing it means.</param>
/// <param name="Title">What its title says: its title in brackets, or its bare id — null where it is still to write.</param>
/// <param name="Hole">The hole standing where its title is still to write.</param>
/// <param name="Label">A title its metadata's <c>label</c> gives it instead.</param>
/// <param name="Link">Where its ticket links to, where the front matter says.</param>
public sealed record KanbanCard(ContentPart Part, ContentPart? Title, ContentPart? Hole, string? Label,
                                string? Ticket, string? Assigned, string? Priority, string? Icon, string? Link);

/// <summary>A column: what its title says, and its cards in the order they are written.</summary>
public sealed record KanbanColumn(ContentPart Part, ContentPart? Title, ContentPart? Hole, string? Label, IReadOnlyList<KanbanCard> Cards);

/// <summary>
/// A <c>kanban</c> block, read: its columns, each holding the cards written under it. A node the stage says is a column starts
/// one; any other node is a card in the last column started. Metadata's values are read without the quotes round them.
/// </summary>
public sealed class KanbanBoard
{
    private KanbanBoard(MermaidBlock block, KanbanConfig config) => (Block, Config) = (block, config);

    

    public static KanbanBoard Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static KanbanBoard Of(MermaidBlock block)
    {
        var board = new KanbanBoard(block, KanbanConfig.Read(block.Config));
        var columns = new List<(ContentPart Part, List<KanbanCard> Cards)>();

        foreach (var node in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == KanbanKinds.Node && part.Trouble is null))
        {
            if (node.Fact(KanbanRoles.Column) is not null)
            {
                columns.Add((node, []));
                continue;
            }

            if (columns.Count == 0) continue;

            var data = Data(node);
            var ticket = data.GetValueOrDefault("ticket");
            columns[^1].Cards.Add(new KanbanCard(
                node, Title(node), Hole(node), data.GetValueOrDefault("label"), ticket, data.GetValueOrDefault("assigned"),
                data.GetValueOrDefault("priority"), data.GetValueOrDefault("icon"),
                ticket is not null && board.Config.TicketBaseUrl is { } url ? url.Replace("#TICKET#", ticket, StringComparison.Ordinal) : null));
        }

        board.Columns = [.. columns.Select(column => new KanbanColumn(column.Part, Title(column.Part), Hole(column.Part), Data(column.Part).GetValueOrDefault("label"), column.Cards))];
        return board;
    }

    public MermaidBlock Block { get; }

    public KanbanConfig Config { get; }

    public IReadOnlyList<KanbanColumn> Columns { get; private set; } = [];

    /// <summary>A node's title: the words in its brackets, or else its bare id.</summary>
    private static ContentPart? Title(ContentPart node) =>
        node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label).Words() is { Length: > 0 } label ? label
        : node.Children.Any(child => child.Kind == MermaidKinds.Label) ? null
        : node.Children.FirstOrDefault(child => child is { Kind: MermaidKinds.Name, Role: KanbanRoles.Id }).Words() is { Length: > 0 } id ? id
        : null;

    private static ContentPart? Hole(ContentPart node) => node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label).Hole();

    /// <summary>What a node's metadata sets, by key in lower case, each value without the quotes round it.</summary>
    private static Dictionary<string, string> Data(ContentPart node)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in node.Inner(KanbanKinds.Data)?.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Property) ?? [])
        {
            var key = property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key)?.Text;
            var value = property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting)?.Text.Trim() ?? string.Empty;
            if (key is null || value.Length == 0) continue;

            if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0]) value = value[1..^1];
            data[key.Trim()] = value;
        }

        return data;
    }
}
