using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Gives the photo on screen the cut-out its recipe asks for (<see cref="RecipeCutOut"/>): the mask kept for it where it
/// is made, else picked now by picture selection in the background, and then the photo shown through it — what it does
/// not keep cleared.
/// </summary>
public sealed class BackgroundRemover : ToolEdit<RecipeCutOut>
{
    private readonly IPictureSelection? _selection;

    /// <param name="selection">Picture selection, where the app has it.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the session and this are read.</param>
    public BackgroundRemover(ImageEditingServices services, IPictureSelection? selection, Func<Action, Task> runOnUi)
        : base(services, selection, runOnUi, "Images.RemoveBackground.")
        => _selection = selection;

    protected override RecipeCutOut? AskOf(ImageRecipe recipe) => recipe.CutOut;

    protected override bool MakesSame(RecipeCutOut ask, RecipeCutOut other) => ask == other;

    protected override string KeptPath(string photo, RecipeCutOut ask, string modelsVersion) => Services.CutOuts.PathOf(photo, ask, modelsVersion);

    protected override string? Kept(string photo, RecipeCutOut ask, string modelsVersion) => Services.CutOuts.Find(photo, ask, modelsVersion);

    protected override Task<string> MakeAsync(string photo, RecipeCutOut ask, IProgress<double> progress, CancellationToken ct)
        => Services.CutOuts.MakeAsync(Services, photo, ask, _selection, progress, ct);

    /// <summary>A cut-out that asks picture selection nothing — a region drawn by hand alone — needs no models at all.</summary>
    protected override IReadOnlyList<IToolModels?> Needs(RecipeCutOut ask) => ask.AsksPictureSelection ? [_selection] : [];

    /// <summary>Removing the background is always offered: a region drawn by hand cuts out on any machine.</summary>
    public override bool Available => true;

    protected override string? UsedPath(PictureRendering session) => session.CutOutPath;

    protected override void Use(PictureRendering session, string? path) => session.UseCutOut(path);
}
