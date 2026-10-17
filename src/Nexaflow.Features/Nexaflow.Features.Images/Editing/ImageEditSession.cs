using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Kept;
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
    private DecodedImage? _restoredProxy;
    private string? _restoredPath;
    private DecodedImage? _erasedProxy;
    private string? _erasedPath;
    private DecodedImage? _pipelineBase;   // what the cached pipeline draws from: the photo, or the photo as erased
    private string? _cutOutPath;
    private SKImage? _cutOutMask;
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

    /// <summary>The restoration the recipe asks for, once it is made: the file it is kept in; null while there is none.</summary>
    public string? RestoredPath
    {
        get { lock (_renderGate) return _restoredPath; }
    }

    /// <summary>The restoration decoded, once there is one.</summary>
    public DecodedImage? RestoredProxy
    {
        get { lock (_renderGate) return _restoredProxy; }
    }

    /// <summary>
    /// The restoration to show from now on — its file, or null for none — a different one dropping the decoded one, which
    /// the display decodes again from the new file.
    /// </summary>
    public void UseRestored(string? path)
    {
        lock (_renderGate)
        {
            if (string.Equals(path, _restoredPath, StringComparison.OrdinalIgnoreCase)) return;
            _restoredPath = path;
            _pipeline?.Dispose();
            _pipeline = null;
            _restoredProxy?.Dispose();
            _restoredProxy = null;
        }
    }

    /// <summary>
    /// Takes the restoration decoded from <paramref name="path"/>, replacing a smaller one; one that is no longer the
    /// restoration shown — or arrives after disposal — is disposed at once. The caller hands ownership over.
    /// </summary>
    public void AttachRestored(DecodedImage restored, string path)
    {
        lock (_renderGate)
        {
            if (_disposed || !string.Equals(path, _restoredPath, StringComparison.OrdinalIgnoreCase))
            {
                restored.Dispose();
                return;
            }
            _pipeline?.Dispose();
            _pipeline = null;
            _restoredProxy?.Dispose();
            _restoredProxy = restored;
        }
    }

    /// <summary>The photo as erased, as the recipe asks, once it is made: the file it is kept in; null while there is none.</summary>
    public string? ErasedPath
    {
        get { lock (_renderGate) return _erasedPath; }
    }

    /// <summary>The photo as erased, decoded, once there is one.</summary>
    public DecodedImage? ErasedProxy
    {
        get { lock (_renderGate) return _erasedProxy; }
    }

    /// <summary>
    /// The photo as erased to show from now on — its file, or null for none — a different one dropping the decoded one, which
    /// the display decodes again from the new file.
    /// </summary>
    public void UseErased(string? path)
    {
        lock (_renderGate)
        {
            if (string.Equals(path, _erasedPath, StringComparison.OrdinalIgnoreCase)) return;
            _erasedPath = path;
            _pipeline?.Dispose();
            _pipeline = null;
            _erasedProxy?.Dispose();
            _erasedProxy = null;
        }
    }

    /// <summary>
    /// Takes the photo as erased decoded from <paramref name="path"/>, replacing a smaller one; one that is no longer the one
    /// shown — or arrives after disposal — is disposed at once. The caller hands ownership over.
    /// </summary>
    public void AttachErased(DecodedImage erased, string path)
    {
        lock (_renderGate)
        {
            if (_disposed || !string.Equals(path, _erasedPath, StringComparison.OrdinalIgnoreCase))
            {
                erased.Dispose();
                return;
            }
            _pipeline?.Dispose();
            _pipeline = null;
            _erasedProxy?.Dispose();
            _erasedProxy = erased;
        }
    }

    /// <summary>The mask the recipe's cut-out asks for, once it is made: the file it is kept in; null while there is none.</summary>
    public string? CutOutPath
    {
        get { lock (_renderGate) return _cutOutPath; }
    }

    /// <summary>What the tools made for the recipe, by file — what a save renders from.</summary>
    public MadeByTools Made
    {
        get { lock (_renderGate) return new MadeByTools(_restoredPath, _cutOutPath, _erasedPath); }
    }

    /// <summary>
    /// The cut-out mask the photo is shown through from now on — its file, or null for none. A different file lets the decoded
    /// mask go; the display decodes again from the new file.
    /// </summary>
    public void UseCutOut(string? path)
    {
        lock (_renderGate)
        {
            if (string.Equals(path, _cutOutPath, StringComparison.OrdinalIgnoreCase)) return;
            _cutOutPath = path;
            _pipeline?.Dispose();
            _pipeline = null;
            _cutOutMask?.Dispose();
            _cutOutMask = null;
        }
    }

    /// <summary>The cut-out mask decoded, once there is one.</summary>
    public SKImage? CutOutMask
    {
        get { lock (_renderGate) return _cutOutMask; }
    }

    /// <summary>
    /// Hands over the cut-out mask decoded from <paramref name="path"/>: drawn through from the next frame. One that is no
    /// longer the mask in use — or arrives after disposal — is disposed at once. The caller hands ownership over.
    /// </summary>
    public void AttachCutOut(SKImage mask, string path)
    {
        lock (_renderGate)
        {
            if (_disposed || !string.Equals(path, _cutOutPath, StringComparison.OrdinalIgnoreCase))
            {
                mask.Dispose();
                return;
            }
            _pipeline?.Dispose();
            _pipeline = null;
            _cutOutMask?.Dispose();
            _cutOutMask = mask;
        }
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
            var source = SourceFor(recipe);
            if (!ReferenceEquals(source, _pipelineBase))
            {
                _pipeline?.Dispose();
                _pipeline = null;
            }
            _pipelineBase = source;
            _pipeline ??= _services.Pipeline(source, restored: _restoredProxy, cutOut: _cutOutMask);
            consume(_pipeline.Render(recipe, request));
            return true;
        }
    }

    /// <summary>What <paramref name="recipe"/> is drawn from: the photo as erased where it erases and that is decoded, else
    /// the photo — the photo as it is on disk shows, without its erasures, while they are made.</summary>
    private DecodedImage SourceFor(ImageRecipe recipe) => !recipe.Erasures.IsEmpty && _erasedProxy is { } erased ? erased : _proxy!;

    /// <summary>
    /// A one-off picture of <paramref name="recipe"/> — a thumbnail, what the assistant is shown — rendered beside the
    /// preview without disturbing its cached passes. The caller owns the result; null when there is nothing to render.
    /// </summary>
    public SKImage? Snapshot(ImageRecipe recipe, RenderRequest request)
    {
        lock (_renderGate)
        {
            if (_disposed || _proxy is null) return null;
            using var pipeline = _services.Pipeline(SourceFor(recipe), cache: false, _restoredProxy, _cutOutMask);
            using var picture = pipeline.Render(recipe, request);
            return SKImage.FromBitmap(SKBitmap.FromImage(picture));
        }
    }

    /// <summary>The finished picture's size in pixels, once the photo is decoded.</summary>
    public SKSizeI? OutputSize(ImageRecipe recipe)
    {
        var proxy = Proxy;
        return proxy is null ? null : CropGeometry.OutputSize(recipe, proxy.SourceSize);
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
            _restoredProxy?.Dispose();
            _restoredProxy = null;
            _erasedProxy?.Dispose();
            _erasedProxy = null;
            _cutOutMask?.Dispose();
            _cutOutMask = null;
            _proxy?.Dispose();
            _proxy = null;
        }
    }
}
