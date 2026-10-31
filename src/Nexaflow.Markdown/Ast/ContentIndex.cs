namespace Nexaflow.Markdown.Ast;

/// <summary>Something that stands for a number of characters, laid end to end with others like it in a <see cref="ContentIndex{T}"/>.</summary>
public interface IContentWidth
{
    /// <summary>How many characters it stands for.</summary>
    int Width { get; }

    /// <summary>
    /// How many characters it spreads over in what holds it: its <see cref="Width"/>, where nothing stands between its characters —
    /// more, where something else's characters do, as a container's marks stand between the lines of a fence written in a quote.
    /// </summary>
    int Spread => this.Width;
}

/// <summary>
/// Things standing for characters, in the order they stand, indexed by how wide each is: where one starts, which one holds a
/// place, and a run of them replaced by others, each found by walking down one path rather than along the list.
///
/// <para>
/// <strong>Nobody's start is written down.</strong> Every node of the tree says how many things it holds and how many characters
/// they stand for, so where a thing starts is the widths before it, added up on the way down. Something written before it moves it
/// along without anything being rewritten — which is what lets a reading's units keep their offsets their own.
/// </para>
/// <para>
/// <strong>Immutable, and shared between versions</strong>, as a document's rope is: replacing a run makes new nodes only along the
/// paths to it, so the index before an edit and the one after share everything the edit did not touch.
/// </para>
/// </summary>
public sealed class ContentIndex<T> where T : IContentWidth
{
    /// <summary>The most things a leaf holds.</summary>
    private const int LeafMost = 32;

    /// <summary>The fewest things a leaf is left holding when it can be joined to its neighbour instead.</summary>
    private const int LeafLeast = 16;

    /// <summary>The most children a node holds.</summary>
    private const int Most = 16;

    /// <summary>The fewest children a node is left holding when it can be joined to its neighbour instead.</summary>
    private const int Fewest = 8;

    private readonly Node? _root;

    private ContentIndex(Node? root) => _root = root;

    /// <summary>Nothing.</summary>
    public static ContentIndex<T> Empty { get; } = new(null);

    /// <summary><paramref name="items"/>, in order.</summary>
    public static ContentIndex<T> Of(IReadOnlyList<T> items) => new(Built(items));

    /// <summary>How many things it holds.</summary>
    public int Count => _root?.Count ?? 0;

    /// <summary>How many characters they stand for, together.</summary>
    public int Width => _root?.Width ?? 0;

    /// <summary>How many characters they spread over, together (<see cref="IContentWidth.Spread"/>).</summary>
    public int Spread => _root?.Spread ?? 0;

    /// <summary>The thing at <paramref name="index"/>, where it starts, and where it starts spread over what holds it.</summary>
    public (T Item, int Start, int Spread) Placed(int index)
    {
        if (index < 0 || index >= this.Count) throw new ArgumentOutOfRangeException(nameof(index), $"{index} is not among the {this.Count} held");

        var (node, start, spread) = (_root!, 0, 0);

        while (node is Branch branch)
            foreach (var child in branch.Children)
            {
                if (index < child.Count) { node = child; break; }

                index -= child.Count;
                start += child.Width;
                spread += child.Spread;
            }

        var items = ((Leaf)node).Items;

        for (var before = 0; before < index; before++)
        {
            start += items[before].Width;
            spread += items[before].Spread;
        }

        return (items[index], start, spread);
    }

    /// <summary>
    /// Which thing's spread holds <paramref name="offset"/> (<see cref="IContentWidth.Spread"/>): the first whose spread ends past
    /// it — the last, for an offset at or past the end of them all — or -1 where there is nothing.
    /// </summary>
    public int IndexAtSpread(int offset)
    {
        if (_root is null) return -1;
        if (offset >= _root.Spread) return _root.Count - 1;

        var (node, index) = (_root, 0);

        while (node is Branch branch)
            foreach (var child in branch.Children)
            {
                if (offset < child.Spread) { node = child; break; }

                offset -= child.Spread;
                index += child.Count;
            }

        foreach (var item in ((Leaf)node).Items)
        {
            if (offset < item.Spread) return index;

            offset -= item.Spread;
            index++;
        }

        return index - 1;
    }

    /// <summary>The thing at <paramref name="index"/>, and where it starts.</summary>
    public (T Item, int Start) this[int index]
    {
        get
        {
            if (index < 0 || index >= this.Count) throw new ArgumentOutOfRangeException(nameof(index), $"{index} is not among the {this.Count} held");

            var (node, start) = (_root!, 0);

            while (node is Branch branch)
                foreach (var child in branch.Children)
                {
                    if (index < child.Count) { node = child; break; }

                    index -= child.Count;
                    start += child.Width;
                }

            var items = ((Leaf)node).Items;
            for (var before = 0; before < index; before++) start += items[before].Width;

            return (items[index], start);
        }
    }

    /// <summary>
    /// Which thing holds <paramref name="offset"/>: the first that ends past it — the last, for an offset at or past the end of them
    /// all — or -1 where there is nothing.
    /// </summary>
    public int IndexAt(int offset)
    {
        if (_root is null) return -1;
        if (offset >= _root.Width) return _root.Count - 1;

        var (node, index) = (_root, 0);

        while (node is Branch branch)
            foreach (var child in branch.Children)
            {
                if (offset < child.Width) { node = child; break; }

                offset -= child.Width;
                index += child.Count;
            }

        foreach (var item in ((Leaf)node).Items)
        {
            if (offset < item.Width) return index;

            offset -= item.Width;
            index++;
        }

        return index - 1;
    }

    /// <summary>The same, with <paramref name="item"/> after the last.</summary>
    public ContentIndex<T> Added(T item) => new(Joined(_root, new Leaf([item])));

    /// <summary>The same, with the things from <paramref name="from"/> up to <paramref name="to"/> replaced by <paramref name="items"/>.</summary>
    public ContentIndex<T> Replaced(int from, int to, IReadOnlyList<T> items)
    {
        if (from < 0 || to < from || to > this.Count)
            throw new ArgumentOutOfRangeException(nameof(from), $"{from}..{to} is not a run of the {this.Count} held");

        return new(Joined(Joined(Before(_root, from), Built(items)), After(_root, to)));
    }

    /// <summary>The things from <paramref name="index"/> on, in order, each with where it starts.</summary>
    public IEnumerable<(T Item, int Start)> From(int index)
    {
        if (_root is null || index >= _root.Count) yield break;

        var (start, skip) = (0, Math.Max(index, 0));
        var waiting = new Stack<Node>();
        waiting.Push(_root);

        while (waiting.Count > 0)
        {
            var node = waiting.Pop();

            if (skip >= node.Count)
            {
                skip -= node.Count;
                start += node.Width;
                continue;
            }

            if (node is Branch branch)
            {
                for (var child = branch.Children.Length - 1; child >= 0; child--) waiting.Push(branch.Children[child]);
                continue;
            }

            foreach (var item in ((Leaf)node).Items)
            {
                if (skip > 0) { skip--; start += item.Width; continue; }

                yield return (item, start);
                start += item.Width;
            }
        }
    }

    // ── The tree ────────────────────────────────────────────────────────────

    private abstract class Node(int count, int width, int spread, int height)
    {
        public int Count { get; } = count;

        public int Width { get; } = width;

        public int Spread { get; } = spread;

        public int Height { get; } = height;

        public abstract bool Filled { get; }
    }

    private sealed class Leaf(T[] items) : Node(items.Length, Summed(items, false), Summed(items, true), 0)
    {
        public T[] Items { get; } = items;

        public override bool Filled => this.Items.Length >= LeafLeast;

        private static int Summed(T[] items, bool spread)
        {
            var width = 0;
            foreach (var item in items) width += spread ? item.Spread : item.Width;

            return width;
        }
    }

    private sealed class Branch(Node[] children)
        : Node(children.Sum(child => child.Count), children.Sum(child => child.Width), children.Sum(child => child.Spread), children[0].Height + 1)
    {
        public Node[] Children { get; } = children;

        public override bool Filled => this.Children.Length >= Fewest;
    }

    private static Node? Built(IReadOnlyList<T> items)
    {
        if (items.Count == 0) return null;

        var count = (items.Count + LeafMost - 1) / LeafMost;
        var level = new Node[count];

        for (var (at, from) = (0, 0); at < count; at++)
        {
            var length = (items.Count - from) / (count - at);
            level[at] = new Leaf([.. items.Skip(from).Take(length)]);
            from += length;
        }

        while (level.Length > 1)
        {
            var parents = new Node[(level.Length + Most - 1) / Most];

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

    /// <summary>The things of <paramref name="left"/> then those of <paramref name="right"/>, as one tree as shallow as the deeper of the two.</summary>
    private static Node? Joined(Node? left, Node? right)
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
            T[] items = [.. first.Items, .. second.Items];
            if (items.Length <= LeafMost) return new Leaf(items);

            var half = items.Length / 2;

            return new Branch([new Leaf(items[..half]), new Leaf(items[half..])]);
        }

        return Merged(((Branch)left).Children, ((Branch)right).Children);
    }

    /// <summary>The first <paramref name="count"/> things of <paramref name="node"/>, or null for none.</summary>
    private static Node? Before(Node? node, int count)
    {
        if (node is null || count <= 0) return null;
        if (count >= node.Count) return node;
        if (node is Leaf leaf) return new Leaf(leaf.Items[..count]);

        var children = ((Branch)node).Children;
        var (whole, taken) = (0, 0);

        while (taken + children[whole].Count <= count) taken += children[whole++].Count;

        return Joined(Gathered(children[..whole]), taken == count ? null : Before(children[whole], count - taken));
    }

    /// <summary>The things of <paramref name="node"/> from the <paramref name="count"/>th on, or null for none.</summary>
    private static Node? After(Node? node, int count)
    {
        if (node is null || count >= node.Count) return null;
        if (count <= 0) return node;
        if (node is Leaf leaf) return new Leaf(leaf.Items[count..]);

        var children = ((Branch)node).Children;
        var (cut, taken) = (0, 0);

        while (taken + children[cut].Count <= count) taken += children[cut++].Count;

        return Joined(taken == count ? children[cut] : After(children[cut], count - taken), Gathered(children[(cut + 1)..]));
    }

    private static Node? Gathered(Node[] nodes) => nodes.Length switch
    {
        0 => null,
        1 => nodes[0],
        _ => new Branch(nodes),
    };

    private static Node Merged(Node[] left, Node[] right)
    {
        Node[] all = [.. left, .. right];
        if (all.Length <= Most) return new Branch(all);

        var cut = Math.Min(Most, all.Length - Fewest);

        return new Branch([new Branch(all[..cut]), new Branch(all[cut..])]);
    }
}
