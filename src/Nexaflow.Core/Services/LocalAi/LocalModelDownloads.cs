using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// The downloads of models on this machine, app-wide: one for each model however many jobs and workspaces want it, going on
/// whether or not a page shows it, each saying how far it has got and how long it should take. A job's choice of a model not
/// here waits on its download, and is made good when it lands — the job keeping its model till then.
/// </summary>
public sealed class LocalModelDownloads
{
    private readonly ModelAssetStore _store;
    private readonly IBackgroundActivityManager? _activities;
    private readonly Action<Workspace, LocalSlot, ProviderModelPair> _use;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LocalDownload> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(Workspace, LocalSlot), LocalModelOption> _waiting = [];

    /// <param name="use">Makes a model a workspace's job's model — saved, and taken up by the running workspace.</param>
    public LocalModelDownloads(ModelAssetStore store, IBackgroundActivityManager? activities, Action<Workspace, LocalSlot, ProviderModelPair> use)
    {
        _store      = store;
        _activities = activities;
        _use        = use;
    }

    /// <summary>A choice that waited on a download was made good: the workspace's job uses the model now. Raised on the thread
    /// the download ended on — the UI's, for one started there.</summary>
    public event Action<Workspace, LocalSlot, ProviderModelPair>? Chose;

    /// <summary>The download under way for <paramref name="option"/>, if any.</summary>
    public LocalDownload? Of(LocalModelOption option)
    {
        lock (_gate) return _running.GetValueOrDefault(Key(option));
    }

    /// <summary>The model a workspace's job is waiting on, if any.</summary>
    public LocalModelOption? WaitingFor(Workspace workspace, LocalSlot slot)
    {
        lock (_gate) return _waiting.GetValueOrDefault((workspace, slot));
    }

    /// <summary>Downloads what <paramref name="option"/> needs — joining the download already under way for it.</summary>
    public LocalDownload Fetch(LocalModelOption option)
    {
        lock (_gate)
        {
            if (_running.TryGetValue(Key(option), out var running)) return running;
            var download = new LocalDownload(option, option.Needs.Where(f => _store.Locate(f) is null).Sum(f => f.Size));
            _running[Key(option)] = download;
            download.Run(RunAsync(download));
            return download;
        }
    }

    /// <summary>Has <paramref name="workspace"/>'s <paramref name="slot"/> use <paramref name="option"/> once it is here: a later
    /// choice for the job takes this one's place.</summary>
    public LocalDownload Choose(Workspace workspace, LocalSlot slot, LocalModelOption option)
    {
        lock (_gate) _waiting[(workspace, slot)] = option;
        var download = Fetch(option);
        _ = MakeGoodAsync(workspace, slot, option, download);
        return download;
    }

    /// <summary>Forgets a job's choice that waits on a download — the job chose a model that is here instead.</summary>
    public void Forget(Workspace workspace, LocalSlot slot)
    {
        lock (_gate) _waiting.Remove((workspace, slot));
    }

    private async Task MakeGoodAsync(Workspace workspace, LocalSlot slot, LocalModelOption option, LocalDownload download)
    {
        var landed = await download.Done;
        lock (_gate)
        {
            if (_waiting.GetValueOrDefault((workspace, slot)) != option) return;   // chosen otherwise since
            _waiting.Remove((workspace, slot));
        }
        if (!landed) return;
        var column = option.Column();
        _use(workspace, slot, column);
        Chose?.Invoke(workspace, slot, column);
    }

    private async Task<bool> RunAsync(LocalDownload download)
    {
        var name     = download.Option.Model.DisplayName;
        var activity = _activities?.StartActivity(Str.Format("Shell.Config.Local.DownloadingFormat", name, 0));
        try
        {
            await _store.EnsureAllAsync([.. download.Option.Needs], new Inline(p =>
            {
                download.Report(p);
                activity?.Update(Str.Format("Shell.Config.Local.DownloadingFormat", name, download.Percent));
            }));
            activity?.Complete();
            download.Finish(null);
            return true;
        }
        catch (Exception ex)
        {
            activity?.Fail(ex.Message);
            download.Finish(ex.Message);
            return false;
        }
        finally
        {
            lock (_gate) _running.Remove(Key(download.Option));
        }
    }

    private static string Key(LocalModelOption option) => $"{option.ProviderName}|{option.Id}";

    /// <summary>Progress told where it is reported — the download's own figures, read by whoever shows them.</summary>
    private sealed class Inline(Action<AssetProgress> report) : IProgress<AssetProgress>
    {
        public void Report(AssetProgress value) => report(value);
    }
}

/// <summary>
/// One model's download: how much of it has come and how much is left, and — from how fast it has come over the last few
/// seconds — about how long the rest will take.
/// </summary>
public sealed partial class LocalDownload : ObservableObject
{
    /// <summary>How far back the speed is read: long enough to even out a burst, short enough to follow a change.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(15);

    private readonly Queue<(DateTime At, long Received)> _samples = new();
    private readonly TimeProvider _clock;
    private Task<bool> _done = Task.FromResult(false);

    public LocalDownload(LocalModelOption option, long total, TimeProvider? clock = null)
    {
        Option = option;
        Total  = total;
        _clock = clock ?? TimeProvider.System;
    }

    public LocalModelOption Option { get; }

    /// <summary>What it downloads, in bytes.</summary>
    public long Total { get; private set; }

    /// <summary>Whether it ends with everything here; false when it fails.</summary>
    public Task<bool> Done => _done;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent))]
    private long _received;

    /// <summary>About how long the rest will take; null until it has come for a moment.</summary>
    [ObservableProperty] private TimeSpan? _left;

    [ObservableProperty] private bool _isRunning = true;

    /// <summary>Why it stopped short; null otherwise.</summary>
    [ObservableProperty] private string? _failure;

    public int Percent => Total > 0 ? (int)(Received * 100 / Total) : 0;

    internal void Run(Task<bool> running) => _done = running;

    /// <summary>Takes a report of how far it has got, and reads the speed over the last few seconds from it.</summary>
    public void Report(AssetProgress progress)
    {
        if (progress.Total > 0) Total = progress.Total;
        var now = _clock.GetUtcNow().UtcDateTime;
        _samples.Enqueue((now, progress.Received));
        while (_samples.Count > 2 && now - _samples.Peek().At > Window) _samples.Dequeue();
        Received = progress.Received;

        var (since, from) = _samples.Peek();
        var seconds = (now - since).TotalSeconds;
        Left = seconds >= 1 && progress.Received > from
            ? TimeSpan.FromSeconds((Total - progress.Received) / ((progress.Received - from) / seconds))
            : null;
    }

    internal void Finish(string? failure)
    {
        Failure   = failure;
        IsRunning = false;
        Left      = null;
    }

    /// <summary>About how long is left, as it is said: "about 40 s left", "about 3 min left", "about 1 h 5 min left".</summary>
    public static string? Say(TimeSpan? left) => left switch
    {
        null                                => null,
        { TotalSeconds: < 60 } s            => Str.Format("Shell.Config.Local.LeftSeconds", Math.Max(1, (int)Math.Ceiling(s.TotalSeconds))),
        { TotalMinutes: < 60 } m            => Str.Format("Shell.Config.Local.LeftMinutes", (int)Math.Ceiling(m.TotalMinutes)),
        { } h                               => Str.Format("Shell.Config.Local.LeftHours", (int)h.TotalHours, h.Minutes),
    };
}
