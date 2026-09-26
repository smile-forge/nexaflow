using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Plot.Stages;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// The stages a plot block is read through, and the order they have to run in.
///
/// <para>
/// Each needs the answers of the ones before it. Nothing can be worked out before the settings are read, because they are
/// what every stage after that reads off the block (<see cref="PlotBlockNode"/>); nothing can name a column before the header
/// is known, and whether a row is the header turns on what its cells read as; nothing can say which channel a setting maps to
/// before the columns have names to match against; a matrix's leading cell has to be known for what it is before the columns
/// are counted, or every column would be off by one; and nothing can be worked out from x and y until something feeds them.
/// </para>
/// </summary>
public static class PlotPipeline
{
    /// <summary>The stages, for a block whose fence names <paramref name="fence"/> — which decides what its settings default to.</summary>
    public static AstPipeline Of(PlotFence fence) =>
        new(new ResolveSettings(fence),
            new ResolveShape(),
            new ResolveColumns(),
            new ResolveValues(),
            new ResolveAesthetics(),
            new ResolveCorrelations(),
            new ResolveStatistics());
}
