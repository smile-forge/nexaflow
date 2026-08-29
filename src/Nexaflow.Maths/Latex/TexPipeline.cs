using System.Text;
using System.Linq;

namespace Nexaflow.Maths.Latex;

/// <summary>
/// Reading a formula, in stages: the parser makes a tree of what was written, and each stage after it
/// takes a tree and returns a tree.
///
/// <para>
/// The one rule every stage obeys is that <strong>the tree still prints as the source it came
/// from</strong>. A stage may nest differently, replace a piece with another piece, or hang something
/// underneath — as long as the characters that come back out are the ones that went in. That is what
/// lets the tree, rather than the string, be the thing that is edited: what is drawn points back at it,
/// an edit changes it, and it can always say what it is in source again.
/// </para>
/// <para>
/// Nothing here is incremental. An edit can put anything anywhere, including a <c>}</c> that reshapes
/// everything after it, so "is this edit contained in that piece" is not a question worth trying to
/// answer cheaply — the tree prints itself back and the whole of this runs again. One path, always
/// taken, therefore always right.
/// </para>
/// <para>
/// Macro expansion is the stage that is not here: it happens as the parser reads, because what a name
/// is shorthand for is a fact about the text and no later stage can change it. It would move here
/// without anything else changing.
/// </para>
/// <para>
/// It is a method and two functions, deliberately. There is nothing to register with and no order to
/// configure — the order is the point, and it is written down once, below.
/// </para>
/// </summary>
public static class TexPipeline
{
    /// <summary>
    /// The tree to build a formula from: what was written, with anything that cannot be drawn — and
    /// anything currently being typed — shown as the characters it is made of.
    /// </summary>
    /// <param name="draws">
    /// Whether whatever is going to set this tree knows how to draw a command, given its name as
    /// written, backslash and all. Asked for rather than known, because what can be drawn is a fact
    /// about a typesetter and this is a reader.
    /// </param>
    /// <param name="editing">
    /// A stretch somebody is in the middle of typing, shown rather than read for as long as they are.
    /// Runs last on purpose: a half-written command is invalid almost by definition, and saying so on
    /// every keystroke would be the wrong thing to draw.
    /// </param>
    public static TexNode Read(string latex, Func<string, bool>? draws = null,
                               (int Start, int Length)? editing = null, bool holes = false)
    {
        var tree = Gathered(TexParser.Parse(latex));

        if (holes) tree = WithHoles(tree);
        if (draws is not null) tree = Checked(tree, draws);
        if (editing is { } zone) tree = ShownAsWritten(tree, zone.Start, zone.Length);

        return tree;
    }

    /// <summary>
    /// The same tree with a stretch of it shown as the characters it was written with rather than read
    /// as maths.
    ///
    /// <para>
    /// The stretch is widened to whole pieces — a caret three characters into <c>\frac</c> is not
    /// editing three characters, it is editing a fraction — and taken as deep as it will go, so that
    /// typing in one cell of a table does not stop the table being a table.
    /// </para>
    /// </summary>
    public static TexNode ShownAsWritten(TexNode tree, int start, int length) =>
        length <= 0 ? tree : Show(tree, 0, start, start + length) ?? tree;

    /// <summary>
    /// The same tree with every command nothing can draw shown as the characters it is made of, and
    /// carrying the reason it is.
    /// </summary>
    public static TexNode Checked(TexNode tree, Func<string, bool> draws) => Check(tree, draws);

    /// <summary>
    /// The same tree with a hole put in every argument and every cell left empty.
    ///
    /// <para>
    /// This half only says one belongs there. What it looks like is the builder's, which turns it into
    /// something drawable the same way it turns every other piece into something drawable — the reading
    /// says what is true of the formula, and the setting says what a reader sees.
    /// </para>
    /// <para>
    /// A hole stands for nothing that was written, so it takes up none of the source and the tree still
    /// prints as what it came from. It is the same kind of piece as a macro's expansion and is there for
    /// the same reason: to say something the characters do not.
    /// </para>
    /// <para>
    /// Asked for by a surface being written on, where the hole is how a reader sees there is something
    /// still to write and how they aim at it. Off by default, because a box in the middle of a formula
    /// that is only being read would simply be wrong, and reading is the commoner case.
    /// </para>
    /// </summary>
    public static TexNode WithHoles(TexNode tree) => Hollow(tree);

    /// <summary>
    /// The same tree with a sign that was written as several things gathered into the one node it means.
    ///
    /// <para>
    /// The mirror of macro expansion, and it is worth seeing them as a pair. Expansion hangs structure
    /// underneath that stands for <em>no</em> source — zero width, never printed. This re-nests structure
    /// that stands for <em>all</em> of its source: every token stays, in the order it was written, and only
    /// the shape over them changes. Both leave the tree printing as what it came from, which is the one
    /// rule here.
    /// </para>
    /// <para>
    /// <c>\not</c> is the case that needs it. It draws a slash over whatever follows, so <c>\not=</c> is
    /// already one node meaning one sign — but physics writes <c>\not\!p</c>, pulling the slash onto the
    /// letter with a kern, and a kern is not something to draw over. Read strictly, the <c>\!</c> becomes
    /// the argument and the <c>p</c> is left outside as a neighbour, which is neither what was meant nor
    /// something the builder can act on without reaching sideways out of its own node.
    /// </para>
    /// <para>
    /// TeX agrees with the gathering rather than with the strict reading: <c>\not</c> overlays the next
    /// <em>atom</em>, and a kern is not an atom. So this is the faithful shape and the parser's is the
    /// literal one — which is the division of labour, the parser reading what is written and a stage
    /// saying what it amounts to.
    /// </para>
    /// </summary>
    public static TexNode Gathered(TexNode tree) => Gather(tree);

    /// <summary>Whether this piece takes up room without drawing anything - a kern, or written space.</summary>
    private static bool IsRoom(TexNode node) =>
        node.Kind is TexKind.Space or TexKind.Comment
        || (node.Kind == TexKind.Command
            && node.Part(TexRole.Name)?.Text is { } name
            && TexCommands.IsSpacing(name));

    /// <summary>A <c>\not</c> whose argument turned out to be a kern, so what it slashes is further on.</summary>
    private static bool IsReaching(TexNode node) =>
        node.Kind == TexKind.Command
        && node.Part(TexRole.Name)?.Text == @"\not"
        && node.Part(TexRole.Base) is { } written
        && IsRoom(written);

    private static TexNode Gather(TexNode node)
    {
        if (node.IsLeaf) return node;

        var rebuilt = new List<TexNode>(node.Children.Count);
        var moved = false;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = Gather(node.Children[at]);
            moved |= !ReferenceEquals(child, node.Children[at]);

            if (IsReaching(child))
            {
                // Everything between the slash and what it is drawn over comes with it: the kern is what
                // puts the one on the other, so it belongs inside the sign rather than beside it.
                var next = at + 1;
                var between = new List<TexNode>();
                while (next < node.Children.Count && IsRoom(node.Children[next]))
                    between.Add(Gather(node.Children[next++]));

                if (next < node.Children.Count)
                {
                    rebuilt.Add(Slashing(child, between, Gather(node.Children[next])));
                    at = next;
                    moved = true;
                    continue;
                }
            }

            rebuilt.Add(child);
        }

        return moved ? node.With(rebuilt) : node;
    }

    /// <summary>
    /// One <c>\not</c> node holding the whole sign: its name, the room written after it, and the thing the
    /// slash goes over — in the order they were written, which is what keeps this printable.
    /// </summary>
    private static TexNode Slashing(TexNode reaching, List<TexNode> between, TexNode over)
    {
        var children = new List<TexNode>(reaching.Children.Count + between.Count + 1);

        // The old argument was the first kern. It keeps its place and stops being the argument.
        foreach (var child in reaching.Children)
            children.Add(child.Role == TexRole.Base ? child.As(TexRole.Element) : child);

        children.AddRange(between);
        children.Add(over.As(TexRole.Base));

        return reaching.With(children);
    }

    private static TexNode Hollow(TexNode node)
    {
        if (node.IsLeaf) return node;

        var rebuilt = new List<TexNode>(node.Children.Count + 1);
        var moved = false;

        foreach (var child in node.Children)
        {
            var seen = Hollow(child);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        // Nothing written between the braces, or between one separator and the next. Machinery does not
        // count as something being there: `{}` is a hole and so is the cell after the last `&`.
        var empty = !rebuilt.Any(child => child.Width > 0
                                          && child.Role is not (TexRole.Open or TexRole.Close or TexRole.Separator));

        if (empty && node.Kind is TexKind.Group or TexKind.Cell)
        {
            rebuilt.Insert(rebuilt.FindIndex(child => child.Role == TexRole.Open) + 1,
                           TexNode.Leaf(TexKind.Hole, string.Empty, TexRole.Element,
                                        "Something still has to go here."));
            moved = true;
        }

        return moved ? node.With(rebuilt) : node;
    }

    // ── Showing a stretch as written ─────────────────────────────────────────

    /// <summary>
    /// This piece rewritten so that everything between <paramref name="from"/> and <paramref name="to"/>
    /// is shown rather than read, or null where the stretch does not reach it.
    /// </summary>
    private static TexNode? Show(TexNode node, int at, int from, int to)
    {
        var end = at + node.Width;
        if (to <= at || from >= end) return null;

        // All of this piece is inside the stretch, so this piece is what gets shown.
        if (from <= at && to >= end) return TexNode.Shown(node.Print(), role: node.Role);

        // Part of it, and nothing underneath to be more precise about: a caret inside a word is still
        // editing the word.
        if (node.IsLeaf) return TexNode.Shown(node.Text, role: node.Role);

        var starts = new int[node.Children.Count];
        var cursor = at;
        for (var i = 0; i < node.Children.Count; i++)
        {
            starts[i] = cursor;
            cursor += node.Children[i].Width;
        }

        int first = -1, last = -1;
        for (var i = 0; i < node.Children.Count; i++)
        {
            // A piece standing for no source cannot be reached by a caret: an expansion is what a macro
            // means, and somebody typing is typing the macro.
            if (node.Children[i].Width == 0) continue;
            if (to <= starts[i] || from >= starts[i] + node.Children[i].Width) continue;

            if (first < 0) first = i;
            last = i;
        }

        if (first < 0) return null;

        var rebuilt = new List<TexNode>(node.Children.Count);
        for (var i = 0; i < first; i++) rebuilt.Add(node.Children[i]);

        if (first == last)
        {
            rebuilt.Add(Show(node.Children[first], starts[first], from, to) ?? node.Children[first]);
        }
        else
        {
            // Several pieces at once, so what replaces them is one run of characters playing none of
            // their parts — a numerator and the brace after it are not a numerator.
            var text = new StringBuilder();
            for (var i = first; i <= last; i++) node.Children[i].PrintTo(text);
            rebuilt.Add(TexNode.Shown(text.ToString(), role: TexRole.Element));
        }

        for (var i = last + 1; i < node.Children.Count; i++) rebuilt.Add(node.Children[i]);

        return node.With(rebuilt);
    }

    // ── Showing what cannot be drawn ─────────────────────────────────────────

    private static TexNode Check(TexNode node, Func<string, bool> draws)
    {
        if (node.IsLeaf) return node;

        // Only the name is ever shown, never the whole command. `\textrm{Hello}` is a word set in the
        // wrong face, which is a great deal closer to right than a blank, and the argument of something
        // nobody has heard of is usually ordinary maths a reader can see and would miss.
        //
        // A command that resolved to something is drawable by definition, whatever its own name means.
        // \begin and \end are the structure of an environment rather than anything drawn in it, so asking
        // whether a typesetter has a drawing for them is the wrong question — and answering it put a red
        // wave under the \end of every correctly set array.
        var name = node.Kind == TexKind.Command
                   && node.Role is not (TexRole.Begin or TexRole.End)
                   && node.Part(TexRole.Expansion) is null
            ? node.Part(TexRole.Name)
            : null;

        var unreadable = name is not null && !draws(name.Text);

        var rebuilt = new List<TexNode>(node.Children.Count);
        var moved = false;

        foreach (var child in node.Children)
        {
            if (unreadable && ReferenceEquals(child, name))
            {
                rebuilt.Add(TexNode.Shown(child.Text, $"there is no {child.Text} to draw", TexRole.Name));
                moved = true;
                continue;
            }

            var seen = Check(child, draws);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        return moved ? node.With(rebuilt) : node;
    }
}
