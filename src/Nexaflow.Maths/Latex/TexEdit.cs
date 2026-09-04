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
}
