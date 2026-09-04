using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Maths.Latex;

/// <summary>
/// Changing a formula by changing its tree.
///
/// <para>
/// Each of these takes a part of a reading and gives back a whole new root. The tree is immutable, so
/// what comes back shares every subtree the edit did not touch — only the spine from the root down to
/// the change is rebuilt, which is why moving one row of a matrix need not reformat the cells beside it.
/// </para>
/// <para>
/// <b>What comes back is provisional.</b> It prints, and printing it is the point: the source it prints
/// as is the document, and reading <em>that</em> is what produces a tree fit to build from. The stages
/// between the parser and the builder do not re-derive themselves when a tree is changed underneath
/// them — a filled hole is still marked a hole, a command whose name has just grown is still marked
/// undrawable, a gathered shape may no longer be the shape gathering would make of it, and a stretch
/// marked as being typed is pinned to a caret that has moved. So nothing is ever typeset from one of
/// these: print it, read the source back, and build from what that gives.
/// </para>
/// <para>
/// Which is also why nothing here consults the source or produces it. An edit expressed against the
/// tree knows what it touched; an edit expressed against the characters knows only that they changed,
/// and cannot afterwards say which unterminated brace is the new one.
/// </para>
/// </summary>
public static class TexEdit
{
    /// <summary>The whole tree, with <paramref name="replacement"/> where <paramref name="at"/> stood.</summary>
    public static TexNode Replace(TexPart at, TexNode replacement) => Swap(at, replacement);

    /// <summary>
    /// The whole tree, without <paramref name="part"/> — taken out of whatever held it.
    /// <para>
    /// Removing the whole formula leaves an empty sequence rather than nothing at all, because a formula
    /// somebody has emptied is still a formula they are in the middle of writing.
    /// </para>
    /// </summary>
    public static TexNode Remove(TexPart part)
    {
        if (part.Parent is not { } parent) return TexNode.Branch(TexKind.Sequence, []);

        var index = Index(parent, part);
        var children = parent.Node.Children.Where((_, i) => i != index).ToArray();

        return Swap(parent, parent.Node.With(children));
    }

    /// <summary>
    /// The whole tree, with <paramref name="node"/> among <paramref name="into"/>'s parts, at
    /// <paramref name="at"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="into"/> stands for characters rather than for parts. A piece cannot hold both:
    /// printing takes the parts of anything that has them and ignores its text, so putting a part inside
    /// a leaf would quietly drop what the leaf said.
    /// </exception>
    public static TexNode Insert(TexPart into, int at, TexNode node)
    {
        if (into.Node.IsLeaf && into.Node.Text.Length > 0)
            throw new ArgumentException(
                $"{into.Node.Kind} \"{into.Node.Text}\" stands for characters, so it holds no parts", nameof(into));

        var children = into.Node.Children.ToList();
        children.Insert(Math.Clamp(at, 0, children.Count), node);

        return Swap(into, into.Node.With(children));
    }

    /// <summary>
    /// The root of the tree <paramref name="at"/> belongs to, rebuilt so that it holds
    /// <paramref name="replacement"/> instead.
    /// </summary>
    private static TexNode Swap(TexPart at, TexNode replacement)
    {
        var node = replacement;

        for (var part = at; part.Parent is { } parent; part = parent)
        {
            var children = parent.Node.Children.ToArray();
            children[Index(parent, part)] = node;
            node = parent.Node.With(children);
        }

        return node;
    }

    /// <summary>Where <paramref name="child"/> sits among the parts of <paramref name="parent"/>.</summary>
    private static int Index(TexPart parent, TexPart child)
    {
        for (var i = 0; i < parent.Children.Count; i++)
            if (ReferenceEquals(parent.Children[i], child)) return i;

        throw new ArgumentException("that part is not one of this part's own", nameof(child));
    }

    /// <summary>
    /// The tree with <paramref name="text"/> written at <paramref name="caret"/>, shaped so that reading
    /// the result back puts it where it was meant to go — and where in the result it landed.
    ///
    /// <para>
    /// Two things have to be said in the tree that would otherwise be said by peeking at characters. An
    /// argument holding one token holds exactly one, so adding to it means bracing it — <c>x^2</c> gaining
    /// a 3 reads as x squared beside a 3, not as x to the twenty-third, unless the braces go in. And a
    /// control word runs on until something that is not a letter ends its name, so a letter written against
    /// one needs a space, or <c>\hbar</c> quietly becomes <c>\hbarz</c>.
    /// </para>
    /// <para>
    /// What is written goes in as <see cref="TexNode.Shown"/>: characters somebody typed, whose meaning is
    /// settled by reading them back rather than guessed at here. That is why this can be so short — it has
    /// to get the characters into the right place, not work out what they are.
    /// </para>
    /// <para>
    /// Nothing here treats a letter written against a finished name as more of that name. It reads as one
    /// token and is not one thing: <c>\hbar</c> is a command that exists and <c>\hbarz</c> is not, and only
    /// a surface that knows the reader is mid-word can tell those apart. That surface has its own way of
    /// saying so — it shows the stretch being typed as the characters it is made of — and until it does,
    /// the safe reading of a complete control word is that it is complete.
    /// </para>
    /// <para>
    /// A caret inside a name rather than beside it writes at the start of that name. Nothing is ever spliced
    /// into the middle of a token, for the same reason.
    /// </para>
    /// </summary>
    public static TexWrite Write(TexReading reading, int caret, string text)
    {
        var where = Math.Clamp(caret, 0, reading.Root.Length);
        if (text.Length == 0) return new TexWrite(reading.Root.Node, where, 0, false);

        TexNode tree, written;
        bool reshaped;

        if (Argument(reading.Root, where) is { } argument)
        {
            var whole = argument.Node.Print();
            var first = where <= argument.Start;

            written = TexNode.Shown(first ? Apart(string.Empty, whole, text) : Apart(whole, string.Empty, text));
            tree = Replace(argument, Braced(argument.Node, written, first));
            reshaped = true;
        }
        else
        {
            var (into, at) = Point(reading.Root, where);
            var lands = Math.Clamp(at < into.Children.Count ? into.Children[at].Start : into.End,
                                   0, reading.Latex.Length);

            written = TexNode.Shown(Apart(reading.Latex[..lands], reading.Latex[lands..], text));
            tree = Insert(into, at, written);
            reshaped = false;
        }

        // Where it landed, read off the tree that holds it rather than counted out — the piece is the one
        // object that knows which of it is the separator and which the writing.
        var place = tree.Placed().First(where => ReferenceEquals(where.Node, written));

        return new TexWrite(tree, place.Start, written.Width, reshaped);
    }

    /// <summary>
    /// The argument <paramref name="caret"/> is writing into, where it is one already holding as much as it
    /// can hold. Null where it is not in one, or is in one with braces of its own.
    /// </summary>
    private static TexPart? Argument(TexPart root, int caret)
    {
        TexPart? found = null;

        foreach (var part in root.SelfAndDescendants())
        {
            if (part.Derived || part.Kind == TexKind.Group || !IsArgument(part.Role)) continue;
            if (caret < part.Start || caret > part.End) continue;
            if (found is null || part.Length < found.Length) found = part;
        }

        return found;
    }

    /// <summary>Roles that name a place content goes, and so are braced when they come to hold more.</summary>
    private static bool IsArgument(string role) =>
        role is TexRole.Superscript or TexRole.Subscript or TexRole.Numerator or TexRole.Denominator
             or TexRole.Degree or TexRole.Radicand or TexRole.Over or TexRole.Under;

    /// <summary><paramref name="argument"/> in braces of its own, with <paramref name="written"/> beside it.</summary>
    private static TexNode Braced(TexNode argument, TexNode written, bool before)
    {
        var open = TexNode.Leaf(TexKind.Token, "{", TexRole.Open);
        var close = TexNode.Leaf(TexKind.Token, "}", TexRole.Close);
        var held = argument.As(TexRole.Element);

        return TexNode.Branch(
            TexKind.Group,
            before ? [open, written, held, close] : [open, held, written, close],
            argument.Role);
    }

    /// <summary>
    /// Where in the tree <paramref name="caret"/> is writing: the piece that will hold what is written, and
    /// where among its parts it goes. Descends only into something with parts, and stops at a piece's edge
    /// rather than stepping inside it, so writing at the start of a group lands before its brace.
    /// </summary>
    private static (TexPart Into, int At) Point(TexPart root, int caret)
    {
        var into = root;

        while (true)
        {
            var at = into.Children.Count;
            TexPart? deeper = null;

            for (var i = 0; i < into.Children.Count; i++)
            {
                var child = into.Children[i];
                if (child.Derived) continue;

                if (caret <= child.Start) { at = i; break; }
                if (caret < child.End) { at = i; deeper = child; break; }
                at = i + 1;
            }

            if (deeper is null || deeper.Children.Count == 0) return (into, at);
            into = deeper;
        }
    }

    /// <summary>
    /// <paramref name="text"/> with a space at either end where the join would otherwise change what the
    /// neighbouring characters say.
    ///
    /// <para>
    /// Asked of the characters rather than of the tree, and deliberately. Whether two things run together
    /// exists nowhere else: <c>\left</c> and the <c>\{</c> after it are in different parts, under different
    /// parents, and are still adjacent on the page. Reading text to decide a shape is not editing text —
    /// nothing here writes a character into the source, and what comes back is a tree.
    /// </para>
    /// </summary>
    /// <param name="before">What is printed immediately before, wherever in the tree it comes from.</param>
    /// <param name="after">What is printed immediately after.</param>
    private static string Apart(string before, string after, string text)
    {
        var lead = EndsWithControlWord(before) && char.IsLetter(text[0]) ? " " : string.Empty;
        var tail = after.Length > 0 && EndsWithControlWord(text) && char.IsLetter(after[0]) ? " " : string.Empty;

        return lead + text + tail;
    }

    /// <summary>Whether these characters end in a command's name, which the next letter would run on.</summary>
    private static bool EndsWithControlWord(string text)
    {
        var i = text.Length;
        while (i > 0 && char.IsLetter(text[i - 1])) i--;
        return i < text.Length && i > 0 && text[i - 1] == '\\';
    }

    /// <summary>
    /// The tree with a table's columns in <paramref name="order"/>, and where the ones that moved landed.
    ///
    /// <para>
    /// A permutation of the cells that are already there. Every cell keeps the node it was — its spacing,
    /// its braces, whatever was written inside it — because nothing about it changed except which of its
    /// neighbours it sits between. Rendering the table instead reformats every cell in it to move one, and
    /// a reader who lined a matrix up by hand loses that for a drag they made somewhere else in it.
    /// </para>
    /// <para>
    /// A cell carries the separator that follows it, so moving cells means moving where the separators are
    /// rather than the cells across them: the last cell of a row has none, and whichever cell becomes last
    /// gives its own up. The one appended is a separator the table already had, so a document that writes
    /// them one way keeps writing them that way.
    /// </para>
    /// </summary>
    public static TexWrite Columns(TexPart environment, IReadOnlyList<int> order, int at, int wide)
    {
        var landed = new HashSet<TexNode>();

        var rows = environment.Node.Children.Select(child =>
            child.Kind != TexKind.Row ? child : Ordered(child, TexKind.Cell, order, at, wide, landed)).ToList();

        return Spanning(Replace(environment, environment.Node.With(rows)), landed);
    }

    /// <summary>
    /// The tree with a table's rows in <paramref name="order"/>, and where the ones that moved landed.
    /// The same permutation a column move is, one level up: a row carries the break that follows it.
    /// </summary>
    public static TexWrite Rows(TexPart environment, IReadOnlyList<int> order, int at, int wide)
    {
        var landed = new HashSet<TexNode>();
        var node = Ordered(environment.Node, TexKind.Row, order, at, wide, landed);

        return Spanning(Replace(environment, node), landed);
    }

    /// <summary>
    /// <paramref name="holder"/> with the parts of <paramref name="kind"/> it holds put into
    /// <paramref name="order"/>, the separators between them redistributed, and everything else where it
    /// was. The ones that end up in <paramref name="wide"/> places from <paramref name="at"/> are recorded
    /// in <paramref name="landed"/>, which is how the caller finds out where they went.
    /// </summary>
    private static TexNode Ordered(TexNode holder, TexKind kind, IReadOnlyList<int> order,
                                   int at, int wide, HashSet<TexNode> landed)
    {
        var split = holder.Children.Where(child => child.Kind == kind).Select(Split).ToList();
        if (split.Count == 0 || order.Any(i => i < 0 || i >= split.Count)) return holder;

        var between = split.Select(part => part.Separator).FirstOrDefault(separator => separator is not null);
        var moved = order.Select(i => split[i].Bare).ToList();

        var built = new List<TexNode>(moved.Count);
        for (var i = 0; i < moved.Count; i++)
        {
            var part = i < moved.Count - 1 && between is not null
                ? moved[i].With([.. moved[i].Children, between])
                : moved[i];

            if (i >= at && i < at + wide) landed.Add(part);
            built.Add(part);
        }

        // Everything that is not one of these — a \begin, an \end, a column spec — stays on the side of the
        // table it was written on, which is what keeps a reordering from turning the table inside out.
        var first = holder.Children.ToList().FindIndex(child => child.Kind == kind);
        var last = holder.Children.ToList().FindLastIndex(child => child.Kind == kind);

        return holder.With([
            .. holder.Children.Take(first),
            .. built,
            .. holder.Children.Skip(last + 1)]);
    }

    /// <summary>The piece without the separator that follows it, and that separator where it had one.</summary>
    private static (TexNode Bare, TexNode? Separator) Split(TexNode part) =>
        part.Children.Count > 0 && part.Children[^1].Role == TexRole.Separator
            ? (part.With([.. part.Children.Take(part.Children.Count - 1)]), part.Children[^1])
            : (part, null);

    /// <summary>Where in what <paramref name="tree"/> prints as the pieces in <paramref name="landed"/> ended up.</summary>
    private static TexWrite Spanning(TexNode tree, HashSet<TexNode> landed)
    {
        int start = int.MaxValue, end = 0;

        foreach (var place in tree.Placed())
        {
            if (!landed.Contains(place.Node)) continue;
            start = Math.Min(start, place.Start);
            end = Math.Max(end, place.End);
        }

        return start > end ? new TexWrite(tree, 0, 0, true) : new TexWrite(tree, start, end - start, true);
    }
}
