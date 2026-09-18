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
/// The settings are read off the tree before any of this, rather than by a stage, because they are what
/// several of the stages are told — the same way a Mermaid diagram hands its front matter to the stages
/// that need it.
/// </para>
/// </summary>
public static class PlotPipeline
{
    /// <summary>The stages, told what the block's settings say.</summary>
    public static AstPipeline For(PlotSettings settings) =>
        new(new ResolveShape(settings),
            new ResolveColumns(),
            new ResolveValues(),
            new ResolveAesthetics(settings));

    /// <summary>
    /// A block read the whole way: parsed, its settings taken off it, and the stages run over it.
    ///
    /// <para>
    /// The tree comes back whatever happens, because it is what is shown when nothing else can be — a
    /// block whose settings will not read is still every character somebody typed.
    /// </para>
    /// </summary>
    public static ContentNode Read(string? source, PlotFence fence,
                                   out PlotSettings settings, out string? error)
    {
        var tree = PlotParser.Parse(source);

        settings = PlotReader.TrySettings(tree, fence, out var read, out error) && read is not null
            ? read
            : PlotSettings.Default;

        return error is null ? For(settings).Run(tree) : tree;
    }
}
