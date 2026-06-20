using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// Downloads and caches Whisper ggml model files under
/// <c>%AppData%\Smile\nexaflow\voice\models</c>. Downloads run in the background
/// and stream to a <c>.tmp</c> file that is atomically renamed on success, so a
/// half-finished download never looks "ready".
/// </summary>
public sealed class WhisperModelManager
{
    public static WhisperModelManager Instance { get; } = new();
    private WhisperModelManager() { }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>The app UI dispatcher, captured when the singleton is built on the UI thread (App.InitializeApp).</summary>
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    private IBackgroundActivityManager? _activity;
    private int _downloading;   // interlocked guard against concurrent downloads

    public string ModelsDir { get; } = Path.Combine(ConfigManager.Instance.BaseDir, "voice", "models");

    /// <summary>Raised on the UI thread when a model becomes (un)available.</summary>
    public event EventHandler? ModelReadyChanged;

    /// <summary>Raised on the UI thread when a download starts, advances, or finishes.</summary>
    public event EventHandler? DownloadProgressChanged;

    /// <summary>Raised on the UI thread only after a fresh download succeeds (not when already present).</summary>
    public event EventHandler? ModelDownloadCompleted;

    /// <summary>Raised on the UI thread when a download fails, carrying the error message.</summary>
    public event EventHandler<string>? ModelDownloadFailed;

    /// <summary>True while a model download is in progress.</summary>
    public bool IsDownloading => Volatile.Read(ref _downloading) == 1;

    /// <summary>Download progress (0–100) of the in-flight download; 0 when none is active.</summary>
    public int DownloadPercent { get; private set; }

    public void Initialize(IBackgroundActivityManager activity) => _activity = activity;

    public string GetModelPath(VoiceConfig cfg) =>
        Path.Combine(ModelsDir, WhisperModelCatalog.FileName(cfg));

    public bool IsModelReady(VoiceConfig cfg)
    {
        var path = GetModelPath(cfg);
        return File.Exists(path) && new FileInfo(path).Length > 1_000_000;   // sanity: >1 MB
    }

    /// <summary>Fire-and-forget background download if the model isn't already present.</summary>
    public void EnsureModelDownloaded(VoiceConfig cfg)
        => _ = EnsureModelDownloadedAsync(cfg, CancellationToken.None);

    public async Task EnsureModelDownloadedAsync(VoiceConfig cfg, CancellationToken ct)
    {
        if (IsModelReady(cfg)) { RaiseReadyChanged(); return; }
        if (Interlocked.Exchange(ref _downloading, 1) == 1) return;   // one at a time

        var file   = WhisperModelCatalog.FileName(cfg);
        var dest   = GetModelPath(cfg);
        var tmp    = dest + ".tmp";
        var handle = _activity?.StartActivity($"Downloading voice model {file}…");

        DownloadPercent = 0;
        RaiseProgressChanged();   // flip the UI to a "downloading" state

        try
        {
            Directory.CreateDirectory(ModelsDir);

            using (var resp = await Http.GetAsync(WhisperModelCatalog.DownloadUrl(cfg),
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;

                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    copied += read;
                    if (total > 0) SetDownloadPercent((int)(copied * 100 / total));
                }
            }

            File.Move(tmp, dest, overwrite: true);
            SetDownloadPercent(100);
            handle?.Complete();
            RaiseReadyChanged();
            RaiseOnUi(ModelDownloadCompleted);
        }
        catch (Exception ex)
        {
            handle?.Fail(ex.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            DispatchToUi(() => ModelDownloadFailed?.Invoke(this, ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _downloading, 0);
            RaiseProgressChanged();   // download finished (ready or failed) — refresh status
        }
    }

    private void SetDownloadPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent == DownloadPercent) return;
        DownloadPercent = percent;
        RaiseProgressChanged();
    }

    private void RaiseReadyChanged()     => RaiseOnUi(ModelReadyChanged);
    private void RaiseProgressChanged()  => RaiseOnUi(DownloadProgressChanged);

    private void RaiseOnUi(EventHandler? handler)
    {
        if (handler is null) return;
        DispatchToUi(() => handler.Invoke(this, EventArgs.Empty));
    }

    private void DispatchToUi(Action raise)
    {
        if (_ui.CheckAccess()) raise();
        else _ui.Invoke(raise);
    }
}
