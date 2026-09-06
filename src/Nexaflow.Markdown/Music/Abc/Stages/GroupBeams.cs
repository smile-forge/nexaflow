using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Gathers the events written together — with no space between them — into a group.
///
/// <para>
/// In ABC that is what a beam <em>is</em>: whitespace groups the notes, and everything else about the
/// beam follows from their lengths. So this stage is purely about what was typed, and says nothing about
/// whether a beam is actually drawn — four quarter notes written <c>ABcd</c> are one written group and
/// get no beam, which is the builder's answer, not this one's.
/// </para>
/// <para>
/// <strong>It is also the unit of selection, which is the reason it is a node rather than a flag.</strong>
/// A reader clicking a beamed pair means the pair, not one head of it; a reader dragging across a line
/// means whole groups. Both fall out of the layout tree on their own once the group is something the
/// source names, and neither is expressible as a number on each note.
/// </para>
/// <para>
/// Runs of one are left alone. A group of one is not a group, and wrapping every lone note in a node
/// would put a rung in every walk that says nothing.
/// </para>
/// </summary>
public sealed class GroupBeams : IAstStage
{
    public string Name => "abc:beams";

    /// <summary>What can be beamed to what: the things that take time and can carry a stem.</summary>
    private static bool IsBeamable(ContentNode node) =>
        node.Kind is AbcKinds.Note or AbcKinds.Chord;

    /// <summary>
    /// What sits between two events without breaking the run: an ornament, a chord symbol, a tie, a slur
    /// mark, grace notes. All of them belong to the note beside them, so a beam written straight through
    /// one is still written straight through.
    /// </summary>
    private static bool RidesAlong(ContentNode node) =>
        node.Kind is AbcKinds.Decoration or AbcKinds.Annotation or AbcKinds.Grace
                  or AbcKinds.SlurOpen or AbcKinds.SlurClose or AbcKinds.Tie or AbcKinds.Broken
                  or AbcKinds.Tuplet;

    // Inside a tuplet as well as along a line: a tuplet beams as one group, and it is already a node by
    // the time this runs, so its notes are its children rather than the line's.
    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Regrouping(tree, (node, children) =>
            node.Kind is AbcKinds.Line or AbcKinds.TupletGroup ? Beamed(children) : null);

    private static IReadOnlyList<ContentNode>? Beamed(IReadOnlyList<ContentNode> children)
    {
        var rebuilt = new List<ContentNode>(children.Count);
        var moved = false;
        var at = 0;

        while (at < children.Count)
        {
            var run = Run(children, at);

            // Two events, at least, and the pieces that ride along with them. One event is not a group.
            if (run.Events >= 2)
            {
                rebuilt.Add(ContentNode.Branch(
                    AbcKinds.Beam,
                    [.. children.Skip(at).Take(run.Length).Select(child =>
                        IsBeamable(child) ? child.As(AbcRoles.Event) : child)]));

                at += run.Length;
                moved = true;
                continue;
            }

            rebuilt.Add(children[at]);
            at++;
        }

        return moved ? rebuilt : null;
    }

    /// <summary>
    /// How far the run starting here goes, and how many events are in it. It stops at anything that is
    /// not an event or one of the pieces that ride along with one — a space, a bar line, a line ending —
    /// and it never ends on a rider, so a decoration written after the last note of a group is not
    /// dragged into it.
    /// </summary>
    private static (int Length, int Events) Run(IReadOnlyList<ContentNode> children, int from)
    {
        int at = from, events = 0, last = from;

        while (at < children.Count)
        {
            var child = children[at];

            if (IsBeamable(child)) { events++; at++; last = at; continue; }
            if (RidesAlong(child)) { at++; continue; }
            break;
        }

        return (last - from, events);
    }
}
