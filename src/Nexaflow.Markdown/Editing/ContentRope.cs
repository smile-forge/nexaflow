using System;
using System.Collections.Generic;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// The characters of a long document as a tree of short strings: a leaf holds a few thousand characters, and every node above
/// says how many characters it holds and how many lines they break into — so a character, a line and the place an edit writes
/// are each found by walking down one path, never along the document.
///
/// <para>
/// <strong>Immutable, and shared between versions.</strong> An edit makes new nodes only along the paths to where it writes
/// (<see cref="Spliced"/>); every other node is the one the version before held. A version kept to go back to costs the nodes its
/// edit made, and nothing else.
/// </para>
/// <para>
/// <strong>A B-tree.</strong> Every leaf is as deep as every other. A node holds at most <see cref="Most"/> children and a leaf at
/// most <see cref="LeafMost"/> characters, and joining two trees (<see cref="Joined"/>) gathers what is short of
/// <see cref="Fewest"/> children or <see cref="LeafLeast"/> characters into its neighbour — so writing a character at a time
/// leaves leaves of a few thousand characters, not a tree of single letters. Cutting a tree (<see cref="Before"/>,
/// <see cref="From"/>) and joining what is left is all an edit is.
/// </para>
/// <para>
/// <strong>A line break is <c>\n</c>, <c>\r\n</c> or a <c>\r</c> alone</strong>, as markdown and every editor read one. A node
/// counts the breaks in its own characters, a <c>\r</c> at its end counted as one; where a <c>\r</c> ending one node meets a
/// <c>\n</c> starting the next, the node holding both counts the pair once (<see cref="Breaks"/>).
/// </para>
/// </summary>
internal abstract class ContentRope
{
    /// <summary>The most characters a leaf holds.</summary>
    internal const int LeafMost = 4096;

    /// <summary>The fewest characters a leaf is left holding when it can be joined to its neighbour instead.</summary>
    internal const int LeafLeast = 1024;

    /// <summary>The most children a node holds.</summary>
    internal const int Most = 16;

    /// <summary>The fewest children a node is left holding when it can be joined to its neighbour instead.</summary>
    internal const int Fewest = 8;

    private ContentRope(int length, int breaks, int crLfs, int height, char first, char last)
    {
        this.Length = length;
        this.Breaks = breaks;
        this.CrLfs = crLfs;
        this.Height = height;
        this.First = first;
        this.Last = last;
    }

    /// <summary>How many characters it holds; never none.</summary>
    public int Length { get; }

    /// <summary>How many line breaks its characters hold, a <c>\r</c> ending them counted as one.</summary>
    public int Breaks { get; }

    /// <summary>How many of those breaks are <c>\r\n</c>.</summary>
    public int CrLfs { get; }

    /// <summary>How far below it the leaves are: none for a leaf.</summary>
    public int Height { get; }

    /// <summary>Its first character.</summary>
    public char First { get; }

    /// <summary>Its last character.</summary>
    public char Last { get; }

    /// <summary>Whether it holds enough to stand beside a neighbour as it is, rather than being joined to it.</summary>
    protected abstract bool Filled { get; }

    /// <summary><paramref name="text"/> as a tree, its leaves and its nodes as evenly filled as they go — or null for nothing.</summary>
    public static ContentRope? Of(string text)
    {
        if (text.Length == 0) return null;

        var count = (text.Length + LeafMost - 1) / LeafMost;
        var level = new ContentRope[count];

        for (var (at, from) = (0, 0); at < count; at++)
        {
            var length = (text.Length - from) / (count - at);
            level[at] = new Leaf(text.Substring(from, length));
            from += length;
        }

        while (level.Length > 1)
        {
            var parents = new ContentRope[(level.Length + Most - 1) / Most];

            for (var (at, from) = (0, 0); at < parents.Length; at++)
            {
                var length = (level.Length - from) / (parents.Length - at);
                parents[at] = new Branch(level[from..(from + length)]);
                from += length;
            }

            level = parents;
        }

        return level[0];
    }

    /// <summary>
    /// <paramref name="rope"/> with its characters from <paramref name="start"/> to <paramref name="end"/> replaced by
    /// <paramref name="text"/> — or null where nothing is left.
    /// </summary>
    public static ContentRope? Spliced(ContentRope rope, int start, int end, string text) =>
        Joined(Joined(Before(rope, start), Of(text)), From(rope, end));

    /// <summary>The character at <paramref name="offset"/>.</summary>
    public char this[int offset]
    {
        get
        {
            var node = this;

            while (node is Branch branch)
                foreach (var child in branch.Children)
                {
                    if (offset < child.Length) { node = child; break; }
                    offset -= child.Length;
                }

            return ((Leaf)node).Text[offset];
        }
    }

    /// <summary>Its characters from <paramref name="start"/> on, as many as <paramref name="into"/> holds.</summary>
    public void CopyTo(int start, Span<char> into)
    {
        if (into.IsEmpty) return;

        if (this is Leaf leaf)
        {
            leaf.Text.AsSpan(start, into.Length).CopyTo(into);
            return;
        }

        foreach (var child in ((Branch)this).Children)
        {
            if (start >= child.Length) { start -= child.Length; continue; }

            var taken = Math.Min(child.Length - start, into.Length);
            child.CopyTo(start, into[..taken]);

            into = into[taken..];
            if (into.IsEmpty) return;
            start = 0;
        }
    }

    /// <summary>Its characters from <paramref name="start"/> to <paramref name="end"/>, as the stretches of its leaves they fall in, in order.</summary>
    public IEnumerable<ReadOnlyMemory<char>> Chunks(int start, int end)
    {
        var waiting = new Stack<(ContentRope Node, int From)>();
        waiting.Push((this, 0));

        while (waiting.Count > 0)
        {
            var (node, from) = waiting.Pop();
            if (from >= end || from + node.Length <= start) continue;

            if (node is Leaf leaf)
            {
                var (low, high) = (Math.Max(start - from, 0), Math.Min(end - from, leaf.Length));
                yield return leaf.Text.AsMemory(low, high - low);
                continue;
            }

            var children = ((Branch)node).Children;
            var at = from + node.Length;

            for (var child = children.Length - 1; child >= 0; child--)
            {
                at -= children[child].Length;
                waiting.Push((children[child], at));
            }
        }
    }

    /// <summary>How many line breaks its first <paramref name="length"/> characters hold, a <c>\r</c> ending them counted as one.</summary>
    public int BreaksIn(int length)
    {
        if (length <= 0) return 0;
        if (length >= this.Length) return this.Breaks;

        if (this is Leaf leaf) return Counted(leaf.Text.AsSpan(0, length)).Breaks;

        var (counted, from) = (0, 0);
        ContentRope? before = null;

        foreach (var child in ((Branch)this).Children)
        {
            var paired = Paired(before, child);

            if (from + child.Length > length)
                return counted + child.BreaksIn(length - from) - (paired && length > from ? 1 : 0);

            counted += child.Breaks - (paired ? 1 : 0);
            from += child.Length;
            before = child;
        }

        return counted;
    }

    /// <summary>
    /// Where the line after its <paramref name="breaks"/>th line break starts, counting from one and a <c>\r</c> ending it as a
    /// break — so a <c>\r</c> it ends on, of a <c>\r\n</c> the characters after it finish, gives its own end.
    /// </summary>
    public int AfterBreak(int breaks)
    {
        if (this is Leaf leaf)
        {
            var text = leaf.Text;

            for (var (at, counted) = (0, 0); at < text.Length; at++)
            {
                if (text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n') continue;
                if ((text[at] is '\n' or '\r') && ++counted == breaks) return at + 1;
            }

            return text.Length;
        }

        var from = 0;
        ContentRope? before = null;

        foreach (var child in ((Branch)this).Children)
        {
            // A \n starting this child finishes the \r the child before ended on, which counted it already.
            var paired = Paired(before, child);
            var own = child.Breaks - (paired ? 1 : 0);

            if (breaks <= own) return from + child.AfterBreak(paired ? breaks + 1 : breaks);

            breaks -= own;
            from += child.Length;
            before = child;
        }

        return this.Length;
    }

    /// <summary>
    /// <paramref name="left"/>'s characters then <paramref name="right"/>'s, as one tree — or null where neither holds any. Whatever
    /// is too short to stand on its own where the two meet is gathered into its neighbour, so the tree stays as shallow as it was.
    /// </summary>
    public static ContentRope? Joined(ContentRope? left, ContentRope? right)
    {
        if (left is null) return right;
        if (right is null) return left;

        if (left.Height < right.Height)
        {
            var children = ((Branch)right).Children;
            if (left.Height == right.Height - 1 && left.Filled) return Merged([left], children);

            var joined = Joined(left, children[0])!;

            return joined.Height == right.Height - 1 ? Merged([joined], children[1..]) : Merged(((Branch)joined).Children, children[1..]);
        }

        if (left.Height > right.Height)
        {
            var children = ((Branch)left).Children;
            if (right.Height == left.Height - 1 && right.Filled) return Merged(children, [right]);

            var joined = Joined(children[^1], right)!;

            return joined.Height == left.Height - 1 ? Merged(children[..^1], [joined]) : Merged(children[..^1], ((Branch)joined).Children);
        }

        if (left.Filled && right.Filled) return new Branch([left, right]);

        if (left is Leaf first && right is Leaf second)
        {
            var text = string.Concat(first.Text, second.Text);
            if (text.Length <= LeafMost) return new Leaf(text);

            var half = text.Length / 2;

            return new Branch([new Leaf(text[..half]), new Leaf(text[half..])]);
        }

        return Merged(((Branch)left).Children, ((Branch)right).Children);
    }

    /// <summary>Its first <paramref name="length"/> characters — or null for none.</summary>
    public static ContentRope? Before(ContentRope rope, int length)
    {
        if (length <= 0) return null;
        if (length >= rope.Length) return rope;
        if (rope is Leaf leaf) return new Leaf(leaf.Text[..length]);

        var children = ((Branch)rope).Children;
        var (whole, from) = (0, 0);

        while (from + children[whole].Length <= length) from += children[whole++].Length;

        return Joined(Gathered(children[..whole]), from == length ? null : Before(children[whole], length - from));
    }

    /// <summary>Its characters from <paramref name="start"/> on — or null for none.</summary>
    public static ContentRope? From(ContentRope rope, int start)
    {
        if (start <= 0) return rope;
        if (start >= rope.Length) return null;
        if (rope is Leaf leaf) return new Leaf(leaf.Text[start..]);

        var children = ((Branch)rope).Children;
        var (cut, from) = (0, 0);

        while (from + children[cut].Length <= start) from += children[cut++].Length;

        var rest = Gathered(children[(cut + 1)..]);

        return Joined(start == from ? children[cut] : From(children[cut], start - from), rest);
    }

    /// <summary>Whether <paramref name="before"/> ends on a <c>\r</c> that <paramref name="after"/> finishes with a <c>\n</c>.</summary>
    private static bool Paired(ContentRope? before, ContentRope after) => before is { Last: '\r' } && after.First == '\n';

    /// <summary>Nodes as deep as each other under one node, or the one itself, or null for none.</summary>
    private static ContentRope? Gathered(ContentRope[] nodes) => nodes.Length switch
    {
        0 => null,
        1 => nodes[0],
        _ => new Branch(nodes),
    };

    /// <summary>Two runs of nodes as deep as each other, under one node where they fit in one, and under two side by side where they do not.</summary>
    private static ContentRope Merged(ContentRope[] left, ContentRope[] right)
    {
        var all = new ContentRope[left.Length + right.Length];
        left.CopyTo(all, 0);
        right.CopyTo(all, left.Length);

        if (all.Length <= Most) return new Branch(all);

        var cut = Math.Min(Most, all.Length - Fewest);

        return new Branch([new Branch(all[..cut]), new Branch(all[cut..])]);
    }

    /// <summary>The line breaks <paramref name="text"/> holds, and how many are <c>\r\n</c> — a <c>\r</c> ending it counted as a break.</summary>
    private static (int Breaks, int CrLfs) Counted(ReadOnlySpan<char> text)
    {
        var (breaks, crLfs) = (0, 0);

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] == '\n') breaks++;
            else if (text[at] == '\r')
            {
                if (at + 1 < text.Length && text[at + 1] == '\n') { crLfs++; continue; }
                breaks++;
            }
        }

        return (breaks, crLfs);
    }

    /// <summary>A few thousand characters of the document.</summary>
    private sealed class Leaf : ContentRope
    {
        public Leaf(string text) : this(text, Counted(text)) { }

        private Leaf(string text, (int Breaks, int CrLfs) counted)
            : base(text.Length, counted.Breaks, counted.CrLfs, 0, text[0], text[^1]) => this.Text = text;

        public string Text { get; }

        protected override bool Filled => this.Text.Length >= LeafLeast;
    }

    /// <summary>The nodes under one, every one as deep as the others.</summary>
    private sealed class Branch : ContentRope
    {
        public Branch(ContentRope[] children) : this(children, Summed(children)) { }

        private Branch(ContentRope[] children, (int Length, int Breaks, int CrLfs) summed)
            : base(summed.Length, summed.Breaks, summed.CrLfs, children[0].Height + 1, children[0].First, children[^1].Last) =>
            this.Children = children;

        public ContentRope[] Children { get; }

        protected override bool Filled => this.Children.Length >= Fewest;

        private static (int Length, int Breaks, int CrLfs) Summed(ContentRope[] children)
        {
            var (length, breaks, crLfs) = (0, 0, 0);

            for (var at = 0; at < children.Length; at++)
            {
                var child = children[at];
                (length, breaks, crLfs) = (length + child.Length, breaks + child.Breaks, crLfs + child.CrLfs);

                // A \r ending one child and a \n starting the next are one break, which the first counted on its own.
                if (at > 0 && Paired(children[at - 1], child)) (breaks, crLfs) = (breaks - 1, crLfs + 1);
            }

            return (length, breaks, crLfs);
        }
    }
}
