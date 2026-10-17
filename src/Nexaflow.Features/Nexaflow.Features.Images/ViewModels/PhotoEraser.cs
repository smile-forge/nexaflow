using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Gives the photo on screen the erasing its recipe asks for (<see cref="ImageRecipe.Erasures"/>): the erased photo kept
/// where it is made, else made now in the background, and then shown in the photo's place. Content-aware fill and the
/// healing brush need nothing downloaded; a step filled by picture erasing needs that, and one that picks a thing needs
/// picture selection.
/// </summary>
public sealed class PhotoEraser : ToolEdit<EraseSteps>
{
    private readonly IPictureErasing? _erasing;
    private readonly IPictureSelection? _selection;

    /// <param name="erasing">Picture erasing, where the app has it.</param>
    /// <param name="selection">Picture selection, where the app has it.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the session and this are read.</param>
    public PhotoEraser(ImageEditingServices services, IPictureErasing? erasing, IPictureSelection? selection, Func<Action, Task> runOnUi)
        : base(services, [erasing, selection], runOnUi, "Images.Erase.")
        => (_erasing, _selection) = (erasing, selection);

    /// <summary>Erasing is always offered: content-aware fill and the healing brush run on any machine.</summary>
    public override bool Available => true;

    /// <summary>Whether a step can be filled by picture erasing on this machine.</summary>
    public bool ErasesByAi => _erasing is { State: not ToolModelsState.Unavailable };

    /// <summary>Whether picture erasing is here now, ready to fill a step.</summary>
    public bool ErasesByAiNow => _erasing is { State: ToolModelsState.Ready };

    protected override IReadOnlyList<IToolModels?> Needs(EraseSteps ask)
        => [.. ask.Steps.Any(s => s.Fill == EraseFill.Ai) ? [_erasing] : Array.Empty<IToolModels?>(),
            .. ask.Steps.Any(s => s.Pick is not null) ? [_selection] : Array.Empty<IToolModels?>()];

    protected override EraseSteps? AskOf(ImageRecipe recipe) => recipe.Erasures.IsEmpty ? null : new EraseSteps(recipe.Erasures);

    protected override bool MakesSame(EraseSteps ask, EraseSteps other) => ask == other;

    protected override string KeptPath(string photo, EraseSteps ask, string modelsVersion) => Services.Erased.PathOf(photo, ask, modelsVersion);

    protected override string? Kept(string photo, EraseSteps ask, string modelsVersion) => Services.Erased.Find(photo, ask, modelsVersion);

    protected override Task<string> MakeAsync(string photo, EraseSteps ask, IProgress<double> progress, CancellationToken ct)
        => Services.Erased.MakeAsync(Services, photo, ask, VersionOf(Needs(ask))!, _erasing, _selection, progress, ct);

    protected override string? UsedPath(ImageEditSession session) => session.ErasedPath;

    protected override void Use(ImageEditSession session, string? path) => session.UseErased(path);
}
