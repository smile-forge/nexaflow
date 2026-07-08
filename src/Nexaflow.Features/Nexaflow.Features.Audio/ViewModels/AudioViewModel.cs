using Nexaflow.Visuals.Common.Formatting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Audio.Models;
using Nexaflow.Features.Audio.Services;
using Nexaflow.Features.Common;

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

    public AudioViewModel(IReadOnlyList<string> paths, int startIndex, IShellServices shell, AudioConfig config,
                          bool autoPlay = false)
    {
        _shell = shell;
        _config = config;
        _paths = paths.Where(AudioFileTypes.IsAudio).ToList();
        _index = _paths.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _paths.Count - 1);
        _volume = config.Volume;
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

    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private string _album = string.Empty;
    [ObservableProperty] private BitmapSource? _albumArt;
    [ObservableProperty] private TagEditorViewModel? _tagEditor;

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

    /// <summary>The tab became visible: resume playback if it was paused by leaving, and restart the loop.</summary>
    public void OnActivated()
    {
        if (_resumeOnActivate && _engine is not null)
        {
            _resumeOnActivate = false;
            _engine.Play();
            IsPlaying = true;
        }
        if (IsPlaying) _renderTimer.Start();
    }

    /// <summary>The tab was hidden (tab switch): pause playback (to resume on return) and stop the loop.</summary>
    public void OnDeactivated()
    {
        _renderTimer.Stop();
        if (_engine is { IsPlaying: true })
        {
            _engine.Pause();
            IsPlaying = false;
            _resumeOnActivate = true;
        }
    }

    /// <summary>Re-points the tab at a new queue/track (shell tab reuse). A no-op if the queue is unchanged,
    /// so re-selecting the tab never interrupts playback.</summary>
    public async Task ReinitializeAsync(IReadOnlyList<string> paths, int startIndex)
    {
        var list = paths.Where(AudioFileTypes.IsAudio).ToList();
        if (list.Count == 0) return;
        if (list.SequenceEqual(_paths) && Math.Clamp(startIndex, 0, list.Count - 1) == _index) return;

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
        _renderTimer.Start();
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
            _renderTimer.Start();
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _renderTimer.Stop();
        _engine?.Dispose();
        _engine = null;

        _config.Volume = Volume;
        try { _shell.SaveFeatureConfig(_config); } catch { /* best-effort persistence */ }
    }
}
