using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.Catalog;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;

namespace Nexaflow.Providers.Local;

/// <summary>
/// Downloads a variant's GGUF file(s) into <c>{modelsDir}\{id}\</c>, reporting progress through the
/// shell's <see cref="IBackgroundActivityManager"/>. Mirrors the WhisperModelManager pattern: stream to
/// a <c>.tmp</c> file then atomic-move, so a partial download never looks complete. Single-flight per
/// model id. (<see cref="IShellServices"/> is unreachable from a provider, so this is how a local model
/// is fetched "in the background" — the download runs off the UI thread and shows in the activity area.)
/// </summary>
public static class LocalModelDownloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(6) };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    /// <summary>True when every file for the variant exists locally AND looks like a real GGUF.
    /// A truncated/HTML/LFS-pointer file counts as "not present" so it gets re-downloaded.</summary>
    public static bool IsPresent(LocalModelVariant v, string modelsDir)
    {
        if (v.Files.Count == 0) return false;
        var dir = Path.Combine(modelsDir, v.Id);
        return v.Files.All(f => LooksLikeGguf(Path.Combine(dir, f)));
    }

    /// <summary>Cheap sanity check: the file exists and starts with the GGUF magic bytes.</summary>
    private static bool LooksLikeGguf(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            Span<byte> magic = stackalloc byte[4];
            return fs.Read(magic) == 4 && magic is [(byte)'G', (byte)'G', (byte)'U', (byte)'F'];
        }
        catch { return false; }
    }

    /// <summary>The file llama.cpp is pointed at (first shard).</summary>
    public static string PrimaryPath(LocalModelVariant v, string modelsDir)
        => Path.Combine(modelsDir, v.Id, v.PrimaryFile);

    /// <summary>Ensures all of the variant's files are present, downloading any that are missing.
    /// Returns the primary file path. Throws on failure.</summary>
    public static async Task<string> EnsureAsync(
        LocalModelVariant v, string modelsDir, IBackgroundActivityManager activity, CancellationToken ct)
    {
        if (IsPresent(v, modelsDir)) return PrimaryPath(v, modelsDir);

        var gate = Locks.GetOrAdd(v.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (IsPresent(v, modelsDir)) return PrimaryPath(v, modelsDir);

            var dir = Path.Combine(modelsDir, v.Id);
            Directory.CreateDirectory(dir);

            var handle = activity.StartActivity($"Downloading {v.Display}…");
            try
            {
                foreach (var file in v.Files)
                {
                    ct.ThrowIfCancellationRequested();
                    var dest = Path.Combine(dir, file);
                    if (File.Exists(dest)) continue;
                    await DownloadFileAsync(v.DownloadUrlFor(file), dest, ct);
                }
                handle.Complete();
                return PrimaryPath(v, modelsDir);
            }
            catch (Exception ex)
            {
                handle.Fail(ex.Message);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    private static async Task DownloadFileAsync(string url, string dest, CancellationToken ct)
    {
        var tmp = dest + ".tmp";
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                throw new LlmProviderException(
                    $"Download failed: {(int)resp.StatusCode} {resp.ReasonPhrase} for {url}. " +
                    "Check the repo and exact GGUF file name in catalog.json — open the repo's \"Files\" tab on huggingface.co to confirm the filename (unsloth varies casing and uses '-UD-' for some MoE quants).");

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                await src.CopyToAsync(dst, ct);

            if (!LooksLikeGguf(tmp))
                throw new LlmProviderException(
                    $"The file downloaded from {url} is not a valid GGUF (got {new FileInfo(tmp).Length:N0} bytes). " +
                    "The URL likely returned an error page or a Git-LFS pointer — verify the repo and exact filename in catalog.json.");

            File.Move(tmp, dest, overwrite: true);   // atomic — a partial .tmp never looks ready
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }
}
