using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing;

/// <summary>
/// One photo open in a viewer: its recipe and that recipe's history, where the recipe is kept, and the decoded
/// picture previews are rendered from.
/// </summary>
/// <remarks>
/// <para>
/// The recipe is written to the photo's sidecar after every change (the store debounces), when the photo is a file
/// on disk and its sidecar is one this build may write. A photo inside an archive, or whose sidecar is unreadable or
/// from a newer build, is still edited — for the session only.
/// </para>
/// <para>
/// History and persistence belong to the UI thread. Rendering happens off it, one render at a time per session,
/// under <see cref="Render"/>'s lock — which is also what keeps disposal from pulling the picture out from under a
/// render.
/// </para>
/// </remarks>
public sealed class ImageEditSession : IDisposable
{
    private readonly ImageEditingServices _services;
    private readonly Lock _renderGate = new();
    private DecodedImage? _proxy;
    private RenderPipeline? _pipeline;
    private bool _disposed;

    public ImageEditSession(string path, ImageEditingServices services)
    {
        Path      = path;
        _services = services;
        IsFileBacked = ImageEditingServices.IsFileBacked(path);

        var load = IsFileBacked ? services.Recipes.Load(path) : new RecipeLoadResult(RecipeLoadStatus.None, null);
        LoadStatus = load.Status;
        CanPersist = IsFileBacked && load.IsWritable;
        History    = new RecipeHistory(load.Recipe ?? ImageRecipe.Identity);
        History.Changed += OnHistoryChanged;
    }

    public string Path { get; }

    /// <summary>A file on disk, as opposed to one inside an archive.</summary>
    public bool IsFileBacked { get; }

    /// <summary>Edits are kept in the sidecar; false means they last as long as the viewer.</summary>
    public bool CanPersist { get; private set; }

    /// <summary>What the sidecar held when the photo was opened.</summary>
    public RecipeLoadStatus LoadStatus { get; private set; }

    public RecipeHistory History { get; }

    public ImageRecipe Recipe => History.Current;

    /// <summary>The decoded picture, once there is one.</summary>
    public DecodedImage? Proxy
    {
        get { lock (_renderGate) return _proxy; }
    }

    /// <summary>Raised on the UI thread whenever the recipe changes, by an edit, an undo or a reload.</summary>
    public event Action? RecipeChanged;

    /// <summary>
    /// Takes a newly decoded picture to render from, replacing a smaller one. The caller hands ownership over; a
    /// picture arriving after disposal is disposed at once.
    /// </summary>
    public void Attach(DecodedImage proxy)
    {
        lock (_renderGate)
        {
            if (_disposed)
            {
                proxy.Dispose();
                return;
            }
            _pipeline?.Dispose();
            _pipeline = null;
            _proxy?.Dispose();
            _proxy = proxy;
        }
    }

    /// <summary>
    /// Renders <paramref name="recipe"/> and hands the picture to <paramref name="consume"/> while it is still valid.
    /// False when there is nothing to render yet, or the session has closed.
    /// </summary>
    public bool Render(ImageRecipe recipe, RenderRequest request, Action<SKImage> consume)
    {
        lock (_renderGate)
        {
            if (_disposed || _proxy is null) return false;
            _pipeline ??= _services.Pipeline(_proxy);
            consume(_pipeline.Render(recipe, request));
            return true;
        }
    }

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

    public void Dispose()
    {
        History.Changed -= OnHistoryChanged;
        if (CanPersist) _services.Recipes.Flush(Path);
        lock (_renderGate)
        {
            _disposed = true;
            _pipeline?.Dispose();
            _pipeline = null;
            _proxy?.Dispose();
            _proxy = null;
        }
    }
}
