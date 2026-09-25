using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Class;

namespace Nexaflow.Markdown.Nomnoml;

/// <summary>
/// A <c>nomnoml</c> block, read into the diagram it describes.
///
/// <para>
/// Nomnoml is UML class notation written shorter, so what a nomnoml block says <em>is</em> a class diagram: the same
/// classes with the same compartments, the same relations between them, the same boxes round the ones written together.
/// It is read into <see cref="ClassDiagram"/> and drawn by the class diagram's own builder, so a fix to how a class
/// diagram is laid out is a fix to both. What is nomnoml's alone is how it is written, which is
/// <see cref="NomnomlGrammar"/>'s.
/// </para>
/// </summary>
public static class NomnomlDiagram
{
    /// <summary>The grammar a nomnoml block is read by: its fence's language names it, since nothing in the block does.</summary>
    public static NomnomlGrammar Grammar { get; } = new();

    /// <summary>What a node says it is, where what it says is that it is not a class at all.</summary>
    public const string NoteWord = "note";

    /// <summary>Reads a block.</summary>
    

    /// <summary>Reads a block already parsed by <see cref="Grammar"/>.</summary>
    public static ClassDiagram Of(MermaidBlock block)
    {
        var reading = new Reading();

        // Not MermaidBlock.Statements: that is what a Mermaid line holds, and a nomnoml line holds its own kinds.
        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
            if (line.Stated() is { } stated)
                Line(stated, inside: null, reading);

        return ClassDiagram.From(block, reading.Way, [.. reading.Nodes.Select(Frozen)], reading.Relations,
                                 reading.Spaces, reading.Notes);
    }

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>One statement, read into whatever group it was written inside.</summary>
    private static void Line(ContentPart stated, string? inside, Reading reading)
    {
        switch (stated.Kind)
        {
            case NomnomlKinds.Directive: Directive(stated, reading); break;
            case NomnomlKinds.Group: Group(stated, inside, reading); break;
            case NomnomlKinds.Association: Association(stated, inside, reading); break;
            case NomnomlKinds.Node: Noded(stated, inside, reading); break;
        }
    }

    /// <summary>The one directive that changes what is drawn: which way the diagram runs.</summary>
    private static void Directive(ContentPart stated, Reading reading)
    {
        if (!string.Equals(Worded(stated, NomnomlRoles.Key)?.Text, NomnomlGrammar.DirectionWord, StringComparison.OrdinalIgnoreCase))
            return;

        reading.Way = Worded(stated, NomnomlRoles.Setting)?.Text.Trim().ToLowerInvariant() switch
        {
            "right" => ClassWay.Right,
            "left" => ClassWay.Left,
            "up" => ClassWay.Up,
            _ => ClassWay.Down,
        };
    }

    /// <summary>A node opened on one line and closed on another: a box, and everything written between them inside it.</summary>
    private static void Group(ContentPart stated, string? inside, Reading reading)
    {
        var opened = stated.Children.FirstOrDefault(child => child.Kind == NomnomlKinds.Node);
        if (opened is null) return;

        var key = Opened(opened, inside, reading);

        foreach (var child in stated.Children.Skip(1)) Line(child, key, reading);
    }

    /// <summary>One or more nodes joined end to end, and a relation for each operator between them.</summary>
    private static void Association(ContentPart stated, string? inside, Reading reading)
    {
        string? from = null, operation = null;
        ContentPart? near = null, far = null, said = null, marked = null;
        var made = new List<ClassRelation>();

        foreach (var child in stated.Children)
        {
            if (child.Kind is NomnomlKinds.Node)
            {
                var id = Noded(child, inside, reading);
                if (id.Length == 0) continue;

                if (from is not null && operation is not null && marked is not null)
                    made.Add(Related(marked, from, id, operation, near, far, reading.Relations.Count + made.Count));

                (from, operation, near, far, marked) = (id, null, null, null, null);
                continue;
            }

            if (child.Kind is NomnomlKinds.Association or NomnomlKinds.Group) { Line(child, inside, reading); continue; }

            switch (child.Role)
            {
                case NomnomlRoles.Operator: (operation, marked) = (child.Text, child); break;
                case NomnomlRoles.Near: near = child; break;
                case NomnomlRoles.Far: far = child; break;
                case NomnomlRoles.Said: said = child; break;
            }
        }

        // What the line says is written at the end of it, so it is only known once every relation on it has been made.
        foreach (var relation in made) reading.Relations.Add(said is null ? relation : relation with { Said = said });
    }

    /// <summary>One relation, as the operator between its two nodes draws it.</summary>
    private static ClassRelation Related(ContentPart part, string from, string to, string operation,
                                         ContentPart? near, ContentPart? far, int order)
    {
        var (head, tail, dotted) = Ends(operation);

        return new ClassRelation(part, from, to, head, tail, dotted, order) { Near = near, Far = far };
    }

    /// <summary>
    /// What an association draws at each end, and whether its line is dashed. The head is the end at the node on the
    /// left and the tail the end at the node on the right, which is the convention a class diagram's own relations keep.
    ///
    /// <para>
    /// Nomnoml spells an association as an end, a line and another end, so it is read back the same way: the line says
    /// whether it is dashed — only a single dash is solid — and each end says what is drawn there.
    /// </para>
    /// </summary>
    public static (ClassEnd Head, ClassEnd Tail, bool Dotted) Ends(string operation)
    {
        foreach (var drawn in NomnomlGrammar.Lines)
        {
            var at = operation.IndexOf(drawn, StringComparison.Ordinal);
            if (at < 0) continue;

            return (Ended(operation[..at]), Ended(operation[(at + drawn.Length)..]), drawn != "-");
        }

        return (ClassEnd.None, ClassEnd.None, false);
    }

    /// <summary>What one end of an association is drawn as.</summary>
    private static ClassEnd Ended(string mark) => mark switch
    {
        "<:" or ":>" => ClassEnd.Extension,
        "+" => ClassEnd.Composition,
        "o" => ClassEnd.Aggregation,
        "<" or ">" => ClassEnd.Association,

        // A ball and socket, which a class diagram draws as the lollipop it is.
        "(" or ")" or "(o" or "o)" or "o<" or ">o" => ClassEnd.Lollipop,
        _ => ClassEnd.None,
    };

    // ── Nodes ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One node: a note where it says it is one, a group where it holds nodes of its own, and a class otherwise. Hands
    /// back what a relation names it by.
    /// </summary>
    private static string Noded(ContentPart node, string? inside, Reading reading)
    {
        var named = Worded(node, NomnomlRoles.Id);
        var id = named?.Text.Trim() ?? string.Empty;
        var kind = Worded(node, NomnomlRoles.Classifier);

        // The bracket that closes a group is a node with nothing in it, and so is a node still being written.
        if (id.Length == 0) return string.Empty;

        if (string.Equals(kind?.Text.Trim(), NoteWord, StringComparison.OrdinalIgnoreCase))
        {
            reading.Notes.Add(new ClassNote(node, string.Empty, named, reading.Notes.Count));
            return id;
        }

        var compartments = node.Children.Where(child => child.Kind == NomnomlKinds.Compartment).ToList();
        var holding = compartments.Where(Holds).ToList();

        if (holding.Count > 0)
        {
            var key = Opened(node, inside, reading);

            // A compartment holding nodes reads exactly as a line of them does, operators and all.
            foreach (var compartment in holding) Association(compartment, key, reading);
            return id;
        }

        var made = Making(id, node, named, inside, reading);
        made.Kind ??= kind;

        foreach (var member in compartments.SelectMany(compartment => compartment.Children)
                                           .Where(child => child.Role == NomnomlRoles.Member))
            made.Members.Add(ClassMember.Of(member, member.Text.Trim(), made.Members.Count));

        return id;
    }

    /// <summary>Whether a compartment holds nodes rather than members, which is what makes its node a group.</summary>
    private static bool Holds(ContentPart compartment) =>
        compartment.Children.Any(child => child.Kind is NomnomlKinds.Node or NomnomlKinds.Association);

    /// <summary>A node that boxes others: its own space in the layout, named by what is written at the top of it.</summary>
    private static string Opened(ContentPart node, string? inside, Reading reading)
    {
        var named = Worded(node, NomnomlRoles.Id);
        var id = named?.Text.Trim() ?? string.Empty;
        var key = inside is null ? id : inside + "." + id;

        reading.Spaces.Add(new ClassSpace(node, key, key, id, inside, reading.Spaces.Count) { Said = named, Whole = node });
        return key;
    }

    /// <summary>The node this id stands for — the one already made for it, or a new one. A node written twice is one node.</summary>
    private static Make Making(string id, ContentPart node, ContentPart? named, string? inside, Reading reading)
    {
        if (reading.Known.TryGetValue(id, out var already))
        {
            already.Said ??= named;
            already.Group ??= inside;
            return already;
        }

        var made = new Make(node, id, reading.Known.Count) { Said = named, Group = inside };
        reading.Known[id] = made;
        reading.Nodes.Add(made);
        return made;
    }

    /// <summary>What was being put together, as the diagram reads it.</summary>
    private static ClassNode Frozen(Make make) =>
        new(make.Part, make.Id, make.Said, make.Group, MermaidStyle.None, make.Order)
        {
            Kind = make.Kind,
            Members = make.Members,
            Whole = make.Part,
        };

    /// <summary>The first part beneath this one written in <paramref name="role"/>.</summary>
    private static ContentPart? Worded(ContentPart part, string role) =>
        part.SelfAndDescendants().FirstOrDefault(found => found.Role == role);

    /// <summary>A node while it is being put together, which a later line may say more about.</summary>
    private sealed class Make(ContentPart part, string id, int order)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        public int Order { get; } = order;

        public ContentPart? Said { get; set; }

        public ContentPart? Kind { get; set; }

        public string? Group { get; set; }

        public List<ClassMember> Members { get; } = [];
    }

    /// <summary>Everything the block has said so far.</summary>
    private sealed class Reading
    {
        public ClassWay Way { get; set; } = ClassWay.Down;

        public Dictionary<string, Make> Known { get; } = new(StringComparer.Ordinal);

        public List<Make> Nodes { get; } = [];

        public List<ClassRelation> Relations { get; } = [];

        public List<ClassSpace> Spaces { get; } = [];

        public List<ClassNote> Notes { get; } = [];
    }
}
