using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// What a plot block's settings say, hung on the block (<see cref="PlotBlockNode"/>) — or, where a setting cannot be read, the
/// tree as parsed with that setting marked with why, since nothing after this can be worked out without it.
///
/// <para>
/// First, because the settings are what every stage after it reads, the same way a Mermaid diagram's front matter is hung on
/// its block for the stages that need it.
/// </para>
/// </summary>
/// <param name="fence">Which kind of plot the block's fence named, which decides what its settings default to.</param>
public sealed class ResolveSettings(PlotFence fence) : IAstStage
{
    public string Name => "plot:settings";

    public ContentNode Run(ContentNode tree)
    {
        if (!PlotReader.TrySettings(tree, fence, out var settings, out var error, out var key) || settings is null)
            return PlotReader.Marked(tree, key, error ?? "This plot's settings could not be read.");

        return new PlotBlockNode(tree, settings);
    }
}
