using System.IO;
using Nexaflow.AiHost.Client;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// The CUDA pack's download, app-wide. Begun at launch where an earlier pack is here: the person had the pack, so a version
/// that moves it on fetches it in place of theirs — the store taking across every file the two share, and downloading only
/// what changed. Begun too from the Local page, where tools whose own files are here wait on it alone. Once the pack is
/// whole the earlier packs are deleted, however it came; on a machine it does not suit, an earlier one is deleted at launch,
/// since nothing will read it.
/// </summary>
public sealed class CudaPackDownload
{
    private readonly ModelAssetStore _store;
    private readonly string _storeRoot;
    private readonly ComputeHardwareReport _hardware;
    private readonly IBackgroundActivityManager? _activities;
    private readonly Lock _gate = new();
    private readonly Lock _deleting = new();
    private bool _downloading;
    private int _percent;
    private string? _failure;

    private Task _fetching = Task.CompletedTask;

    /// <param name="storeRoot">The folder <paramref name="store"/> keeps its files under, where earlier packs are found.</param>
    public CudaPackDownload(ModelAssetStore store, string storeRoot, ComputeHardwareReport hardware, IBackgroundActivityManager? activities)
    {
        _store      = store;
        _storeRoot  = storeRoot;
        _hardware   = hardware;
        _activities = activities;
        store.Changed += OnAssetChanged;
    }

    /// <summary>Whether the pack runs here: an NVIDIA GPU of its own, and a driver new enough for CUDA 13.</summary>
    public bool Applies => CudaRuntimePack.Applies(_hardware);

    /// <summary>Every file of the pack is here and verified.</summary>
    public bool IsHere => CudaRuntimePack.Locate(_store) is not null;

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

    /// <summary>At launch: an earlier pack here means the person had the pack, so this version's is fetched in its place —
    /// or, on a machine the pack does not suit, the earlier one is only deleted. Ends when that has.</summary>
    public Task ReplaceEarlierAsync()
    {
        if (CudaRuntimePack.EarlierIn(_storeRoot).Count == 0) return Task.CompletedTask;
        if (!Applies) return Task.Run(DeleteEarlier);
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
            DeleteEarlier();   // a pack already whole raises no change of its files, so its earlier ones go here
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            activity?.Fail(ex.Message);   // the earlier pack stays, with whatever was not yet taken from it; the next launch tries again
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

    /// <summary>A file of the pack arrived or went — fetched by this download, or with a model or a tool that needs it. The
    /// pack whole, the earlier ones go, off the thread the store told it on.</summary>
    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (!CudaRuntimePack.Files.Contains(asset)) return;
        if (IsHere) _ = Task.Run(DeleteEarlier);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes the earlier packs. One still held open — by an earlier Nexaflow running from it — stays, and the next
    /// launch tries again.</summary>
    private void DeleteEarlier()
    {
        lock (_deleting)
        {
            foreach (var folder in CudaRuntimePack.EarlierIn(_storeRoot))
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Progress told where it is reported — the download's own figures, read by whoever shows them.</summary>
    private sealed class Inline(Action<AssetProgress> report) : IProgress<AssetProgress>
    {
        public void Report(AssetProgress value) => report(value);
    }
}
