using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// A plot block as its stages leave it: what its settings say (<see cref="Stages.ResolveSettings"/>), what shape its table is
/// (<see cref="Stages.ResolveShape"/>), the coefficient between each pair of its columns where it asks for them
/// (<see cref="Stages.ResolveCorrelations"/>), and the statistics it reports (<see cref="Stages.ResolveStatistics"/>). A block
/// whose settings will not read has none of this, and is left as written with the setting at fault marked. It prints as the
/// block written.
/// </summary>
internal sealed class PlotBlockNode : ContentNode
{
    private static readonly IReadOnlyDictionary<string, PlotCorrelation> NoStatistics = new Dictionary<string, PlotCorrelation>();

    internal PlotBlockNode(ContentNode written, PlotSettings settings, bool matrix = false,
                           IReadOnlyList<(string Across, string Down, double R)>? correlations = null,
                           PlotCorrelation? statistic = null, IReadOnlyDictionary<string, PlotCorrelation>? statistics = null)
        : base(written)
    {
        this.Settings = settings;
        this.Matrix = matrix;
        this.Correlations = correlations ?? [];
        this.Statistic = statistic;
        this.Statistics = statistics ?? NoStatistics;
    }

    public PlotSettings Settings { get; }

    /// <summary>
    /// Whether the table is read down its side as well as across it: a header with every row one cell wider than it, the extra
    /// leading cell naming the row. Down a matrix the columns are the values, so neither axis has a name of its own.
    /// </summary>
    public bool Matrix { get; }

    /// <summary>The coefficient between each pair of numeric columns — empty unless the block asked for <c>geom: corr</c>.</summary>
    public IReadOnlyList<(string Across, string Down, double R)> Correlations { get; }

    /// <summary>What the <c>stats:</c> line reports over every row — null where it asks for nothing, or too few rows have both.</summary>
    public PlotCorrelation? Statistic { get; }

    /// <summary>The same, over only the rows of each value the facet column takes, by that value.</summary>
    public IReadOnlyDictionary<string, PlotCorrelation> Statistics { get; }

    /// <summary>The same block, of the shape its table was worked out to be.</summary>
    internal PlotBlockNode Shaped(bool matrix) =>
        new(this, this.Settings, matrix, this.Correlations, this.Statistic, this.Statistics);

    /// <summary>The same block, with the coefficient between each pair of its columns.</summary>
    internal PlotBlockNode Correlated(IReadOnlyList<(string Across, string Down, double R)> correlations) =>
        new(this, this.Settings, this.Matrix, correlations, this.Statistic, this.Statistics);

    /// <summary>The same block, with what its <c>stats:</c> line reports.</summary>
    internal PlotBlockNode Reporting(PlotCorrelation? statistic, IReadOnlyDictionary<string, PlotCorrelation> statistics) =>
        new(this, this.Settings, this.Matrix, this.Correlations, statistic, statistics);

    protected override ContentNode Reshaped(ContentNode shape) =>
        new PlotBlockNode(shape, this.Settings, this.Matrix, this.Correlations, this.Statistic, this.Statistics);
}

/// <summary>
/// A row as its stages leave it (<see cref="Stages.ResolveShape"/>): whether it names the columns rather than holding a point, and
/// — down a matrix — the name every cell of it shares down the side. It prints as the row written.
/// </summary>
internal sealed class PlotRowNode : ContentNode
{
    internal PlotRowNode(ContentNode written, bool header, string? names) : base(written)
    {
        this.Header = header;
        this.Names = names;
    }

    /// <summary>Whether it names the columns rather than holding a point.</summary>
    public bool Header { get; }

    /// <summary>Down a matrix, what its leading cell names it — the value every cell of it shares down the side.</summary>
    public string? Names { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new PlotRowNode(shape, this.Header, this.Names);
}

/// <summary>
/// A cell as its stages leave it: which column it stands in and what that column is called (<see cref="Stages.ResolveColumns"/>),
/// the number it reads as (<see cref="Stages.ResolveValues"/>), which channels its column feeds
/// (<see cref="Stages.ResolveAesthetics"/>) and — the leading cell of a row of a matrix — the row it names
/// (<see cref="Stages.ResolveShape"/>). It prints as the cell written, quotes and all.
/// </summary>
internal sealed class PlotCellNode : ContentNode
{
    private PlotCellNode(ContentNode written, int? index, string? column, double? number, IReadOnlyList<PlotAesthetic> feeds,
                         string? names)
        : base(written)
    {
        this.Index = index;
        this.Column = column;
        this.Number = number;
        this.Feeds = feeds;
        this.Names = names;
    }

    /// <summary>Which column it stands in, counted from nought — null for a header's cell, and the cell naming a row of a matrix.</summary>
    public int? Index { get; }

    /// <summary>What its column is called, where the table has a header.</summary>
    public string? Column { get; }

    /// <summary>The number it reads as — null for a name, which is what makes it a category.</summary>
    public double? Number { get; }

    /// <summary>Every channel its column feeds — a column may feed several.</summary>
    public IReadOnlyList<PlotAesthetic> Feeds { get; }

    /// <summary>Down a matrix, the row its leading cell names.</summary>
    public string? Names { get; }

    /// <summary>What the stages have said of a cell so far — nothing, where they have said nothing yet.</summary>
    internal static PlotCellNode Of(ContentNode cell) =>
        cell as PlotCellNode ?? new PlotCellNode(cell, null, null, null, [], null);

    internal PlotCellNode Placed(int index, string? column) => new(this, index, column, this.Number, this.Feeds, this.Names);

    internal PlotCellNode Numbered(double number) => new(this, this.Index, this.Column, number, this.Feeds, this.Names);

    internal PlotCellNode Feeding(IReadOnlyList<PlotAesthetic> feeds) => new(this, this.Index, this.Column, this.Number, feeds, this.Names);

    internal PlotCellNode Naming(string names) => new(this, this.Index, this.Column, this.Number, this.Feeds, names);

    protected override ContentNode Reshaped(ContentNode shape) =>
        new PlotCellNode(shape, this.Index, this.Column, this.Number, this.Feeds, this.Names);
}
