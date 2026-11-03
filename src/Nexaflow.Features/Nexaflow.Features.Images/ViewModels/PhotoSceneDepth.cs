using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>What a recipe asks scene depth for: the picture's depth, the same whatever its haze and lens blur.</summary>
public sealed record SceneDepthAsk
{
    public static SceneDepthAsk Instance { get; } = new();

    private SceneDepthAsk() { }
}

/// <summary>
/// Gives the photo on screen the scene depth its recipe's haze and lens blur are drawn from
/// (<see cref="ImageRecipe.AsksSceneDepth"/>): the kept depth where it is estimated, else estimated now in the background, and
/// then drawn from — a focus moved, an aperture or a haze dragged, never estimating it again. The depth is of the photo as
/// erased, where the recipe erases from it, so what was rubbed out is not in it.
/// </summary>
public sealed class PhotoSceneDepth : ToolEdit<SceneDepthAsk>
{
    private readonly ISceneDepth? _depth;

    /// <param name="depth">Scene depth, where the app has it.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the session and this are read.</param>
    public PhotoSceneDepth(ImageEditingServices services, ISceneDepth? depth, Func<Action, Task> runOnUi, PictureModels? models = null)
        : base(services, depth, runOnUi, "Images.SceneDepth.", models)
        => _depth = depth;

    /// <summary>Whether scene depth is here now, ready to estimate a photo's.</summary>
    public bool EstimatesNow => _depth is { State: ToolModelsState.Ready };

    protected override SceneDepthAsk? AskOf(ImageRecipe recipe) => recipe.AsksSceneDepth ? SceneDepthAsk.Instance : null;

    /// <summary>The photo as erased, where the recipe erases from it — the depth is of that, so it waits until it is made — else
    /// the photo.</summary>
    protected override string? Input(PictureRendering session) => session.Recipe.Erasures.IsEmpty ? session.Path : session.ErasedPath;

    protected override bool MakesSame(SceneDepthAsk ask, SceneDepthAsk other) => true;

    protected override string KeptPath(string photo, SceneDepthAsk ask, string modelsVersion) => Services.SceneDepths.PathOf(photo, modelsVersion);

    protected override string? Kept(string photo, SceneDepthAsk ask, string modelsVersion) => Services.SceneDepths.Find(photo, modelsVersion);

    protected override async Task<string> MakeAsync(string photo, SceneDepthAsk ask, IProgress<double> progress, CancellationToken ct)
    {
        var made = await Services.SceneDepths.MakeAsync(Services, photo, _depth!, ct);
        progress.Report(1);
        return made;
    }

    protected override string? UsedPath(PictureRendering session) => session.SceneDepthPath;

    protected override void Use(PictureRendering session, string? path) => session.UseSceneDepth(path);
}
