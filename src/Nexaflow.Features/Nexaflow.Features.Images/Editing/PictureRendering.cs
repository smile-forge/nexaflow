using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing;

/// <summary>
/// One picture as it is drawn: its file, the decoded picture previews are rendered from, what the picture tools made of it
/// for its recipe — its restoration, the picture as erased, the mask it is cut out by — and the render pipeline that draws
/// the recipe over them. A photo's edit session is one; each picture layer of a layered picture has its own.
/// </summary>
/// <remarks>
/// Rendering happens off the UI thread, one render at a time, under the rendering's lock — which is also what keeps a
/// decode arriving, or disposal, from pulling a picture out from under a render. Pictures that render together, the layers
/// of one layered picture, share one lock, so a composite holds all their renders at once.
/// </remarks>
public abstract class PictureRendering : IDisposable
{
    private readonly ImageEditingServices _services;
    private DecodedImage? _proxy;
    private DecodedImage? _restoredProxy;
    private string? _restoredPath;
    private DecodedImage? _erasedProxy;
    private string? _erasedPath;
    private DecodedImage? _pipelineBase;   // what the cached pipeline draws from: the picture, or the picture as erased
    private string? _cutOutPath;
    private SKImage? _cutOutMask;
    private RenderPipeline? _pipeline;
    private long _version;
    private bool _disposed;

    /// <param name="path">The picture's file, wherever the file system finds it.</param>
    /// <param name="gate">The lock its renders take — shared by pictures rendered together; its own where none is given.</param>
    protected PictureRendering(string path, ImageEditingServices services, Lock? gate = null)
    {
        Path      = path;
        _services = services;
        Gate      = gate ?? new Lock();
    }

    public string Path { get; }

    /// <summary>The lock every render and every change to what is rendered from takes.</summary>
    protected internal Lock Gate { get; }

    /// <summary>The recipe the picture is drawn by, as it stands.</summary>
    public abstract ImageRecipe Recipe { get; }

    /// <summary>What changes whenever what the picture is rendered from does — a decode arriving, a tool's result taken or let go.</summary>
    public long Version
    {
        get { lock (Gate) return _version; }
    }

    /// <summary>The decoded picture, once there is one.</summary>
    public DecodedImage? Proxy
    {
        get { lock (Gate) return _proxy; }
    }

    /// <summary>The restoration the recipe asks for, once it is made: the file it is kept in; null while there is none.</summary>
    public string? RestoredPath
    {
        get { lock (Gate) return _restoredPath; }
    }

    /// <summary>The restoration decoded, once there is one.</summary>
    public DecodedImage? RestoredProxy
    {
        get { lock (Gate) return _restoredProxy; }
    }

    /// <summary>
    /// The restoration to show from now on — its file, or null for none — a different one dropping the decoded one, which
    /// the display decodes again from the new file.
    /// </summary>
    public void UseRestored(string? path)
    {
        lock (Gate)
        {
            if (string.Equals(path, _restoredPath, StringComparison.OrdinalIgnoreCase)) return;
            _restoredPath = path;
            DropPipeline();
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
        lock (Gate)
        {
            if (_disposed || !string.Equals(path, _restoredPath, StringComparison.OrdinalIgnoreCase))
            {
                restored.Dispose();
                return;
            }
            DropPipeline();
            _restoredProxy?.Dispose();
            _restoredProxy = restored;
        }
    }

    /// <summary>The picture as erased, as the recipe asks, once it is made: the file it is kept in; null while there is none.</summary>
    public string? ErasedPath
    {
        get { lock (Gate) return _erasedPath; }
    }

    /// <summary>The picture as erased, decoded, once there is one.</summary>
    public DecodedImage? ErasedProxy
    {
        get { lock (Gate) return _erasedProxy; }
    }

    /// <summary>
    /// The picture as erased to show from now on — its file, or null for none — a different one dropping the decoded one,
    /// which the display decodes again from the new file.
    /// </summary>
    public void UseErased(string? path)
    {
        lock (Gate)
        {
            if (string.Equals(path, _erasedPath, StringComparison.OrdinalIgnoreCase)) return;
            _erasedPath = path;
            DropPipeline();
            _erasedProxy?.Dispose();
            _erasedProxy = null;
        }
    }

    /// <summary>
    /// Takes the picture as erased decoded from <paramref name="path"/>, replacing a smaller one; one that is no longer the one
    /// shown — or arrives after disposal — is disposed at once. The caller hands ownership over.
    /// </summary>
    public void AttachErased(DecodedImage erased, string path)
    {
        lock (Gate)
        {
            if (_disposed || !string.Equals(path, _erasedPath, StringComparison.OrdinalIgnoreCase))
            {
                erased.Dispose();
                return;
            }
            DropPipeline();
            _erasedProxy?.Dispose();
            _erasedProxy = erased;
        }
    }

    /// <summary>The mask the recipe's cut-out asks for, once it is made: the file it is kept in; null while there is none.</summary>
    public string? CutOutPath
    {
        get { lock (Gate) return _cutOutPath; }
    }

    /// <summary>What the tools made for the recipe, by file — what a save renders from.</summary>
    public MadeByTools Made
    {
        get { lock (Gate) return new MadeByTools(_restoredPath, _cutOutPath, _erasedPath); }
    }

    /// <summary>
    /// The cut-out mask the picture is shown through from now on — its file, or null for none. A different file lets the
    /// decoded mask go; the display decodes again from the new file.
    /// </summary>
    public void UseCutOut(string? path)
    {
        lock (Gate)
        {
            if (string.Equals(path, _cutOutPath, StringComparison.OrdinalIgnoreCase)) return;
            _cutOutPath = path;
            DropPipeline();
            _cutOutMask?.Dispose();
            _cutOutMask = null;
        }
    }

    /// <summary>The cut-out mask decoded, once there is one.</summary>
    public SKImage? CutOutMask
    {
        get { lock (Gate) return _cutOutMask; }
    }

    /// <summary>
    /// Hands over the cut-out mask decoded from <paramref name="path"/>: drawn through from the next frame. One that is no
    /// longer the mask in use — or arrives after disposal — is disposed at once. The caller hands ownership over.
    /// </summary>
    public void AttachCutOut(SKImage mask, string path)
    {
        lock (Gate)
        {
            if (_disposed || !string.Equals(path, _cutOutPath, StringComparison.OrdinalIgnoreCase))
            {
                mask.Dispose();
                return;
            }
            DropPipeline();
            _cutOutMask?.Dispose();
            _cutOutMask = mask;
        }
    }

    /// <summary>
    /// Takes a newly decoded picture to render from, replacing a smaller one. The caller hands ownership over; a picture
    /// arriving after disposal is disposed at once.
    /// </summary>
    public void Attach(DecodedImage proxy)
    {
        lock (Gate)
        {
            if (_disposed)
            {
                proxy.Dispose();
                return;
            }
            DropPipeline();
            _proxy?.Dispose();
            _proxy = proxy;
        }
    }

    /// <summary>
    /// Renders <paramref name="recipe"/> and hands the picture to <paramref name="consume"/> while it is still valid.
    /// False when there is nothing to render yet, or the rendering has closed.
    /// </summary>
    public bool Render(ImageRecipe recipe, RenderRequest request, Action<SKImage> consume)
    {
        lock (Gate)
        {
            if (RenderHeld(recipe, request) is not { } picture) return false;
            consume(picture);
            return true;
        }
    }

    /// <summary>
    /// Renders <paramref name="recipe"/> for a caller holding <see cref="Gate"/>: the picture stays valid while it is held
    /// and until the next render; null when there is nothing to render yet, or the rendering has closed.
    /// </summary>
    protected internal SKImage? RenderHeld(ImageRecipe recipe, RenderRequest request)
    {
        if (_disposed || _proxy is null) return null;
        var source = SourceFor(recipe);
        if (!ReferenceEquals(source, _pipelineBase))
        {
            _pipeline?.Dispose();
            _pipeline = null;
        }
        _pipelineBase = source;
        _pipeline ??= _services.Pipeline(source, restored: _restoredProxy, cutOut: _cutOutMask);
        return _pipeline.Render(recipe, request);
    }

    /// <summary>What <paramref name="recipe"/> is drawn from: the picture as erased where it erases and that is decoded, else
    /// the picture — the picture as it is on disk shows, without its erasures, while they are made.</summary>
    private DecodedImage SourceFor(ImageRecipe recipe) => !recipe.Erasures.IsEmpty && _erasedProxy is { } erased ? erased : _proxy!;

    /// <summary>
    /// A one-off picture of <paramref name="recipe"/> — a thumbnail, what the assistant is shown — rendered beside the
    /// preview without disturbing its cached passes. The caller owns the result; null when there is nothing to render.
    /// </summary>
    public SKImage? Snapshot(ImageRecipe recipe, RenderRequest request)
    {
        lock (Gate)
        {
            if (_disposed || _proxy is null) return null;
            using var pipeline = _services.Pipeline(SourceFor(recipe), cache: false, _restoredProxy, _cutOutMask);
            using var picture = pipeline.Render(recipe, request);
            return SKImage.FromBitmap(SKBitmap.FromImage(picture));
        }
    }

    /// <summary>The finished picture's size in pixels, once the picture is decoded.</summary>
    public SKSizeI? OutputSize(ImageRecipe recipe)
    {
        var proxy = Proxy;
        return proxy is null ? null : CropGeometry.OutputSize(recipe, proxy.SourceSize);
    }

    /// <summary>The cached pipeline goes — what it draws from changed — and the version moves on.</summary>
    private void DropPipeline()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _version++;
    }

    public virtual void Dispose()
    {
        lock (Gate)
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
        GC.SuppressFinalize(this);
    }
}
