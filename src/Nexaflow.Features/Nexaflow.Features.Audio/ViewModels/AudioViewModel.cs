using Nexaflow.Visuals.Common.Formatting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Audio.Controls;
using Nexaflow.Features.Audio.Models;
using Nexaflow.Features.Audio.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Features.Audio.ViewModels;

/// <summary>Which slide-out panel the side drawer shows.</summary>
public enum AudioPanel { Tags, Lyrics }

/// <summary>One entry in the playlist drawer.</summary>
public partial class PlaylistItemViewModel : ObservableObject
{
    public PlaylistItemViewModel(string path)
    {
        Path = path;
        Display = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    public string Path { get; }
    public string Display { get; }

    /// <summary>True for the track currently loaded/playing — highlighted in the list.</summary>
    [ObservableProperty] private bool _isCurrent;
}

/// <summary>
/// The audio player tab. Owns the playback engine (created lazily — no audio device is touched until
/// the user plays), a folder queue with next/previous/auto-advance, the spectrum + waveform visual
/// feed, timed lyrics, and the tag editor. A render timer (~30 fps) pulls position + FFT bands while
/// playing. Leaving the tab pauses playback (resumed on return); the engine + timer are torn down when
/// the tab closes.
/// </summary>
public sealed partial class AudioViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private const int WaveformBuckets = 800;
    private static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    private readonly List<string> _paths;
    private readonly IShellServices _shell;
    private readonly AudioConfig _config;
    private readonly DispatcherTimer _renderTimer;

    private AudioPlaybackEngine? _engine;
    private string? _engineLoadedPath;
    private int _index;
    private int _loadToken;
    private bool _disposed;
    private bool _resumeOnActivate;
    private bool _autoPlayPending;

    // Background play: while the tab is hidden with Background play on, the engine keeps running and a
    // transport control is hosted in the shell chrome; _mediatedHandle removes it when we retake the tab.
    private bool _backgrounded;
    private IDisposable? _mediatedHandle;

    public AudioViewModel(IReadOnlyList<string> paths, int startIndex, IShellServices shell, AudioConfig config,
                          bool autoPlay = false)
    {
        _shell = shell;
        _config = config;
        _paths = paths.Where(AudioFileTypes.IsAudio).ToList();
        _index = _paths.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _paths.Count - 1);
        _volume = config.Volume;
        _backgroundPlay = config.BackgroundPlay;
        _autoPlayPending = autoPlay && _paths.Count > 0;

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _renderTimer.Tick += (_, _) => OnRenderTick();

        BuildPlaylist();
    }

    public LyricsViewModel Lyrics { get; } = new();

    // ── Playlist (left drawer; only shown when more than one track) ────────────

    public ObservableCollection<PlaylistItemViewModel> Playlist { get; } = [];

    public bool HasPlaylist => _paths.Count > 1;

    [ObservableProperty] private bool _isPlaylistOpen;

    /// <summary>Opens/closes the left playlist drawer (animated in the view).</summary>
    [RelayCommand]
    private void TogglePlaylist() => IsPlaylistOpen = !IsPlaylistOpen;

    /// <summary>Jumps to and plays the track at <paramref name="index"/> (playlist double-click).</summary>
    public Task PlayAtAsync(int index)
    {
        if (index < 0 || index >= _paths.Count) return Task.CompletedTask;
        return SwitchToAsync(index, keepPlaying: true);
    }

    /// <summary>Reorders the queue (playlist drag), keeping the currently-loaded track current.</summary>
    public void MovePlaylistItem(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _paths.Count || to >= _paths.Count || from == to) return;

        var current = CurrentPath;
        var path = _paths[from];
        _paths.RemoveAt(from);
        _paths.Insert(to, path);
        Playlist.Move(from, to);

        if (current is not null) _index = _paths.IndexOf(current);
        NotifyQueue();
    }

    private void BuildPlaylist()
    {
        Playlist.Clear();
        foreach (var p in _paths) Playlist.Add(new PlaylistItemViewModel(p));
        UpdatePlaylistCurrent();
    }

    private void UpdatePlaylistCurrent()
    {
        for (int i = 0; i < Playlist.Count; i++) Playlist[i].IsCurrent = i == _index;
    }

    // ── Side drawer (Tags / Lyrics) ──────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTagsPanel))]
    [NotifyPropertyChangedFor(nameof(ShowLyricsPanel))]
    [NotifyPropertyChangedFor(nameof(IsTagsButtonActive))]
    [NotifyPropertyChangedFor(nameof(IsLyricsButtonActive))]
    private AudioPanel _activePanel = AudioPanel.Tags;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTagsButtonActive))]
    [NotifyPropertyChangedFor(nameof(IsLyricsButtonActive))]
    private bool _isPanelOpen = true;

    public bool ShowTagsPanel => ActivePanel == AudioPanel.Tags;
    public bool ShowLyricsPanel => ActivePanel == AudioPanel.Lyrics;
    public bool IsTagsButtonActive => IsPanelOpen && ActivePanel == AudioPanel.Tags;
    public bool IsLyricsButtonActive => IsPanelOpen && ActivePanel == AudioPanel.Lyrics;

    /// <summary>
    /// Toggles the side drawer: clicking the active panel's button closes it; clicking the other
    /// button (or any button while closed) opens that panel. Closing/opening animates in the view;
    /// switching between panels while open is instant.
    /// </summary>
    [RelayCommand]
    private void TogglePanel(string which)
    {
        var requested = string.Equals(which, "Lyrics", StringComparison.OrdinalIgnoreCase)
            ? AudioPanel.Lyrics : AudioPanel.Tags;

        if (IsPanelOpen && ActivePanel == requested)
            IsPanelOpen = false;
        else
        {
            ActivePanel = requested;
            IsPanelOpen = true;
        }
    }

    // ── Now-playing display ──────────────────────────────────────────────────

    [ObservableProperty][NotifyPropertyChangedFor(nameof(NowPlayingText))] private string _fileName = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(NowPlayingText))] private string _title = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(NowPlayingText))] private string _artist = string.Empty;
    [ObservableProperty] private string _album = string.Empty;
    [ObservableProperty] private BitmapSource? _albumArt;
    [ObservableProperty] private TagEditorViewModel? _tagEditor;

    /// <summary>Artist + title (or filename) for the now-playing line; used as the chrome remote's tooltip and
    /// stays fresh across background auto-advance since it recomputes from <see cref="Title"/>/<see cref="Artist"/>.</summary>
    public string NowPlayingText
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Title) ? FileName : Title;
            if (string.IsNullOrWhiteSpace(name)) return "Audio player";
            return string.IsNullOrWhiteSpace(Artist) ? name : $"{Artist} — {name}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueueText))]
    [NotifyPropertyChangedFor(nameof(HasQueue))]
    private int _queueCount;

    public string QueueText => _paths.Count > 1 ? $"{_index + 1} / {_paths.Count}" : string.Empty;
    public bool HasQueue => _paths.Count > 1;

    // ── Transport state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private TimeSpan _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private TimeSpan _duration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    /// <summary>When on, leaving the tab hands playback to a chrome transport control instead of pausing.
    /// Persisted; has no effect while the page is active (the page owns playback).</summary>
    [ObservableProperty] private bool _backgroundPlay;

    [ObservableProperty] private double _volume;
    [ObservableProperty] private float[]? _spectrumBands;
    [ObservableProperty] private float[]? _waveformPeaks;

    public double ProgressFraction => Duration.TotalSeconds > 0
        ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0, 1)
        : 0;

    public string PositionText => FormatTime(Position);
    public string DurationText => FormatTime(Duration);
    public string PlayPauseGlyph => IsPlaying ? "⏸" : "▶";

    public bool HasNext => _index >= 0 && _index < _paths.Count - 1;
    public bool HasPrevious => _index > 0;

    private string? CurrentPath => _index >= 0 && _index < _paths.Count ? _paths[_index] : null;

    // ── Lifecycle (driven by the view) ───────────────────────────────────────

    /// <summary>First-load entry point — call from the view's <c>Loaded</c>. Honours "Play folder" auto-play.</summary>
    public async Task LoadAsync()
    {
        if (_autoPlayPending)
        {
            _autoPlayPending = false;
            StartPlayback();
        }
        await LoadCurrentAsync();
    }

    /// <summary>The tab became visible: reclaim playback from the chrome remote (or resume if it was paused by
    /// leaving), and restart the render loop.</summary>
    public void OnActivated()
    {
        // Background play: the engine never stopped while backgrounded, so play/pause state and position carry
        // over untouched — just drop the chrome remote and resume the render loop. Do NOT call Play().
        if (_mediatedHandle is not null)
        {
            _backgrounded = false;
            _mediatedHandle.Dispose();
            _mediatedHandle = null;
            StartRenderTimerIfVisible();
            return;
        }

        if (_resumeOnActivate && _engine is not null)
        {
            _resumeOnActivate = false;
            _engine.Play();
            IsPlaying = true;
        }
        StartRenderTimerIfVisible();
    }

    /// <summary>Window minimized/restored: suspend only the spectrum/waveform repaint — playback
    /// keeps running (minimizing while listening is normal); the render loop resumes with the window.</summary>
    public void SetRenderSuspended(bool suspended)
    {
        if (suspended) _renderTimer.Stop();
        else StartRenderTimerIfVisible();
    }

    /// <summary>The tab was hidden (tab switch). With Background play on and a track loaded, hand the engine to a
    /// transport control in the shell chrome — playback keeps running in whatever play/pause state it's in, so the
    /// remote can pause/resume/skip — instead of pausing. Otherwise pause playback to resume on return.</summary>
    public void OnDeactivated()
    {
        _renderTimer.Stop();

        if (BackgroundPlay && CurrentPath is not null)
        {
            _backgrounded = true;
            _mediatedHandle = _shell.RegisterMediatedTask(
                new MediatedTaskRegistration(NowPlayingText, () => new AudioMiniTransport { DataContext = this }));
            return;   // don't pause — the chrome remote drives play/pause/skip; the engine keeps its position
        }

        if (_engine is { IsPlaying: true })
        {
            _engine.Pause();
            IsPlaying = false;
            _resumeOnActivate = true;
        }
    }

    /// <summary>Starts the ~30 fps render loop only when the page is visible and playing — never while
    /// backgrounded (playback continues via the chrome remote, but the spectrum/lyrics repaint would be wasted).</summary>
    private void StartRenderTimerIfVisible()
    {
        if (!_backgrounded && IsPlaying) _renderTimer.Start();
    }

    /// <summary>Re-points the tab at a new queue/track (shell tab reuse). A no-op if the queue is unchanged,
    /// so re-selecting the tab never interrupts playback.</summary>
    public async Task ReinitializeAsync(IReadOnlyList<string> paths, int startIndex)
    {
        var list = paths.Where(AudioFileTypes.IsAudio).ToList();
        if (list.Count == 0) return;

        // The queue identifies the tab, so an unchanged queue is a no-op — re-selecting the tab re-pushes its
        // ORIGINAL params, and we must never rewind to the start track or interrupt playback. The user may have
        // skipped ahead since (including from the chrome remote while backgrounded), so ignore the frozen
        // startIndex here; a real jump arrives as a different queue. (Tab adoption requires an exact param match,
        // so a same-queue-different-index request never reaches here — it opens a fresh tab instead.)
        if (list.SequenceEqual(_paths)) return;

        _engine?.Release();
        _engineLoadedPath = null;
        _paths.Clear();
        _paths.AddRange(list);
        _index = Math.Clamp(startIndex, 0, _paths.Count - 1);
        IsPlaying = false;
        BuildPlaylist();
        if (!HasPlaylist) IsPlaylistOpen = false;
        await LoadCurrentAsync();
    }

    // ── Loading the current track's metadata / waveform / lyrics ─────────────

    private async Task LoadCurrentAsync()
    {
        if (CurrentPath is not { } path) return;

        int token = ++_loadToken;
        FileName = Path.GetFileName(path);
        Title = FileName;
        Artist = string.Empty;
        Album = string.Empty;
        AlbumArt = null;
        Position = TimeSpan.Zero;
        SpectrumBands = [];
        WaveformPeaks = [];
        NotifyQueue();

        var tags = await Task.Run(() => TagService.Read(path));
        if (token != _loadToken || _disposed) return;

        Title = string.IsNullOrWhiteSpace(tags.Title) ? FileName : tags.Title;
        Artist = tags.Artist;
        Album = tags.Album;
        Duration = tags.Duration;
        AlbumArt = ImageHelpers.ToBitmap(tags.AlbumArt);
        TagEditor = new TagEditorViewModel(tags, _shell, SaveTagsAsync);

        await LoadLyricsAsync(path, tags, token);
        _ = LoadWaveformAsync(path, token);
    }

    private async Task LoadLyricsAsync(string path, TrackTags tags, int token)
    {
        var lrc = Path.ChangeExtension(path, ".lrc");
        IReadOnlyList<LyricLine> lines;
        bool synced;

        if (File.Exists(lrc))
        {
            lines = await Task.Run(() => LrcParser.ParseFile(lrc));
            synced = true;
        }
        else if (!string.IsNullOrWhiteSpace(tags.Lyrics))
        {
            lines = tags.Lyrics.Split('\n')
                .Select(t => new LyricLine(TimeSpan.Zero, t.TrimEnd('\r')))
                .ToList();
            synced = false;
        }
        else
        {
            lines = [];
            synced = false;
        }

        if (token != _loadToken || _disposed) return;
        Lyrics.Load(lines, synced);
    }

    private async Task LoadWaveformAsync(string path, int token)
    {
        var peaks = await Task.Run(() => WaveformAnalyzer.Analyze(path, WaveformBuckets));
        if (token == _loadToken && !_disposed) WaveformPeaks = peaks;
    }

    // ── Transport commands ───────────────────────────────────────────────────

    [RelayCommand]
    private void PlayPause()
    {
        if (CurrentPath is null) return;

        if (_engine is { IsPlaying: true })
        {
            _engine.Pause();
            IsPlaying = false;
            _renderTimer.Stop();
            SpectrumBands = [];
        }
        else
        {
            StartPlayback();
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _engine?.Stop();
        IsPlaying = false;
        _renderTimer.Stop();
        Position = TimeSpan.Zero;
        SpectrumBands = [];
        Lyrics.UpdatePosition(TimeSpan.Zero);
    }

    [RelayCommand(CanExecute = nameof(HasNext))]
    private Task Next() => SwitchToAsync(_index + 1, IsPlaying);

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private Task Previous()
    {
        // Within the first few seconds, "previous" restarts the current track (familiar player behaviour).
        if (_engine is { } e && e.Position > RestartThreshold)
        {
            SeekToFraction(0);
            return Task.CompletedTask;
        }
        return SwitchToAsync(_index - 1, IsPlaying);
    }

    /// <summary>Seeks to a 0..1 position in the current track (waveform click / restart).</summary>
    public void SeekToFraction(double fraction)
    {
        if (CurrentPath is null || !EnsureEngineLoaded()) return;
        var target = TimeSpan.FromSeconds(Duration.TotalSeconds * Math.Clamp(fraction, 0, 1));
        _engine!.Seek(target);
        Position = target;
        Lyrics.UpdatePosition(target);
    }

    private void StartPlayback()
    {
        if (!EnsureEngineLoaded()) return;
        _engine!.Play();
        IsPlaying = true;
        StartRenderTimerIfVisible();
    }

    private async Task SwitchToAsync(int newIndex, bool keepPlaying)
    {
        if (newIndex < 0 || newIndex >= _paths.Count) return;

        _engine?.Release();
        _engineLoadedPath = null;
        SpectrumBands = [];
        WaveformPeaks = [];
        Position = TimeSpan.Zero;
        _index = newIndex;
        NotifyQueue();

        if (keepPlaying) StartPlayback();
        await LoadCurrentAsync();
    }

    private bool EnsureEngineLoaded()
    {
        if (CurrentPath is not { } path) return false;
        _engine ??= CreateEngine();

        if (_engineLoadedPath != path)
        {
            try
            {
                _engine.Load(path, _config.SpectrumBarCount);
                _engineLoadedPath = path;
            }
            catch (Exception ex)
            {
                _shell.ShowError($"Can't play {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }
        }
        return true;
    }

    private AudioPlaybackEngine CreateEngine()
    {
        var engine = new AudioPlaybackEngine { Volume = (float)Volume };
        engine.PlaybackEnded += OnEnginePlaybackEnded;
        return engine;
    }

    private void OnEnginePlaybackEnded()
    {
        // Fires on the output thread — marshal back before touching observable state.
        _ = _shell.RunOnUiAsync(() =>
        {
            if (_disposed) return;
            if (_config.AutoAdvance && HasNext)
            {
                _ = SwitchToAsync(_index + 1, keepPlaying: true);
            }
            else
            {
                IsPlaying = false;
                _renderTimer.Stop();
                Position = Duration;
                SpectrumBands = [];
            }
        });
    }

    // ── Tag save (orchestrates the engine release TagLib needs) ──────────────

    private async Task<bool> SaveTagsAsync(TrackTags tags, byte[]? art, bool replaceArt)
    {
        if (CurrentPath is not { } path) return false;

        bool wasPlaying = IsPlaying;
        var resumeAt = _engine?.Position ?? TimeSpan.Zero;

        // Release the file handle so TagLib can open it for writing.
        _engine?.Release();
        _engineLoadedPath = null;
        IsPlaying = false;
        _renderTimer.Stop();

        bool ok = await Task.Run(() => TagService.Save(path, tags, art, replaceArt));
        if (_disposed) return ok;

        if (ok)
        {
            _shell.ShowNotification("Tags saved.");
            await LoadCurrentAsync();
        }
        else
        {
            _shell.ShowError("Could not save tags.");
        }

        if (wasPlaying && EnsureEngineLoaded())
        {
            _engine!.Seek(resumeAt);
            _engine.Play();
            IsPlaying = true;
            StartRenderTimerIfVisible();
        }
        return ok;
    }

    // ── Render loop ──────────────────────────────────────────────────────────

    private void OnRenderTick()
    {
        if (_engine is null) return;

        Position = _engine.Position;
        if (_engine.Duration > TimeSpan.Zero && _engine.Duration != Duration)
            Duration = _engine.Duration;
        SpectrumBands = _engine.Bands;
        Lyrics.UpdatePosition(Position);
    }

    partial void OnVolumeChanged(double value)
    {
        if (_engine is not null) _engine.Volume = (float)value;
    }

    partial void OnBackgroundPlayChanged(bool value)
    {
        _config.BackgroundPlay = value;
        try { _shell.SaveFeatureConfig(_config); } catch { /* best-effort persistence */ }
    }

    private void NotifyQueue()
    {
        QueueCount = _paths.Count;
        OnPropertyChanged(nameof(QueueText));
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(HasPlaylist));
        OnPropertyChanged(nameof(HasNext));
        OnPropertyChanged(nameof(HasPrevious));
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        UpdatePlaylistCurrent();
    }

    private static string FormatTime(TimeSpan t) => DurationFormatter.FormatMediaTime(t);

    // ── IPageViewModel ───────────────────────────────────────────────────────

    public string GetContext()
    {
        if (CurrentPath is null) return "Audio player — no track loaded.";
        var state = IsPlaying ? "playing" : "paused";
        var name = string.IsNullOrWhiteSpace(Title) ? FileName : Title;
        var who = string.IsNullOrWhiteSpace(Artist) ? "" : $" by {Artist}";
        var queue = _paths.Count > 1 ? $" (track {_index + 1} of {_paths.Count})" : "";
        return $"Audio player — {state} \"{name}\"{who}{queue}.";
    }

    /// <summary>Scope boundary = the loaded track's path, so two pinned players on different files stay
    /// distinguishable; null when the queue is empty (nothing to act on).</summary>
    public string? GetSecurityContext() => CurrentPath;

    public string? GetAiSystemPromptGuidance() =>
        "An audio player tab is open. You cannot hear the audio — call read_now_playing to read the current "
      + "track, play state, position and queue, and control_playback / seek / next_track / previous_track to "
      + "drive playback. Report what the transport reports; never claim to have listened to the sound.";

    /// <summary>
    /// Client tools for the player. All are reversible view-state changes (play/pause/seek/skip) or pure
    /// reads, so every one is <see cref="ToolSafety.SafeOperation"/>. The AI can't hear the audio — these
    /// tools drive and report the transport, they don't "listen". Every tool that mutates UI-bound playback
    /// state marshals the change through <see cref="IShellServices.RunOnUiAsync(System.Action)"/> because
    /// client tools run off the UI thread and features must never touch the dispatcher.
    /// </summary>
    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Pure read: report transport state (no UI mutation, so no marshalling) ──
        new DelegateClientTool(
            "read_now_playing",
            "Report the audio player's state: playing/paused, the resolved track title and artist, "
          + "position and duration, and the track's place in the queue. Read-only — you cannot hear the audio.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(ReadNowPlaying()),
            parallelizable: true),

        // ── Transport control (reversible; marshalled to the UI thread) ──
        new DelegateClientTool(
            "control_playback",
            "Start, pause, toggle or stop playback of the current track.",
            [new ClientToolParameter("action", "One of: play, pause, toggle, stop.")],
            ToolSafety.SafeOperation,
            (args, _) => ControlPlaybackAsync(ToolArgs.Str(args, "action"))),

        new DelegateClientTool(
            "seek",
            "Seek the current track to a position given as whole seconds (e.g. 90) or mm:ss / h:mm:ss (e.g. 1:30).",
            [new ClientToolParameter("position", "Target position: seconds, or mm:ss.")],
            ToolSafety.SafeOperation,
            (args, _) => SeekAsync(ToolArgs.Str(args, "position"))),

        new DelegateClientTool(
            "next_track",
            "Advance to the next track in the queue (stops at the last one).",
            [],
            ToolSafety.SafeOperation,
            (_, _) => NavigateAsync(forward: true)),

        new DelegateClientTool(
            "previous_track",
            "Go back to the previous track in the queue (stops at the first one).",
            [],
            ToolSafety.SafeOperation,
            (_, _) => NavigateAsync(forward: false)),
    ];

    /// <summary>Resolved display name for the current track — the tag title, else the loaded file name,
    /// else the current path's basename (so a not-yet-loaded track still reads honestly, not blank).</summary>
    private string ResolvedName()
    {
        if (!string.IsNullOrWhiteSpace(Title)) return Title;
        if (!string.IsNullOrWhiteSpace(FileName)) return FileName;
        return CurrentPath is null ? "(no track)" : System.IO.Path.GetFileName(CurrentPath);
    }

    /// <summary>read_now_playing body — a pure read of transport state (never mutates, so never marshals).</summary>
    private ToolResult ReadNowPlaying()
    {
        if (CurrentPath is null)
            return ToolResult.Ok("no track", "No track is loaded in the audio player.");

        var name = ResolvedName();
        var lines = new List<string>
        {
            $"State: {(IsPlaying ? "playing" : "paused")}",
            $"Track: {name}",
            $"Artist: {(string.IsNullOrWhiteSpace(Artist) ? "unknown" : Artist)}",
        };
        if (!string.IsNullOrWhiteSpace(Album)) lines.Add($"Album: {Album}");
        lines.Add($"Position: {PositionText} / {DurationText}");
        lines.Add(_paths.Count > 1 ? $"Queue: track {_index + 1} of {_paths.Count}" : "Queue: single track");

        return ToolResult.Ok($"{(IsPlaying ? "playing" : "paused")} \"{name}\"{ArtistSuffix()}", string.Join("\n", lines));
    }

    /// <summary>control_playback body — sets/toggles playback through the existing transport methods. The state
    /// change touches UI-bound properties, so it is marshalled to the UI thread; errors when nothing is loaded.</summary>
    private async Task<ToolResult> ControlPlaybackAsync(string? action)
    {
        var verb = action?.ToLowerInvariant();
        if (verb is not ("play" or "pause" or "toggle" or "stop"))
            return ToolResult.Error("invalid action",
                $"Unknown action '{action}'. Use one of: play, pause, toggle, stop.");

        if (CurrentPath is null)
            return ToolResult.Error("no track", "No track is loaded, so there is nothing to control.");

        await _shell.RunOnUiAsync(() =>
        {
            switch (verb)
            {
                case "play":   if (!IsPlaying) StartPlayback(); break;
                case "pause":  if (IsPlaying)  PlayPause();     break;   // PlayPause pauses when currently playing
                case "toggle": PlayPause();                     break;
                case "stop":   Stop();                          break;
            }
        });

        var state = IsPlaying ? "playing" : verb == "stop" ? "stopped" : "paused";
        return ToolResult.Ok(state, $"Playback {state} — \"{ResolvedName()}\" at {PositionText} / {DurationText}.");
    }

    /// <summary>seek body — parses a seconds/mm:ss position and seeks the engine. UI-bound (Position/lyrics), so
    /// the seek is marshalled; errors when nothing is loaded or the position can't be parsed.</summary>
    private async Task<ToolResult> SeekAsync(string? positionText)
    {
        if (CurrentPath is null)
            return ToolResult.Error("no track", "No track is loaded to seek.");

        if (ParsePosition(positionText) is not { } target)
            return ToolResult.Error("invalid position",
                $"Couldn't read a position from '{positionText}'. Give whole seconds (e.g. 90) or mm:ss (e.g. 1:30).");

        bool ok = false;
        await _shell.RunOnUiAsync(() =>
        {
            if (!EnsureEngineLoaded()) return;
            _engine!.Seek(target);
            Position = _engine.Position;
            Lyrics.UpdatePosition(Position);
            ok = true;
        });

        return ok
            ? ToolResult.Ok($"seeked to {PositionText}", $"Seeked to {PositionText} of {DurationText}.")
            : ToolResult.Error("couldn't seek", "The track couldn't be loaded to seek to that position.");
    }

    /// <summary>next_track / previous_track body — steps the queue through the existing Next/Previous logic
    /// (which changes CurrentPath and reloads UI-bound metadata), marshalled to the UI thread; no-ops at the ends.</summary>
    private async Task<ToolResult> NavigateAsync(bool forward)
    {
        if (CurrentPath is null)
            return ToolResult.Error("no track", "No track is loaded to navigate.");

        if (forward ? !HasNext : !HasPrevious)
            return ToolResult.Ok(
                forward ? "already at the last track" : "already at the first track",
                $"Already at the {(forward ? "last" : "first")} track ({_index + 1} of {_paths.Count}).");

        Task? nav = null;
        await _shell.RunOnUiAsync(() => nav = forward ? Next() : Previous());
        if (nav is not null) await nav;

        return ToolResult.Ok(
            $"track {_index + 1} of {_paths.Count}",
            $"Now on \"{ResolvedName()}\"{ArtistSuffix()} — track {_index + 1} of {_paths.Count}.");
    }

    /// <summary>" by {Artist}" when an artist is known, else empty — for one-line now-playing summaries.</summary>
    private string ArtistSuffix() => string.IsNullOrWhiteSpace(Artist) ? "" : $" by {Artist}";

    /// <summary>Parses a seek position: whole/fractional seconds, or mm:ss / h:mm:ss. Null if unparseable.</summary>
    private static TimeSpan? ParsePosition(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();

        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length is < 2 or > 3) return null;
            double total = 0;
            foreach (var part in parts)
            {
                if (!double.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) || v < 0)
                    return null;
                total = total * 60 + v;
            }
            return TimeSpan.FromSeconds(total);
        }

        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var secs) && secs >= 0
            ? TimeSpan.FromSeconds(secs)
            : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _mediatedHandle?.Dispose();   // remove the chrome remote if the tab is closed while backgrounded
        _mediatedHandle = null;
        _renderTimer.Stop();
        _engine?.Dispose();
        _engine = null;

        _config.Volume = Volume;
        try { _shell.SaveFeatureConfig(_config); } catch { /* best-effort persistence */ }
    }
}
