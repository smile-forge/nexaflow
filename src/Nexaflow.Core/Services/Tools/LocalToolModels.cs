using System.Security.Cryptography;
using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
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
    private bool _downloading;
    private Task _fetching = Task.CompletedTask;
    private int _percent;

    private string? _failure;

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
         : _downloading ? ToolModelsState.Downloading
         : ToolModelsState.NotDownloaded;

    /// <summary>Every file the tool needs here: its models, and the CUDA pack where it suits the machine.</summary>
    public IReadOnlyList<ModelAsset> Files => [.. _front.Catalog?.Files ?? [], .. _front.Runtime?.Files ?? []];

    public IReadOnlyList<ModelAsset> OwnFiles => _front.Catalog?.Files ?? [];

    public long DownloadBytes => Files.Where(f => _store.Locate(f) is null).Sum(f => f.Size);

    public int DownloadPercent => _percent;

    public string? DownloadFailure => _failure;

    public event EventHandler? Changed;

    /// <summary>Names the models: what one set made is not what another makes, so whatever keeps a result keys it by this.</summary>
    public string ModelsVersion => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", OwnFiles.Select(f => f.Sha256)))))[..16];

    public void Download()
    {
        lock (_gate)
        {
            if (State is not ToolModelsState.NotDownloaded) return;
            _percent = 0;
            _failure = null;
            _downloading = true;
            _fetching = FetchAsync();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Waits until the tool is here, starting its download where none is under way — for a tool the app runs on the
    /// person's behalf as part of other work, such as a check every picture made passes, where asking for that work is
    /// asking for the tool.
    /// </summary>
    /// <exception cref="InvalidOperationException">It does not run on this machine, or its download failed.</exception>
    protected async Task ReadyAsync(CancellationToken ct)
    {
        if (State is ToolModelsState.Ready) return;
        if (State is ToolModelsState.Unavailable) throw new InvalidOperationException($"{Name} does not run on this machine.");
        Download();
        Task fetching;
        lock (_gate) fetching = _fetching;
        await fetching.WaitAsync(ct);
        if (State is not ToolModelsState.Ready)
            throw new InvalidOperationException(DownloadFailure is { } why ? $"{Name} could not be downloaded: {why}" : $"{Name} is not on this machine.");
    }

    private async Task FetchAsync()
    {
        await Task.Yield();   // the caller has said it is coming before anything reports
        var activity = _activities?.StartActivity(Str.Format("Shell.Config.Local.DownloadingFormat", Name, 0));
        string? failure = null;
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
            failure = ex.Message;
            activity?.Fail(ex.Message);   // the tool stays to be downloaded, with why; the person can ask again
        }
        finally
        {
            // Over before it is said to be: whatever reads the state on hearing of it reads where the download ended.
            lock (_gate)
            {
                _downloading = false;
                _failure = failure;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// One of the tool's jobs in the AI host, <paramref name="run"/>, asked once more where the host is lost while it works — on
    /// the fresh host the next request starts — so a host stopped for reasons of its own moment (killed for going quiet during a
    /// cold start, a driver's crash) costs the person nothing. Only for a job that can be asked again whole: one that hands
    /// nothing over until it ends. What it then fails with is a <see cref="ToolRunException"/> saying whether asking again can help.
    /// </summary>
    protected static async Task<T> AskedAsync<T>(Func<Task<T>> run, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await run();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && FailureOf(ex) is { } failure)
            {
                if (failure == ToolRunFailure.Interrupted && attempt == 1) continue;
                throw Told(failure, ex);
            }
        }
    }

    /// <summary><paramref name="run"/>, a job that hands over what it makes as it goes and so is never asked again whole, its
    /// failure told as a <see cref="ToolRunException"/> saying whether asking again can help.</summary>
    protected static async Task<T> ToldAsync<T>(Func<Task<T>> run, CancellationToken ct)
    {
        try
        {
            return await run();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && FailureOf(ex) is { } failure)
        {
            throw Told(failure, ex);
        }
    }

    private static ToolRunException Told(ToolRunFailure failure, Exception ex)
        => ex as ToolRunException
        ?? new ToolRunException(failure, failure == ToolRunFailure.Interrupted ? Str.Format("Shell.Tools.StoppedTwice", ex.Message) : ex.Message, ex);

    /// <summary>
    /// How a job failed, where the AI host or the models' runtime says: the host lost while it worked; refused for want of room,
    /// or no host to be had; or the job run and failed. Null for anything else — the picture handed over, a file — which the
    /// caller tells itself.
    /// </summary>
    private static ToolRunFailure? FailureOf(Exception ex)
    {
        if (ex is ToolRunException told) return told.Failure;
        if ((ex as AiHostException ?? ex.InnerException as AiHostException) is { } host)
            return host.Failure switch
            {
                AiHostFailure.Lost => ToolRunFailure.Interrupted,
                AiHostFailure.Unavailable => ToolRunFailure.NotNow,
                _ => host.Kind is HostErrorKind.Refused ? ToolRunFailure.NotNow : ToolRunFailure.Unworkable,
            };
        // The runtime's own failures are of placing the models: no room beside the others, or files not here.
        return (ex as LlmProviderException ?? ex.InnerException as LlmProviderException) is not null ? ToolRunFailure.NotNow : null;
    }

    /// <summary>Progress reported at once, on the reporting thread.</summary>
    protected sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
