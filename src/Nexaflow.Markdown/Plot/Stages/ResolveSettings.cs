using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Plot.Stages;

/// <summary>
/// What a plot block's settings say, and everything worked out from them: the stages its settings call for, with the settings
/// hung on the tree — or, where a setting cannot be read, the tree as parsed with that setting marked with why, since nothing
/// can be worked out without it.
///
/// <para>
/// First, because the settings are what several of the stages after it are told, the same way a Mermaid diagram hands its
/// front matter to the stages that need it (<see cref="PlotPipeline.For"/>).
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

        return PlotPipeline.For(settings).Run(tree).Holding(PlotKinds.Settings, PlotRoles.Settings, settings);
    }
}
