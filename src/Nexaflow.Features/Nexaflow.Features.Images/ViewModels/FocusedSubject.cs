using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Gives the photo on screen the outline of what its lens blur is focused on (<see cref="RecipeLensBlur.Subject"/>) — a thing
/// clicked, or the selection — so the blur keeps that edge crisp: the mask kept for it where it is made, else picked now by
/// picture selection in the background. Kept as a cut-out mask is, beside the cut-outs, as it is the same kind of mask.
/// </summary>
public sealed class FocusedSubject : ToolEdit<RecipeCutOut>
{
    private readonly IPictureSelection? _selection;

    /// <param name="selection">Picture selection, where the app has it.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the session and this are read.</param>
    public FocusedSubject(ImageEditingServices services, IPictureSelection? selection, Func<Action, Task> runOnUi, PictureModels? models = null)
        : base(services, selection, runOnUi, "Images.FocusedSubject.", models)
        => _selection = selection;

    protected override RecipeCutOut? AskOf(ImageRecipe recipe) => recipe.LensBlur?.Subject;

    protected override bool MakesSame(RecipeCutOut ask, RecipeCutOut other) => ask == other;

    protected override string KeptPath(string photo, RecipeCutOut ask, string modelsVersion) => Services.CutOuts.PathOf(photo, ask, modelsVersion);

    protected override string? Kept(string photo, RecipeCutOut ask, string modelsVersion) => Services.CutOuts.Find(photo, ask, modelsVersion);

    protected override Task<string> MakeAsync(string photo, RecipeCutOut ask, IProgress<double> progress, CancellationToken ct)
        => Services.CutOuts.MakeAsync(Services, photo, ask, _selection, progress, ct);

    protected override IReadOnlyList<IToolModels?> Needs(RecipeCutOut ask) => CutOutMasks.ModelsFor(ask, _selection, Services.SceneDepth);

    /// <summary>Always offered: a selection drawn by hand outlines a subject on any machine.</summary>
    public override bool Available => true;

    protected override string? UsedPath(PictureRendering session) => session.SubjectPath;

    protected override void Use(PictureRendering session, string? path) => session.UseSubject(path);
}
