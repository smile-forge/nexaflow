using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Gathers the music between two bar lines into a measure.
///
/// <para>
/// A bar is the unit almost everything downstream works in: accidentals last one, a system is a whole
/// number of them, justification measures them, and clicking the background of one selects it. So it is
/// worth being a node, and the bar lines that close it are its own machinery — a measure carries the line
/// that opened it and the line that closed it, in the places they were written.
/// </para>
/// <para>
/// <strong>A measure never crosses a source line.</strong> A bar that runs on to the next line comes back
/// as two measures, the first with no line closing it. That is not a compromise: a line ending in ABC is
/// a suggested system break, so the two halves are drawn on different systems anyway, and a node that
/// spanned the break would have to hold the line ending in the middle of itself and would still draw as
/// two. The builder joins them for the purpose of counting time; the reader selects the half in front of
/// them.
/// </para>
/// </summary>
public sealed class GroupBars : IAstStage
{
    public string Name => "abc:bars";

    /// <summary>Whether anything in this run is music rather than punctuation left over at a line end.</summary>
    private static bool IsMusic(ContentNode node) =>
        node.Kind is AbcKinds.Note or AbcKinds.Chord or AbcKinds.Rest or AbcKinds.Beam
                  or AbcKinds.TupletGroup or AbcKinds.Grace or AbcKinds.Spacer;

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Regrouping(tree, (node, children) =>
            node.Kind == AbcKinds.Line ? Barred(children) : null);

    private static IReadOnlyList<ContentNode>? Barred(IReadOnlyList<ContentNode> children)
    {
        var rebuilt = new List<ContentNode>(children.Count);
        var pending = new List<ContentNode>();
        var opened = (ContentNode?)null;
        var moved = false;

        // A leading bar line opens the first measure rather than closing an empty one, which is what a
        // tune whose lines all start with `|` means and what would otherwise produce a measure of nothing
        // at the head of every line.
        foreach (var child in children)
        {
            if (IsBarline(child))
            {
                if (pending.Any(IsMusic))
                {
                    rebuilt.Add(Measure(opened, pending, child));
                    pending.Clear();
                    opened = null;
                    moved = true;
                    continue;
                }

                // Nothing to close. Whatever was pending is trivia at the head of the line, and this line
                // opens the next measure — but a line that was already waiting to open one opened nothing
                // after all, and has to be put back where it was written. Real tunebooks do write
                // `|  |  |  | E4E2E2 |` at the head of a line, and dropping the first three of those was
                // the only thing ten thousand of them caught that the constructs did not.
                if (opened is not null) rebuilt.Add(opened);
                rebuilt.AddRange(pending);
                pending.Clear();
                opened = child;
                continue;
            }

            pending.Add(child);
        }

        if (pending.Any(IsMusic))
        {
            // The line ending, and any comment before it, belong to the line rather than to the last bar
            // on it. Left inside, they would be part of what selecting that bar selects, and a bar whose
            // source range ran to the start of the next line is a bar an edit would splice wrongly.
            var trailing = pending.Count;
            while (trailing > 0 && pending[trailing - 1].Kind is Kinds.Space or Kinds.Comment) trailing--;

            rebuilt.Add(Measure(opened, pending[..trailing], null));
            rebuilt.AddRange(pending[trailing..]);
            moved = true;
        }
        else
        {
            if (opened is not null) rebuilt.Add(opened);
            rebuilt.AddRange(pending);
        }

        return moved ? rebuilt : null;
    }

    private static bool IsBarline(ContentNode node) =>
        node.Kind == AbcKinds.Barline;

    /// <summary>
    /// One measure: the line that opened it, what is in it, and the line that closed it — each in the
    /// place it was written, which is what keeps the tune printing as it was typed. A measure with no line
    /// after it runs on to whatever comes next.
    /// </summary>
    private static ContentNode Measure(ContentNode? opened, IReadOnlyList<ContentNode> inside, ContentNode? closed)
    {
        var pieces = new List<ContentNode>(inside.Count + 2);

        if (opened is not null) pieces.Add(opened.As(Roles.Open));
        pieces.AddRange(inside);
        if (closed is not null) pieces.Add(Closing(closed));

        return ContentNode.Branch(AbcKinds.Measure, pieces);
    }

    // ── The line that closes it ─────────────────────────────────────────────

    /// <summary>
    /// The line closing a measure, saying whether it is a repeat line: whether it carries the dots on either
    /// side, as <c>:|</c>, <c>|:</c>, <c>::</c> and <c>:|]</c> do. An ending written before a <c>|:</c> ends
    /// there as surely as one written before a <c>:|</c>.
    /// </summary>
    private static ContentNode Closing(ContentNode line)
    {
        var spelled = line.IsLeaf ? line.Text : line.Part(Roles.Name)?.Text ?? "";
        return new AbcBarlineNode(line.As(Roles.Close), spelled.Contains(':'));
    }
}
