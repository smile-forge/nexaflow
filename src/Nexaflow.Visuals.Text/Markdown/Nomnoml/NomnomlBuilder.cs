using System;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Class;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Class;

namespace Nexaflow.Visuals.Text.Markdown.Nomnoml;

/// <summary>
/// Draws a <c>nomnoml</c> block.
///
/// <para>
/// Nomnoml is UML class notation written its own way, and what it draws is a class diagram — the same classes with the same
/// compartments, the same relations between them, the same boxes round the ones written together. So the drawing is the
/// class diagram's own (<see cref="ClassBuilder"/>), and a fix to how a class diagram is laid out is a fix to both; what is
/// here is only the walk down a nomnoml tree (<see cref="NomnomlParser"/>) into what that drawing is made from.
/// </para>
/// <para>
/// A node's compartments are its bands as written: the first past its name is drawn above the rule, and every one after it
/// below. A node whose compartment holds nodes of its own is a box drawn round them, and so is a group written across lines.
/// </para>
/// </summary>
internal sealed class NomnomlBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
    : ClassBuilder(reading, state, style, isReadOnly, nesting)
{
    /// <summary>What a node says it is, where what it says is that it is not a class at all.</summary>
    private const string NoteWord = "note";

    /// <inheritdoc/>
    protected override Diagram Read(ContentPart root, ClassConfig config)
    {
        var diagram = new Diagram(config);
        Walk(root.Children, inside: null, diagram);
        return diagram;
    }

    /// <summary>
    /// Lines, read into whatever group they were written inside. A group is the line opening it, which names its box, and
    /// the lines after that, which are inside it.
    /// </summary>
    private static void Walk(System.Collections.Generic.IEnumerable<ContentPart> lines, string? inside, Diagram diagram)
    {
        foreach (var part in lines)
        {
            if (part.Kind == NomnomlKinds.Group)
            {
                if (Said(part.Children.FirstOrDefault()) is { Kind: NomnomlKinds.Node } opened)
                    Walk(part.Children.Skip(1), Opened(opened, inside, diagram), diagram);

                continue;
            }

            switch (Said(part))
            {
                case { Kind: NomnomlKinds.Directive } directive:
                    Directive(directive, diagram);
                    break;

                case { Kind: NomnomlKinds.Association } association:
                    Association(association, inside, diagram);
                    break;

                case { Kind: NomnomlKinds.Node } node:
                    Noded(node, inside, diagram);
                    break;
            }
        }
    }

    /// <summary>What a line says: the first thing on it that is not space.</summary>
    private static ContentPart? Said(ContentPart? line) =>
        line?.Kind == NomnomlKinds.Line ? line.Children.FirstOrDefault(child => child.Kind != Kinds.Space) : null;

    /// <summary>The one directive that changes what is drawn: which way the diagram runs.</summary>
    private static void Directive(ContentPart directive, Diagram diagram)
    {
        if (!string.Equals(Worded(directive, NomnomlRoles.Key)?.Text, NomnomlParser.DirectionWord, StringComparison.OrdinalIgnoreCase))
            return;

        diagram.Way = Worded(directive, NomnomlRoles.Setting)?.Text.Trim().ToLowerInvariant() switch
        {
            "right" => DiagramWay.Right,
            "left" => DiagramWay.Left,
            "up" => DiagramWay.Up,
            _ => DiagramWay.Down,
        };
    }

    /// <summary>One or more nodes joined end to end, and a relation for each operator between them.</summary>
    private static void Association(ContentPart association, string? inside, Diagram diagram)
    {
        string? from = null;
        ContentPart? operation = null, near = null, far = null, said = null;
        var made = new System.Collections.Generic.List<Relation>();

        foreach (var child in association.Children)
        {
            if (child.Kind == NomnomlKinds.Node)
            {
                var id = Noded(child, inside, diagram);
                if (id.Length == 0) continue;

                if (from is not null && operation is not null) made.Add(Related(operation, from, id, near, far));

                (from, operation, near, far) = (id, null, null, null);
                continue;
            }

            if (child.Kind == NomnomlKinds.Operator)
            {
                operation = child;
                continue;
            }

            switch (child.Role)
            {
                case NomnomlRoles.Near: near = child; break;
                case NomnomlRoles.Far: far = child; break;
                case NomnomlRoles.Said: said = child; break;
            }
        }

        // What the line says is written at the end of it, so it is only known once every relation on it has been made.
        foreach (var relation in made) diagram.Relations.Add(said is null ? relation : relation with { Said = said });
    }

    /// <summary>
    /// One relation, as its operator draws it: the head is the end at the node on the left and the tail the end at the node on
    /// the right, which is the convention a class diagram's own relations keep, and only a single dash is drawn solid.
    /// </summary>
    private static Relation Related(ContentPart operation, string from, string to, ContentPart? near, ContentPart? far) =>
        new(operation, from, to, Ended(Piece(operation, NomnomlRoles.Head)), Ended(Piece(operation, NomnomlRoles.Tail)),
            Piece(operation, NomnomlRoles.Drawn) != "-")
        {
            Near = near,
            Far = far,
        };

    private static string? Piece(ContentPart operation, string role) => operation.Children.FirstOrDefault(piece => piece.Role == role)?.Text;

    /// <summary>What one end of an operator draws.</summary>
    private static DiagramHead Ended(string? mark) => mark switch
    {
        "<:" or ":>" => DiagramHead.Triangle,
        "+" => DiagramHead.Diamond,
        "o" => DiagramHead.HollowDiamond,
        "<" or ">" => DiagramHead.Open,

        // A ball and socket, which a class diagram draws as the lollipop it is.
        "(" or ")" or "(o" or "o)" or "o<" or ">o" => DiagramHead.Circle,
        _ => DiagramHead.None,
    };

    /// <summary>
    /// One node: a note where it says it is one, a box round others where it holds nodes of its own, and a class otherwise.
    /// Hands back what a relation names it by.
    /// </summary>
    private static string Noded(ContentPart node, string? inside, Diagram diagram)
    {
        var named = Worded(node, NomnomlRoles.Id);
        var id = named?.Text.Trim() ?? string.Empty;
        var kind = Worded(node, NomnomlRoles.Classifier);

        // A node still being written has nothing to be called by.
        if (id.Length == 0) return string.Empty;

        if (string.Equals(kind?.Text.Trim(), NoteWord, StringComparison.OrdinalIgnoreCase))
        {
            diagram.Notes.Add(new Note(node, string.Empty, named, null));
            return id;
        }

        var compartments = node.Children.Where(child => child.Kind == NomnomlKinds.Compartment).ToList();
        var holding = compartments.Where(Holds).ToList();

        if (holding.Count > 0)
        {
            var key = Opened(node, inside, diagram);

            // A compartment holding nodes reads exactly as a line of them does, operators and all.
            foreach (var compartment in holding) Association(compartment, key, diagram);
            return id;
        }

        var made = diagram.Find(id);
        if (made is null)
        {
            made = diagram.Made(node, id, inside);
            made.Whole = node;
        }

        made.Said ??= named;
        made.Group ??= inside;
        made.Kind ??= kind;

        for (var band = 0; band < compartments.Count; band++)
            foreach (var member in compartments[band].Children.Where(child => child.Role == NomnomlRoles.Member))
                made.Members.Add(new Member(member, member.Text, Method: band > 0));

        return id;
    }

    /// <summary>Whether a compartment holds nodes rather than members, which is what makes its node a box round them.</summary>
    private static bool Holds(ContentPart compartment) =>
        compartment.Children.Any(child => child.Kind is NomnomlKinds.Node or NomnomlKinds.Association);

    /// <summary>A node that boxes others: its own space in the layout, named by what is written at the top of it.</summary>
    private static string Opened(ContentPart node, string? inside, Diagram diagram)
    {
        var named = Worded(node, NomnomlRoles.Id);
        var id = named?.Text.Trim() ?? string.Empty;
        var key = inside is null ? id : inside + "." + id;

        diagram.Spaces.Add(new Namespace(node, key, id, inside) { Said = named, Whole = node });
        return key;
    }

    /// <summary>The first part beneath this one written in <paramref name="role"/>.</summary>
    private static ContentPart? Worded(ContentPart part, string role) =>
        part.SelfAndDescendants().FirstOrDefault(found => found.Role == role);
}
