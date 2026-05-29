using System.IO;
using System.Net.Http;
using System.Windows;
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

    private IBackgroundActivityManager? _activity;
    private int _downloading;   // interlocked guard against concurrent downloads

    public string ModelsDir { get; } = Path.Combine(ConfigManager.Instance.BaseDir, "voice", "models");

    /// <summary>Raised on the UI thread when a model becomes (un)available.</summary>
    public event EventHandler? ModelReadyChanged;

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

        try
        {
            Directory.CreateDirectory(ModelsDir);

            using (var resp = await Http.GetAsync(WhisperModelCatalog.DownloadUrl(cfg),
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
                await src.CopyToAsync(dst, ct);
            }

            File.Move(tmp, dest, overwrite: true);
            handle?.Complete();
            RaiseReadyChanged();
        }
        catch (Exception ex)
        {
            handle?.Fail(ex.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
        finally
        {
            Interlocked.Exchange(ref _downloading, 0);
        }
    }

    private void RaiseReadyChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        Action raise = () => ModelReadyChanged?.Invoke(this, EventArgs.Empty);
        if (dispatcher is null || dispatcher.CheckAccess()) raise();
        else dispatcher.Invoke(raise);
    }
}
