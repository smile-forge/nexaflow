using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Visuals.Common.Editing;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing;

/// <summary>
/// One photo open in a viewer: the picture as it is drawn (<see cref="PictureRendering"/>), its recipe and that recipe's
/// history, and where the recipe is kept.
/// </summary>
/// <remarks>
/// The recipe is written to the photo's sidecar after every change (the store debounces), when the photo is a file on disk
/// and its sidecar is one this build may write. A photo inside an archive, or whose sidecar is unreadable or from a newer
/// build, is still edited — for the session only. History and persistence belong to the UI thread.
/// </remarks>
public sealed class ImageEditSession : PictureRendering, IRecipeEditing
{
    private readonly ImageEditingServices _services;

    public ImageEditSession(string path, ImageEditingServices services) : base(path, services)
    {
        _services = services;
        IsFileBacked = ImageEditingServices.IsFileBacked(path);

        var load = IsFileBacked ? services.Recipes.Load(path) : new RecipeLoadResult(RecipeLoadStatus.None, null);
        LoadStatus = load.Status;
        CanPersist = IsFileBacked && load.IsWritable;
        History    = new SnapshotHistory<ImageRecipe>(load.Recipe ?? ImageRecipe.Identity);
        History.Changed += OnHistoryChanged;
    }

    /// <summary>A file on disk, as opposed to one inside an archive.</summary>
    public bool IsFileBacked { get; }

    /// <summary>Edits are kept in the sidecar; false means they last as long as the viewer.</summary>
    public bool CanPersist { get; private set; }

    /// <summary>What the sidecar held when the photo was opened.</summary>
    public RecipeLoadStatus LoadStatus { get; private set; }

    /// <summary>
    /// Undo and redo over whole recipes. A recipe is a few hundred bytes, so a snapshot per step costs nothing and undo
    /// is exact.
    /// </summary>
    public SnapshotHistory<ImageRecipe> History { get; }

    public override ImageRecipe Recipe => History.Current;

    PictureRendering IRecipeEditing.Rendering => this;

    void IRecipeEditing.Commit(ImageRecipe next) => History.Commit(next);

    void IMarkupEditing.BeginGesture() => History.BeginGesture();

    void IMarkupEditing.EndGesture() => History.EndGesture();

    void IMarkupEditing.CancelGesture() => History.CancelGesture();

    bool IMarkupEditing.InGesture => History.InGesture;

    /// <summary>Raised on the UI thread whenever the recipe changes, by an edit, an undo or a reload.</summary>
    public event Action? RecipeChanged;

    /// <summary>
    /// The photo changed after its recipe was made, and the user chose to keep the edits: they are re-anchored to the
    /// photo as it is now.
    /// </summary>
    public void KeepEditsForChangedSource()
    {
        if (LoadStatus != RecipeLoadStatus.SourceChanged) return;
        LoadStatus = RecipeLoadStatus.Loaded;
        History.Adopt(Recipe with { Source = null });
        Persist();
    }

    /// <summary>Back to the photo as it is on disk, export settings and all: the history keeps the way back, the sidecar goes.</summary>
    public void Revert()
    {
        if (LoadStatus == RecipeLoadStatus.SourceChanged) LoadStatus = RecipeLoadStatus.Loaded;
        History.Commit(ImageRecipe.Identity);
    }

    /// <summary>
    /// The sidecar changed on disk — another window edited the photo. A recipe other than this one becomes an
    /// undoable step here; this session's own write coming back is recognised and ignored.
    /// </summary>
    public void ReloadFromSidecar()
    {
        if (!CanPersist) return;
        var load = _services.Recipes.Load(Path);
        if (!load.IsWritable)
        {
            LoadStatus = load.Status;
            CanPersist = false;
            RecipeChanged?.Invoke();
            return;
        }
        var stored = load.Recipe ?? ImageRecipe.Identity;
        if (SameEdits(stored, Recipe)) return;
        History.Adopt(stored);
    }

    /// <summary>
    /// The photo was saved with its edits baked in: its pixels now are the recipe's, so the recipe is spent and the
    /// sidecar goes. The history restarts from the new picture.
    /// </summary>
    public void Baked()
    {
        if (IsFileBacked) _services.Recipes.Delete(Path);
        LoadStatus = RecipeLoadStatus.None;
        CanPersist = IsFileBacked;
        History.Reset(ImageRecipe.Identity);
    }

    private void OnHistoryChanged()
    {
        Persist();
        RecipeChanged?.Invoke();
    }

    private void Persist()
    {
        if (CanPersist && LoadStatus != RecipeLoadStatus.SourceChanged) _services.Recipes.SaveDebounced(Path, Recipe);
    }

    /// <summary>The same edits, whichever photo fingerprint each carries.</summary>
    private static bool SameEdits(ImageRecipe a, ImageRecipe b) => (a with { Source = null }).Equals(b with { Source = null });

    public override void Dispose()
    {
        History.Changed -= OnHistoryChanged;
        if (CanPersist) _services.Recipes.Flush(Path);
        base.Dispose();
    }
}
