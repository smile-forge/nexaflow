using System.Security.Cryptography;
using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// A tool's models as features, the assistant and the settings pages see them (<see cref="ILocalTool"/>): where they
/// stand on this machine, and their download — with the CUDA pack where it suits the machine — fetched once, app-wide,
/// when a person asks, and shown with the app's other work in the background while it comes. What the tool does is each
/// tool's own.
/// </summary>
public abstract class LocalToolModels : ILocalTool
{
    private readonly OwnedToolFront _front;
    private readonly IModelAssetStore _store;
    private readonly IBackgroundActivityManager? _activities;
    private readonly Lock _gate = new();
    private Task? _download;
    private int _percent;

    protected LocalToolModels(OwnedToolFront front, IModelAssetStore store, IBackgroundActivityManager? activities)
    {
        _front = front;
        _store = store;
        _activities = activities;
        front.AvailabilityChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public abstract string Name { get; }

    public abstract string About { get; }

    public ToolModelsState State
        => !_front.RunsHere ? ToolModelsState.Unavailable
         : _front.IsReady ? ToolModelsState.Ready
         : _download is { IsCompleted: false } ? ToolModelsState.Downloading
         : ToolModelsState.NotDownloaded;

    /// <summary>Every file the tool needs here: its models, and the CUDA pack where it suits the machine.</summary>
    public IReadOnlyList<ModelAsset> Files => [.. _front.Catalog?.Files ?? [], .. _front.Runtime?.Files ?? []];

    public IReadOnlyList<ModelAsset> OwnFiles => _front.Catalog?.Files ?? [];

    public long DownloadBytes => Files.Where(f => _store.Locate(f) is null).Sum(f => f.Size);

    public int DownloadPercent => _percent;

    public event EventHandler? Changed;

    /// <summary>Names the models: what one set made is not what another makes, so whatever keeps a result keys it by this.</summary>
    public string ModelsVersion => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", OwnFiles.Select(f => f.Sha256)))))[..16];

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
            activity?.Fail(ex.Message);   // the tool stays to be downloaded; the person can ask again
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Progress reported at once, on the reporting thread.</summary>
    protected sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
