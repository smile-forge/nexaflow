using System.IO;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;

using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// The CUDA pack's download, app-wide. Begun at launch where the AI host's store holds an earlier pack: the person had the pack, so
/// a version that moves it on fetches it in place of theirs — the host taking across every file the two share, and downloading only
/// what changed. Begun too from the Local page, where tools whose own files are here wait on it alone. The host deletes the earlier
/// packs itself, once the pack is whole or where the machine does not suit it.
/// </summary>
public sealed class CudaPackDownload
{
    private readonly IModelAssetStore _store;
    private readonly Func<bool> _hadEarlier;
    private readonly MachineCards _hardware;
    private readonly IBackgroundActivityManager? _activities;
    private readonly Lock _gate = new();
    private bool _downloading;
    private int _percent;
    private string? _failure;

    private Task _fetching = Task.CompletedTask;

    /// <param name="hadEarlier">Whether the host's store holds an earlier pack — read once it has been read from the host.</param>
    public CudaPackDownload(IModelAssetStore store, Func<bool> hadEarlier, MachineCards hardware, IBackgroundActivityManager? activities)
    {
        _store      = store;
        _hadEarlier = hadEarlier;
        _hardware   = hardware;
        _activities = activities;
        store.Changed += OnAssetChanged;
    }

    /// <summary>Whether the pack runs here: an NVIDIA GPU of its own, and a driver new enough for CUDA 13.</summary>
    public bool Applies => CudaRuntimePack.Applies(_hardware);

    /// <summary>Every file of the pack is in the host's store, verified.</summary>
    public bool IsHere => CudaRuntimePack.IsIn(_store);

    public bool IsDownloading
    {
        get { lock (_gate) return _downloading; }
    }

    public int Percent => _percent;

    /// <summary>Why the last download stopped short; null otherwise.</summary>
    public string? Failure
    {
        get { lock (_gate) return _failure; }
    }

    /// <summary>What a download fetches: the files not here, less those an earlier pack holds — they are moved across.</summary>
    public long DownloadBytes => _store.FetchBytes(CudaRuntimePack.Files);

    /// <summary>The pack arrived, went, or its download moved on. Raised on whichever thread made the change.</summary>
    public event EventHandler? Changed;

    /// <summary>Once the host's store has been read: an earlier pack there means the person had the pack, so this version's is
    /// fetched in its place, where it suits the machine. Ends when that has.</summary>
    public Task ReplaceEarlierAsync()
    {
        if (!_hadEarlier() || !Applies || IsHere) return Task.CompletedTask;
        Download();
        lock (_gate) return _fetching;
    }

    /// <summary>Fetches the pack where it suits the machine — joining the download already under way.</summary>
    public void Download()
    {
        lock (_gate)
        {
            if (_downloading || !Applies) return;
            _downloading = true;
            _percent     = 0;
            _failure     = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        var fetching = FetchAsync();
        lock (_gate) _fetching = fetching;
    }

    private async Task FetchAsync()
    {
        var name     = Str.Get("Shell.Components.CudaPack.Name");
        var activity = _activities?.StartActivity(Str.Format("Shell.Config.Local.DownloadingFormat", name, 0));
        string? failure = null;
        try
        {
            await _store.EnsureAllAsync(CudaRuntimePack.Files, new Inline(p =>
            {
                int percent = (int)(p.Fraction * 100);
                if (percent == _percent) return;   // said again only when it moves
                _percent = percent;
                activity?.Update(Str.Format("Shell.Config.Local.DownloadingFormat", name, percent));
                Changed?.Invoke(this, EventArgs.Empty);
            }));
            activity?.Complete();
        }
        catch (Exception ex)
        {
            failure = ModelRoomText.MessageOf(ex);
            activity?.Fail(failure);   // the earlier pack stays, with whatever was not yet taken from it; the next launch tries again
        }
        finally
        {
            // Over before it is said to be: whatever reads the state on hearing of it reads where the download ended.
            lock (_gate)
            {
                _downloading = false;
                _failure     = failure;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A file of the pack arrived or went — fetched by this download, or with a model or a tool that needs it.</summary>
    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (CudaRuntimePack.Files.Contains(asset)) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Progress told where it is reported — the download's own figures, read by whoever shows them.</summary>
    private sealed class Inline(Action<AssetProgress> report) : IProgress<AssetProgress>
    {
        public void Report(AssetProgress value) => report(value);
    }
}
