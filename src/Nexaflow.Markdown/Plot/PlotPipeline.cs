using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Plot.Stages;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// The stages a plot block is read through, and the order they have to run in.
///
/// <para>
/// Each needs the answers of the ones before it. Nothing can name a column before the header is known,
/// and whether a row is the header turns on what its cells read as; nothing can say which channel a
/// setting maps to before the columns have names to match against; and a matrix's leading cell has to be
/// known for what it is before the columns are counted, or every column would be off by one.
/// </para>
/// <para>
/// The settings are read off the tree before any of this (<see cref="ResolveSettings"/>), because they are what several of
/// the stages are told — the same way a Mermaid diagram hands its front matter to the stages that need it.
/// </para>
/// </summary>
public static class PlotPipeline
{
    /// <summary>The stages, told what the block's settings say.</summary>
    public static AstPipeline For(PlotSettings settings) =>
        new(new ResolveShape(settings),
            new ResolveColumns(),
            new ResolveValues(),
            new ResolveAesthetics(settings),
            new ResolveCorrelations(settings));

    /// <summary>A block parsed and worked out (<see cref="ResolveSettings"/>).</summary>
    public static ContentNode Read(string? source, PlotFence fence) => new ResolveSettings(fence).Run(PlotParser.Parse(source));

    /// <summary>The settings a tree <see cref="Read"/> made was read with, or null where they could not be read.</summary>
    public static PlotSettings? Settings(ContentNode tree) => tree.HeldAs(PlotRoles.Settings) as PlotSettings;
}
