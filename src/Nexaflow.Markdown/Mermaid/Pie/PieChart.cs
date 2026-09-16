using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>
/// One slice, read: where it was written, what it is called, what it is worth, and what the config makes of it.
/// </summary>
/// <param name="Part">The whole slice as it was written — what a press on the wedge means.</param>
/// <param name="Label">What it is called, without its quotes.</param>
/// <param name="Value">The number as it was written — empty where none has been yet — which is what typing in the legend changes.</param>
/// <param name="Worth">What that number comes to, or nought where it is not a number greater than nought.</param>
/// <param name="Colour">The colour the config asks for, or null to leave it to the theme.</param>
/// <param name="Swatch">Which of <c>pie1</c>…<c>pie12</c> that colour came from, or null.</param>
/// <param name="Highlighted">Whether the config picks this slice out.</param>
public sealed record PieSlice(
    ContentPart Part,
    ContentPart Label,
    ContentPart? Value,
    double Worth,
    string? Colour,
    string? Swatch,
    bool Highlighted)
{
    /// <summary>What it is called.</summary>
    public string Name => Label.Text;

    /// <summary>What is wrong with it, where anything is — a value that is not a number greater than nought.</summary>
    public string? Trouble => Value?.Trouble;

    /// <summary>Whether it is worth drawing a wedge for.</summary>
    public bool Drawn => Worth > 0;

    /// <summary>The hole standing where the label is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? LabelHole { get; init; }

    /// <summary>The hole standing where the value is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? ValueHole { get; init; }
}

/// <summary>
/// A <c>pie</c> block, read: its title, whether it shows its values, its slices in the order they are written, and
/// what its front matter asks for.
///
/// <para>
/// What <see cref="Chemistry.Molecule"/> is to a SMILES string — the tree read back into the thing it describes, with
/// every part kept, so what the builder draws can point at what the reader wrote.
/// </para>
/// </summary>
public sealed class PieChart
{
    private PieChart(MermaidBlock block, PieConfig config, IReadOnlyList<PieSlice> slices,
                     ContentPart? title, bool showsData)
    {
        Block = block;
        Config = config;
        Slices = slices;
        Title = title;
        ShowsData = showsData;
    }

    /// <summary>Reads a block: parsed, then worked over by <see cref="PiePipeline"/>.</summary>
    public static PieChart Read(string? block) => Of(PiePipeline.Read(block));

    /// <summary>Reads a tree the pipeline has already been over.</summary>
    public static PieChart Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the pie's own stages.</summary>
    public static PieChart Of(MermaidBlock block)
    {

        var slices = new List<PieSlice>();
        ContentPart? title = null;
        var showsData = false;

        foreach (var part in block.Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case PieKinds.ShowData:
                    showsData = true;
                    break;

                case PieKinds.Title when title is null:
                    title = part.Part(PieRoles.Label);
                    break;

                case PieKinds.Slice when Slice(part) is { } slice:
                    slices.Add(slice);
                    break;
            }
        }

        return new PieChart(block, PieConfig.Read(block.Config), slices, title ?? block.Title, showsData);
    }

    /// <summary>The block this was read from — its front matter, its header, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public PieConfig Config { get; }

    /// <summary>The slices, in the order they were written, which is the order they are drawn clockwise.</summary>
    public IReadOnlyList<PieSlice> Slices { get; }

    /// <summary>
    /// The title as it was written — the chart's own where it has one, and the front matter's otherwise — or null.
    /// </summary>
    public ContentPart? Title { get; }

    /// <summary>What the title says, without the quotes a front-matter one may carry.</summary>
    public string? TitleText => Title is null ? null : Title == Block.Title ? Block.TitleText : Title.Text;

    /// <summary>Whether each slice's value is shown in the legend beside its share.</summary>
    public bool ShowsData { get; }

    /// <summary>What every slice worth drawing comes to, together.</summary>
    public double Total => Slices.Where(slice => slice.Drawn).Sum(slice => slice.Worth);

    /// <summary>What share of the whole a slice is, or nought where there is nothing to share.</summary>
    public double Share(PieSlice slice) => Total > 0 && slice.Drawn ? slice.Worth / Total : 0;

    private static PieSlice? Slice(ContentPart part)
    {
        if (part.SelfAndDescendants().FirstOrDefault(node => node.Kind == PieKinds.Name) is not { } label) return null;

        var value = part.SelfAndDescendants().FirstOrDefault(node => node.Kind == PieKinds.Value);
        var worth = value is { Trouble: null, Length: > 0 }
                    && double.TryParse(value.Text, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out var read)
            ? read
            : 0;

        return new PieSlice(part, label, value, worth,
                            Said(part, PieRoles.Colour), Said(part, PieRoles.Swatch), Said(part, PieRoles.Highlighted) is not null)
        {
            LabelHole = Hole(label),
            ValueHole = Hole(value),
        };
    }

    /// <summary>The hole a stage put beside a part with nothing written in it, or null where there is none.</summary>
    private static ContentPart? Hole(ContentPart? part) =>
        part?.Parent?.Children.FirstOrDefault(child => child.Kind == Kinds.Hole);

    /// <summary>What a stage worked out about a piece and hung underneath it — see <see cref="AstRewrite.Fact"/>.</summary>
    private static string? Said(ContentPart part, string role)
    {
        foreach (var child in part.Children)
        {
            if (!child.Derived) continue;

            foreach (var inner in child.Children)
                if (inner.Role == role) return inner.Text;
        }

        return null;
    }
}
