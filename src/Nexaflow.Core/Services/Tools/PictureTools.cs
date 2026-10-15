using System.Security.Cryptography;
using System.Text;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// The picture tools as features and the assistant use them (<see cref="IPictureTools"/>): Real-ESRGAN and GFPGAN in the
/// AI host (<see cref="OwnedPictureTools"/>), their models — and the CUDA pack, where it suits the machine — fetched once,
/// app-wide, when a person asks, and shown with the app's other work in the background while they come.
/// </summary>
public sealed class PictureTools : IPictureTools, ILocalTool
{
    /// <summary>How much of a restoration's progress the upscaler is, where faces are remade after it.</summary>
    private const double UpscalingShare = 0.85;

    private readonly OwnedPictureTools _tools;
    private readonly IModelAssetStore _store;
    private readonly IBackgroundActivityManager? _activities;
    private readonly Lock _gate = new();
    private Task? _download;
    private int _percent;

    public PictureTools(OwnedPictureTools tools, IModelAssetStore store, IBackgroundActivityManager? activities)
    {
        _tools = tools;
        _store = store;
        _activities = activities;
        tools.AvailabilityChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public string Name => Str.Get("Shell.Tools.Picture.Name");

    public string About => Str.Get("Shell.Tools.Picture.About");

    public ToolModelsState State
        => !_tools.RunsHere ? ToolModelsState.Unavailable
         : _tools.IsReady ? ToolModelsState.Ready
         : _download is { IsCompleted: false } ? ToolModelsState.Downloading
         : ToolModelsState.NotDownloaded;

    /// <summary>Every file the tools need here: their models, and the CUDA pack where it suits the machine.</summary>
    public IReadOnlyList<ModelAsset> Files => [.. _tools.Catalog?.Files ?? [], .. _tools.Runtime?.Files ?? []];

    public IReadOnlyList<ModelAsset> OwnFiles => _tools.Catalog?.Files ?? [];

    public long DownloadBytes => Files.Where(f => _store.Locate(f) is null).Sum(f => f.Size);

    public int DownloadPercent => _percent;

    public event EventHandler? Changed;

    public void Download()
    {
        lock (_gate)
        {
            if (State is not ToolModelsState.NotDownloaded) return;
            _percent = 0;
            _download = FetchAsync();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task FetchAsync()
    {
        await Task.Yield();   // the caller has the task before anything reports
        var activity = _activities?.StartActivity(Str.Format("Shell.Config.Local.DownloadingFormat", Name, 0));
        try
        {
            await _store.EnsureAllAsync([.. Files.Where(f => _store.Locate(f) is null)], new Inline<AssetProgress>(p =>
            {
                int percent = (int)(p.Fraction * 100);
                if (percent == _percent) return;   // said again only when it moves
                _percent = percent;
                activity?.Update(Str.Format("Shell.Config.Local.DownloadingFormat", Name, percent));
                Changed?.Invoke(this, EventArgs.Empty);
            }));
            activity?.Complete();
        }
        catch (Exception ex)
        {
            activity?.Fail(ex.Message);   // the tools stay to be downloaded; the person can ask again
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool Restores(int width, int height, int scale) => RestorePicture.Restores(width, height, scale);

    public string ModelsVersion => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", OwnFiles.Select(f => f.Sha256))))) [..16];

    public async Task<PictureRestoration> RestoreAsync(PictureRestoreRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        if (!Restores(request.Width, request.Height, request.Scale))
            throw new InvalidOperationException($"A {request.Width}x{request.Height} picture is too large to restore at {request.Scale}x.");
        double upscaling = request.Faces ? UpscalingShare : 1;
        var steps = progress is null ? null : new Inline<RestoringStep>(step => progress.Report(step.Stage == RestoringStages.Faces
            ? upscaling + (1 - upscaling) * step.Done / step.Total
            : upscaling * step.Done / step.Total));
        var restored = await _tools.RestoreAsync(request.Pixels, request.Width, request.Height, request.Scale, request.Faces, request.Into, steps, ct);
        return new PictureRestoration(restored.Width, restored.Height, restored.FacesRestored);
    }

    /// <summary>Progress reported at once, on the reporting thread.</summary>
    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
