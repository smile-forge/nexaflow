using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Venn.Stages;

namespace Nexaflow.Markdown.Mermaid.Venn;

/// <summary>One item written inside a set or a union: a <c>text</c> line.</summary>
public sealed class VennItem
{
    internal VennItem(string id, ContentPart part, ContentPart name)
    {
        Id = id;
        Part = part;
        Name = name;
    }

    /// <summary>What it is called — which is what a style names it by.</summary>
    public string Id { get; }

    /// <summary>The <c>text</c> line it was written on — what a press on it means.</summary>
    public ContentPart Part { get; }

    /// <summary>Its name as written, without its quotes.</summary>
    public ContentPart Name { get; }

    /// <summary>The hole standing where its name is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? NameHole { get; internal init; }

    /// <summary>What its label says, without its brackets or quotes, or null where it has none.</summary>
    public ContentPart? Label { get; internal init; }

    /// <summary>The hole standing where its label is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? LabelHole { get; internal init; }

    public MermaidStyle Style { get; internal set; } = MermaidStyle.None;
}

/// <summary>A region of a Venn diagram: a set, or the overlap a union names.</summary>
public abstract class VennRegion
{
    private protected VennRegion(string key, ContentPart part, double weight)
    {
        Key = key;
        Part = part;
        Weight = weight;
    }

    /// <summary>What it is known by — see <see cref="VennRoles.Key"/>.</summary>
    public string Key { get; }

    /// <summary>The region that first wrote it — its line, and the items indented under it — which is what a press on it means.</summary>
    public ContentPart Part { get; }

    /// <summary>What its label says, without its brackets or quotes, or null where none is written. The last one written wins.</summary>
    public ContentPart? Label { get; internal set; }

    /// <summary>The hole standing where its label is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? LabelHole { get; internal set; }

    /// <summary>Its size as written, or null where none is.</summary>
    public ContentPart? Size { get; internal set; }

    /// <summary>How much of the drawing it takes: the size written, or Mermaid's where none is or it is no size.</summary>
    public double Weight { get; internal set; }

    public MermaidStyle Style { get; internal set; } = MermaidStyle.None;

    /// <summary>The items written in it, in order.</summary>
    public IReadOnlyList<VennItem> Items => Held;

    internal List<VennItem> Held { get; } = [];
}

/// <summary>One set: a circle.</summary>
public sealed class VennSet : VennRegion
{
    /// <summary>What a set is worth where no size is written — Mermaid's.</summary>
    public const double Unsized = 10;

    internal VennSet(string id, ContentPart part, ContentPart name, int order) : base(id, part, Unsized)
    {
        Name = name;
        Order = order;
    }

    /// <summary>What it is called.</summary>
    public string Id => Key;

    /// <summary>Its name as written, without its quotes.</summary>
    public ContentPart Name { get; }

    /// <summary>The hole standing where its name is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? NameHole { get; internal init; }

    /// <summary>Where it comes among the sets, which is which colour of the palette it takes.</summary>
    public int Order { get; }

    /// <summary>The colour the config writes for its place in the order, or null to leave it to the theme.</summary>
    public string? Colour { get; internal init; }

    /// <summary>Which of <c>venn1</c>…<c>venn8</c> that colour came from, or null.</summary>
    public string? Swatch { get; internal init; }
}

/// <summary>A union: where two sets or more overlap.</summary>
public sealed class VennUnion : VennRegion
{
    internal VennUnion(string key, ContentPart part, IReadOnlyList<string> sets) : base(key, part, Unsized(sets.Count)) =>
        Sets = sets;

    /// <summary>What an overlap of this many sets is worth where no size is written — Mermaid's, ten shared out by the square of the count.</summary>
    public static double Unsized(int sets) => VennSet.Unsized / Math.Max(1, sets * sets);

    /// <summary>The names of the sets it is the overlap of, sorted.</summary>
    public IReadOnlyList<string> Sets { get; }
}

/// <summary>
/// A <c>venn-beta</c> block, read: its sets in the order written, the unions where they overlap, the items written in each,
/// their styles, and what its front matter asks for. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// What <see cref="Pie.PieChart"/> is to a pie — the tree read back into the thing it describes, every part kept, so what
/// the builder draws can point at what the reader wrote. A set written twice is one set, its later label and size
/// winning; a union naming a set not written above it, or fewer than two, is no overlap and is not drawn, the reason
/// already on its line.
/// </para>
/// </summary>
public sealed class VennDiagram
{
    private VennDiagram(MermaidBlock block, VennConfig config, IReadOnlyList<VennSet> sets, IReadOnlyList<VennUnion> unions)
    {
        Block = block;
        Config = config;
        Sets = sets;
        Unions = unions;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static VennDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the Venn diagram's own stages.</summary>
    public static VennDiagram Of(MermaidBlock block)
    {
        var config = VennConfig.Read(block.Config);
        var sets = new List<VennSet>();
        var unions = new List<VennUnion>();
        var loose = new List<ContentPart>();
        var styles = new List<ContentPart>();

        foreach (var part in block.Reading.Root.Children)
        {
            if (part.Kind == VennKinds.Region)
            {
                var region = part.Children[0].Stated() switch
                {
                    { Kind: VennKinds.Set } set => Declared(part, set),
                    { Kind: VennKinds.Union } union => Overlapped(part, union),
                    _ => null,
                };

                foreach (var line in part.Children.Skip(1))
                    if (region is not null && line.Stated() is { Kind: VennKinds.Text } item && Item(item) is { } read)
                        region.Held.Add(read);

                continue;
            }

            switch (part.Stated())
            {
                // An item on its own may name a region written after it, and a style anything written anywhere: both are
                // read once every region is known.
                case { Kind: VennKinds.Text } written:
                    loose.Add(written);
                    break;

                case { Kind: VennKinds.Style } written:
                    styles.Add(written);
                    break;
            }
        }

        foreach (var item in loose)
        {
            if (item.Fact(VennRoles.Key) is not { } key || Item(item) is not { } read) continue;

            VennRegion? region = sets.FirstOrDefault(set => set.Key == key);
            region ??= unions.FirstOrDefault(union => union.Key == key);
            region?.Held.Add(read);
        }

        foreach (var style in styles) Styled(style);

        return new VennDiagram(block, config, sets, unions);

        VennRegion? Declared(ContentPart region, ContentPart line)
        {
            if (Id(line) is not { } name) return null;

            var id = region.Fact(VennRoles.Key) ?? string.Empty;
            var set = sets.FirstOrDefault(set => set.Id == id);
            if (set is null)
            {
                var number = (sets.Count % VennConfig.PaletteSize) + 1;
                sets.Add(set = new VennSet(id, region, name, sets.Count)
                {
                    NameHole = name.Parent.Hole(),
                    Colour = config.Swatches.GetValueOrDefault(number),
                    Swatch = config.Swatches.ContainsKey(number) ? $"venn{number}" : null,
                });
            }

            Written(set, line);
            return set;
        }

        VennRegion? Overlapped(ContentPart region, ContentPart line)
        {
            var key = region.Fact(VennRoles.Key) ?? string.Empty;
            var names = key.Split(',', StringSplitOptions.RemoveEmptyEntries);

            // Only sets written above it: that is what a union is the overlap of.
            if (names.Length < 2 || names.Any(name => sets.All(set => set.Id != name))) return null;

            var union = unions.FirstOrDefault(union => union.Key == key);
            if (union is null) unions.Add(union = new VennUnion(key, region, names));

            Written(union, line);
            return union;
        }

        void Styled(ContentPart line)
        {
            if (line.Fact(VennRoles.Key) is not { Length: > 0 } key) return;

            var properties = line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Properties);

            if (key.Contains(','))
            {
                if (unions.FirstOrDefault(union => union.Key == key) is { } union) union.Style = union.Style.With(properties);
                return;
            }

            if (sets.FirstOrDefault(set => set.Id == key) is { } set) set.Style = set.Style.With(properties);

            foreach (var item in sets.Concat<VennRegion>(unions).SelectMany(region => region.Items).Where(item => item.Id == key))
                item.Style = item.Style.With(properties);
        }
    }

    /// <summary>An item, from its <c>text</c> line — or null for one with no name to go by.</summary>
    private static VennItem? Item(ContentPart line)
    {
        if (Id(line) is not { } name) return null;

        var label = line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);
        return new VennItem(name.Text, line, name)
        {
            NameHole = name.Parent.Hole(),
            Label = label.Words(),
            LabelHole = label.Hole(),
        };
    }

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public VennConfig Config { get; }

    /// <summary>The sets, in the order they were first written, which is the order their colours are taken in.</summary>
    public IReadOnlyList<VennSet> Sets { get; }

    /// <summary>The unions that overlap two sets or more written above them, in the order written.</summary>
    public IReadOnlyList<VennUnion> Unions { get; }

    /// <summary>
    /// How much two sets overlap: the size of the union written for the pair of them — or, where they are only two of the
    /// sets a larger union overlaps, a quarter of the smaller, which is what gives that union a region to sit in, as
    /// Mermaid does — and nought where nothing says they overlap at all.
    /// </summary>
    public double Overlap(VennSet one, VennSet other)
    {
        var key = ResolveRegions.Key([one.Id, other.Id]);
        if (Unions.FirstOrDefault(union => union.Key == key) is { } written) return written.Weight;

        return Unions.Any(union => union.Sets.Contains(one.Id) && union.Sets.Contains(other.Id))
            ? Math.Min(one.Weight, other.Weight) / 4
            : 0;
    }

    /// <summary>A set's or a union's label and size, where the line writes them: a later line's win.</summary>
    private static void Written(VennRegion region, ContentPart line)
    {
        if (line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label) is { } label)
        {
            region.Label = label.Words();
            region.LabelHole = label.Hole();
        }

        if (line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Amount).Inner(MermaidKinds.Number) is { } size)
        {
            region.Size = size;
            if (size.Number() is { } read) region.Weight = read;
        }
    }

    /// <summary>The name a line writes directly on it — a set's, or an item's past the region it names — without its quotes.</summary>
    private static ContentPart? Id(ContentPart line) =>
        line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words();
}
