using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Gives the photo on screen the restoration its recipe asks for (<see cref="RecipeRestore"/>): the kept one where it is
/// made, else made now by the picture tools in the background, and then shown, laid over the photo at its strength — a
/// strength alone never making it again.
/// </summary>
public sealed class PhotoRestorer : ToolEdit<RecipeRestore>
{
    private readonly IPictureTools? _tools;

    /// <param name="tools">The picture tools, where the app has them.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the session and this are read.</param>
    public PhotoRestorer(ImageEditingServices services, IPictureTools? tools, Func<Action, Task> runOnUi)
        : base(services, tools, runOnUi, "Images.Restore.")
        => _tools = tools;

    /// <summary>Whether a <paramref name="width"/> × <paramref name="height"/> photo restores at <paramref name="scale"/> here.</summary>
    public bool Restores(int width, int height, int scale) => Available && _tools!.Restores(width, height, scale);

    protected override RecipeRestore? AskOf(ImageRecipe recipe) => recipe.Restore;

    protected override bool MakesSame(RecipeRestore ask, RecipeRestore other) => ask.MakesSameAs(other);

    protected override string KeptPath(string photo, RecipeRestore ask, string modelsVersion) => Services.Restored.PathOf(photo, ask, modelsVersion);

    protected override string? Kept(string photo, RecipeRestore ask, string modelsVersion) => Services.Restored.Find(photo, ask, modelsVersion);

    protected override Task<string> MakeAsync(string photo, RecipeRestore ask, IProgress<double> progress, CancellationToken ct)
        => Services.Restored.MakeAsync(Services, photo, ask, _tools!, progress, ct);

    protected override string? UsedPath(ImageEditSession session) => session.RestoredPath;

    protected override void Use(ImageEditSession session, string? path) => session.UseRestored(path);
}
