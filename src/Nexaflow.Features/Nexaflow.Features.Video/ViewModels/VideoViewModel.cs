using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Video.ClientTools;
using Nexaflow.Features.Video.Interop;
using Nexaflow.Features.Video.Models;
using Nexaflow.Features.Video.Rendering;
using VlcMediaPlayer = LibVLCSharp.MediaPlayer;   // disambiguate from System.Windows.Media.MediaPlayer

namespace Nexaflow.Features.Video.ViewModels;

/// <summary>
/// Drives the embedded video player. The video is rendered into a WPF <see cref="WriteableBitmap"/>
/// (<see cref="VideoFrame"/>) via <see cref="VlcVideoSink"/> rather than a native window — so the whole UI
/// (transport bar, click-to-pause, scene strip, fullscreen) is ordinary WPF with no airspace caveats.
/// <para>
/// Holds native resources, so it is <see cref="IDisposable"/> (the registration disposes it on
/// <see cref="Page.Closed"/>); it pauses on tab-switch (<see cref="OnDeactivated"/>) and there is no
/// autoplay. Playback uses plain Play/SetPause; thumbnails and frame captures run on their own short-lived
/// <see cref="Media"/> objects so they never share a decoder with live playback. A fatal engine error
/// raises <see cref="FatalError"/> so the shell can close the tab with a notice.
/// </para>
/// </summary>
public sealed partial class VideoViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private const int KeyframeCount = 32;
    private const uint ThumbWidth = 160, ThumbHeight = 90;

    private readonly string _path;
    private readonly IShellServices? _shell;
    private readonly bool _hardwareDecoding;
    private readonly CancellationTokenSource _cts = new();

    private LibVLC? _libVlc;
    private VlcMediaPlayer? _mp;
    private Media? _media;
    private VlcVideoSink? _sink;

    private bool _loaded;
    private bool _disposed;
    private bool _syncingPosition;   // guards the timeline slider against a seek/echo loop
    private long? _pendingSeekMs;    // seek requested before playback started → apply on first Playing
    private uint _videoWidth, _videoHeight;
    private bool _hasSubtitleTracks;
    private VideoCaptureFrameTool? _captureTool;
    private Task? _keyframeTask;     // tracked so disposal waits for it before freeing the engine

    /// <summary>Raised (on the UI thread) when the engine hits an unrecoverable error or fails to open. The
    /// registration handles it by notifying the user and closing the tab.</summary>
    public event Action<string>? FatalError;

    public VideoViewModel(string path, IShellServices? shell, bool hardwareDecoding = false)
    {
        _path             = path;
        _shell            = shell;
        _hardwareDecoding = hardwareDecoding;
        Keyframes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasKeyframes));
            OnPropertyChanged(nameof(ShowSceneStrip));
        };
    }

    public string FilePath => _path;
    public string FileName => Path.GetFileName(_path);

    // ── Observable playback state ───────────────────────────────────────────

    /// <summary>The live video surface, bound to an <c>&lt;Image&gt;</c> in the tab and the fullscreen window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPoster))]
    private WriteableBitmap? _videoFrame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBigPlayButton), nameof(ShowPoster))]
    private bool _isLoaded;

    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBigPlayButton))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPoster))]
    private bool _hasPlayed;

    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private string _positionDisplay = "0:00";
    [ObservableProperty] private string _durationDisplay = "0:00";
    [ObservableProperty] private int _volume = 100;
    [ObservableProperty] private bool _isMuted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLabel))]
    private double _playbackRate = 1.0;

    [ObservableProperty] private bool _isSpeedMenuOpen;
    [ObservableProperty] private bool _isSubtitleMenuOpen;

    [ObservableProperty] private bool _isInfoPanelVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSceneStrip))]
    private bool _isSceneStripVisible = true;

    [ObservableProperty] private bool _isFullScreen;

    [ObservableProperty] private MediaInfo? _mediaInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPoster))]
    private ImageSource? _posterImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBigPlayButton), nameof(ShowPoster))]
    private bool _hasError;

    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>The centre play affordance shows when loaded, not playing, and with no error.</summary>
    public bool ShowBigPlayButton => IsLoaded && !IsPlaying && !HasError;

    /// <summary>A first-frame still shown over the surface until the user first presses play.</summary>
    public bool ShowPoster => IsLoaded && !HasPlayed && !HasError && PosterImage is not null;

    public string RateLabel => PlaybackRate == 1.0 ? "1×" : $"{PlaybackRate.ToString("0.##", CultureInfo.InvariantCulture)}×";

    /// <summary>Whether the file carries any subtitle/text tracks (controls the CC button's relevance).</summary>
    public bool HasSubtitles => _hasSubtitleTracks;

    public ObservableCollection<SubtitleTrackOption> SubtitleTracks { get; } = [];

    public ObservableCollection<KeyframeViewModel> Keyframes { get; } = [];

    public bool HasKeyframes => Keyframes.Count > 0;

    public bool ShowSceneStrip => IsSceneStripVisible && HasKeyframes;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>Builds the engine and parses the media on a background thread (so opening a tab never
    /// blocks the UI — a "Loading…" placeholder shows until ready), then kicks off scene thumbnailing.
    /// Idempotent.</summary>
    public async Task LoadAsync()
    {
        if (_loaded || _disposed) return;
        _loaded = true;

        try
        {
            await Task.Run(() =>
            {
                VlcInit.EnsureInitialized();
                _libVlc = new LibVLC("--no-video-title-show");
                _mp     = new VlcMediaPlayer(_libVlc) { EnableHardwareDecoding = _hardwareDecoding };
                _mp.SetVolume(Volume);

                // Render frames into a WPF WriteableBitmap (no native window → no airspace).
                _sink = new VlcVideoSink(_mp, OnUi, bmp => VideoFrame = bmp);

                _media = new Media(new Uri(_path));

                _mp.TimeChanged      += OnTimeChanged;
                _mp.LengthChanged    += OnLengthChanged;
                _mp.Playing          += OnPlaying;
                _mp.Paused           += OnPaused;
                _mp.Stopped          += OnStopped;
                _mp.EncounteredError += OnEncounteredError;

                _mp.Media = _media;
            }, _cts.Token).ConfigureAwait(false);

            await _media!.ParseAsync(_libVlc!, MediaParseOptions.ParseLocal, -1, _cts.Token)
                         .ConfigureAwait(false);

            var info     = BuildMediaInfo();
            var dur      = Math.Max(0, _media.Duration / 1000.0);
            var durLabel = Format(_media.Duration);
            await OnUi(() =>
            {
                DurationSeconds = dur;
                DurationDisplay = durLabel;
                MediaInfo       = info;
                OnPropertyChanged(nameof(HasSubtitles));
                IsLoaded        = true;
                IsLoading       = false;
            }).ConfigureAwait(false);

            _keyframeTask = Task.Run(() => GenerateKeyframesAsync(_cts.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await OnUi(() => { IsLoading = false; Fatal($"Couldn't open this video. {ex.Message}"); }).ConfigureAwait(false);
        }
    }

    /// <summary>Copies the latest decoded frame into the bitmap. Driven by the view's per-frame render tick.</summary>
    public void RenderTick() => _sink?.Blit();

    public void OnActivated() { }

    /// <summary>Tab switched away (or closing): stop the picture and sound by pausing.</summary>
    public void OnDeactivated()
    {
        try { if (_mp is { IsPlaying: true }) _mp.SetPause(true); } catch { }
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (_mp is null || HasError) return;
        try
        {
            if (_mp.IsPlaying)       _mp.SetPause(true);
            else if (_mp.IsSeekable) _mp.SetPause(false);   // paused mid-stream → resume
            else                     _mp.Play();            // not started / ended → start
        }
        catch { }
    }

    [RelayCommand]
    private void StepBack() { try { _mp?.JumpTime(-5000); } catch { } }

    [RelayCommand]
    private void StepForward() { try { _mp?.JumpTime(5000); } catch { } }

    [RelayCommand]
    private void ToggleMute()
    {
        if (_mp is null) return;
        try { _mp.ToggleMute(); IsMuted = _mp.Mute; } catch { }
    }

    [RelayCommand]
    private void ToggleInfoPanel() => IsInfoPanelVisible = !IsInfoPanelVisible;

    [RelayCommand]
    private void ToggleSceneStrip() => IsSceneStripVisible = !IsSceneStripVisible;

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    public void ExitFullScreen() => IsFullScreen = false;

    [RelayCommand]
    private void ToggleSpeedMenu() => IsSpeedMenuOpen = !IsSpeedMenuOpen;

    [RelayCommand]
    private void SetSpeed(string? factor)
    {
        if (double.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            PlaybackRate = rate;
        IsSpeedMenuOpen = false;
    }

    [RelayCommand]
    private void ToggleSubtitleMenu()
    {
        if (!IsSubtitleMenuOpen) RefreshSubtitleTracks();
        IsSubtitleMenuOpen = !IsSubtitleMenuOpen;
    }

    [RelayCommand]
    private void SetSubtitle(SubtitleTrackOption? option)
    {
        if (option is null || _mp is null) { IsSubtitleMenuOpen = false; return; }
        try
        {
            if (option.Id is null) _mp.Unselect(TrackType.Text);
            else                   _mp.Select(TrackType.Text, option.Id);
        }
        catch { }
        IsSubtitleMenuOpen = false;
    }

    /// <summary>Jump to a scene from the strip. If playback hasn't started, start it and apply the seek once
    /// the first frame is up (avoids seeking a player with no decoded output yet).</summary>
    [RelayCommand]
    private void SeekToKeyframe(KeyframeViewModel? frame)
    {
        if (frame is null || _mp is null) return;
        var ms = (long)frame.Position.TotalMilliseconds;
        try
        {
            if (_mp.IsSeekable) _mp.SetTime(ms, fast: false);
            else { _pendingSeekMs = ms; _mp.Play(); }
        }
        catch { }
    }

    // ── Slider / volume / rate two-way plumbing ─────────────────────────────

    partial void OnPositionSecondsChanged(double value)
    {
        if (_syncingPosition || _mp is null || _media is null || _media.Duration <= 0) return;
        try { if (_mp.IsSeekable) _mp.SetTime((long)Math.Clamp(value * 1000.0, 0, _media.Duration), fast: true); }
        catch { }
    }

    partial void OnVolumeChanged(int value) { try { _mp?.SetVolume(Math.Clamp(value, 0, 100)); } catch { } }

    partial void OnPlaybackRateChanged(double value) { try { _mp?.SetRate((float)value); } catch { } }

    // ── Engine events (raised on libvlc threads → marshal to UI) ────────────

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        var time = e.Time;
        _ = OnUi(() =>
        {
            _syncingPosition = true;
            PositionSeconds  = time / 1000.0;
            _syncingPosition = false;
            PositionDisplay  = Format(time);
        });
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        var len = _mp?.Length ?? 0;
        _ = OnUi(() =>
        {
            DurationSeconds = Math.Max(0, len / 1000.0);
            DurationDisplay = Format(len);
        });
    }

    private void OnPlaying(object? sender, EventArgs e) => _ = OnUi(() =>
    {
        IsPlaying = true;
        HasPlayed = true;
        // Re-assert the slider's volume: libvlc creates the audio output only once playback starts, so a
        // volume set before then can be dropped — which made playback quieter than the slider implied.
        try { _mp?.SetVolume(Volume); } catch { }
        if (_pendingSeekMs is { } ms)
        {
            _pendingSeekMs = null;
            try { if (_mp is { IsSeekable: true }) _mp.SetTime(ms, fast: false); } catch { }
        }
    });

    private void OnPaused(object? sender, EventArgs e)  => _ = OnUi(() => IsPlaying = false);
    private void OnStopped(object? sender, EventArgs e) => _ = OnUi(() => IsPlaying = false);

    private void OnEncounteredError(object? sender, EventArgs e) =>
        _ = OnUi(() => Fatal("Playback error: this file may use an unsupported codec or be corrupt."));

    private void Fatal(string message)
    {
        if (_disposed) return;
        HasError     = true;
        ErrorMessage = message;
        FatalError?.Invoke(message);
    }

    // ── Subtitles ────────────────────────────────────────────────────────────

    private void RefreshSubtitleTracks()
    {
        SubtitleTracks.Clear();
        SubtitleTracks.Add(new SubtitleTrackOption(null, "Off"));
        if (_mp is null) return;

        MediaTrackList? list = null;
        try
        {
            list = _mp.Tracks(TrackType.Text);
            int n = 0;
            foreach (MediaTrack tr in list)
            {
                n++;
                var label = FirstNonEmpty(tr.Name, tr.Description, tr.Language) ?? $"Subtitle {n}";
                SubtitleTracks.Add(new SubtitleTrackOption(tr.Id, label));
            }
        }
        catch { }
        finally { (list as IDisposable)?.Dispose(); }
    }

    // ── Media info ──────────────────────────────────────────────────────────

    private MediaInfo BuildMediaInfo()
    {
        var sections = new List<MediaInfoSection>();
        var durLabel = Format(_media?.Duration ?? 0);

        var file = new MediaInfoSection { Header = "File" };
        file.Rows.Add(new("Name", FileName));
        file.Rows.Add(new("Container", Path.GetExtension(_path).TrimStart('.').ToUpperInvariant()));
        try { file.Rows.Add(new("Size", FormatBytes(new FileInfo(_path).Length))); } catch { }
        file.Rows.Add(new("Duration", durLabel));
        sections.Add(file);

        string videoSummary = "", audioSummary = "";

        AddTracks(TrackType.Video, "Video", sections, (sec, tr) =>
        {
            var v = tr.Data.Video;
            if (_videoWidth == 0) { _videoWidth = v.Width; _videoHeight = v.Height; }
            var codec = CodecName(TrackType.Video, tr);
            sec.Rows.Add(new("Codec", codec));
            sec.Rows.Add(new("Resolution", $"{v.Width} × {v.Height}"));
            if (v.FrameRateDen > 0)
                sec.Rows.Add(new("Frame rate", $"{v.FrameRateNum / (double)v.FrameRateDen:0.##} fps"));
            if (tr.Bitrate > 0) sec.Rows.Add(new("Bitrate", $"{tr.Bitrate / 1000} kbps"));
            if (videoSummary.Length == 0) videoSummary = $"{v.Width}×{v.Height} {codec}";
        });

        AddTracks(TrackType.Audio, "Audio", sections, (sec, tr) =>
        {
            var a = tr.Data.Audio;
            var codec = CodecName(TrackType.Audio, tr);
            sec.Rows.Add(new("Codec", codec));
            sec.Rows.Add(new("Channels", ChannelLabel(a.Channels)));
            if (a.Rate > 0) sec.Rows.Add(new("Sample rate", $"{a.Rate / 1000.0:0.#} kHz"));
            if (!string.IsNullOrWhiteSpace(tr.Language)) sec.Rows.Add(new("Language", tr.Language!));
            if (audioSummary.Length == 0) audioSummary = $"{codec} {ChannelLabel(a.Channels)}";
        });

        int subtitleCount = 0;
        AddTracks(TrackType.Text, "Subtitles", sections, (sec, tr) =>
        {
            subtitleCount++;
            sec.Rows.Add(new("Track", FirstNonEmpty(tr.Description, tr.Language, tr.Name) ?? tr.Id ?? "subtitle"));
            if (!string.IsNullOrWhiteSpace(tr.Language)) sec.Rows.Add(new("Language", tr.Language!));
        });
        _hasSubtitleTracks = subtitleCount > 0;

        var summary = string.Join(", ",
            new[] { videoSummary, durLabel, audioSummary }.Where(s => !string.IsNullOrEmpty(s)));

        return new MediaInfo { FileName = FileName, Sections = sections, Summary = summary };
    }

    private void AddTracks(TrackType type, string label, List<MediaInfoSection> sections,
                           Action<MediaInfoSection, MediaTrack> fill)
    {
        if (_media is null) return;
        MediaTrackList? list = null;
        try
        {
            list = _media.TrackList(type);
            int n = 0;
            foreach (MediaTrack tr in list)
            {
                var sec = new MediaInfoSection { Header = list.Count > 1 ? $"{label} #{++n}" : label };
                try { fill(sec, tr); } catch { }
                if (sec.Rows.Count > 0) sections.Add(sec);
            }
        }
        catch { }
        finally { (list as IDisposable)?.Dispose(); }
    }

    private string CodecName(TrackType type, MediaTrack tr)
    {
        try
        {
            var desc = _media?.CodecDescription(type, tr.Codec);
            if (!string.IsNullOrWhiteSpace(desc)) return desc!;
        }
        catch { }
        return FourCc(tr.Codec);
    }

    // ── Keyframe / scene strip (own short-lived Media — never the player's) ──

    private async Task GenerateKeyframesAsync(CancellationToken ct)
    {
        if (_libVlc is null) return;

        Media thumbMedia;
        try { thumbMedia = new Media(new Uri(_path)); } catch { return; }

        using (thumbMedia)
        {
            long durMs;
            try
            {
                await thumbMedia.ParseAsync(_libVlc, MediaParseOptions.ParseLocal, -1, ct).ConfigureAwait(false);
                durMs = thumbMedia.Duration;
            }
            catch (OperationCanceledException) { return; }
            catch { durMs = 0; }

            if (durMs <= 0) durMs = (long)(DurationSeconds * 1000);
            if (durMs <= 0) return;

            for (int i = 0; i < KeyframeCount; i++)
            {
                if (ct.IsCancellationRequested) return;
                long t = (long)((i + 0.5) / KeyframeCount * durMs);
                try
                {
                    using var pic = await thumbMedia
                        .GenerateThumbnailAsync(_libVlc, t, ThumbnailerSeekSpeed.Fast,
                                                ThumbWidth, ThumbHeight, crop: false, PictureType.Png, 3000, ct)
                        .ConfigureAwait(false);

                    var bmp = PictureToBitmap(pic);
                    if (bmp is null) continue;

                    int idx = i;
                    var kf = new KeyframeViewModel { Position = TimeSpan.FromMilliseconds(t), Thumbnail = bmp };
                    await OnUi(() =>
                    {
                        Keyframes.Add(kf);
                        if (idx == 0 && PosterImage is null) PosterImage = bmp;
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch { /* skip a frame that won't decode */ }
            }
        }
    }

    // ── AI: capture the current frame for vision (own short-lived Media) ─────

    /// <summary>Decodes the frame at the current playback position and returns it as PNG bytes (for the
    /// <c>video_capture_frame</c> tool). Uses a fresh Media so it never disturbs live playback.</summary>
    public async Task<(byte[] Png, string TimeLabel)?> CaptureCurrentFrameAsync(CancellationToken ct)
    {
        if (_libVlc is null) return null;
        long t = _mp?.Time ?? 0;

        uint w = _videoWidth > 0 ? Math.Min(_videoWidth, 1280u) : 1280u;
        uint h = (_videoWidth > 0 && _videoHeight > 0)
            ? (uint)Math.Round(_videoHeight * (w / (double)_videoWidth))
            : 720u;

        Media capMedia;
        try { capMedia = new Media(new Uri(_path)); } catch { return null; }
        using (capMedia)
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct))
        {
            using var pic = await capMedia
                .GenerateThumbnailAsync(_libVlc, t, ThumbnailerSeekSpeed.Precise, w, h, crop: false, PictureType.Png, 5000, linked.Token)
                .ConfigureAwait(false);

            var bytes = PictureBytes(pic);
            return bytes is null ? null : (bytes, Format(t));
        }
    }

    // ── IPageViewModel ──────────────────────────────────────────────────────

    public string GetContext()
    {
        var state = HasError ? "failed to open"
                  : IsPlaying ? $"playing at {PositionDisplay}"
                  : $"paused at {PositionDisplay}";
        var facts = MediaInfo?.Summary;
        return string.IsNullOrEmpty(facts)
            ? $"Video \"{FileName}\" is open ({state})."
            : $"Video \"{FileName}\" is open: {facts}. Currently {state}.";
    }

    public IReadOnlyList<IClientTool> GetClientTools() =>
        [_captureTool ??= new VideoCaptureFrameTool(this)];

    public string? GetAiSystemPromptGuidance() =>
        "A video is open. To answer questions about what is on screen, call video_capture_frame to grab " +
        "the frame at the current playback position; it is returned to you as an image.";

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static BitmapImage? PictureToBitmap(Picture? pic)
    {
        var bytes = PictureBytes(pic);
        if (bytes is null) return null;
        using var ms = new MemoryStream(bytes);
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption  = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>Copies a PNG-typed <see cref="Picture"/>'s encoded bytes out of the native buffer.</summary>
    private static byte[]? PictureBytes(Picture? pic)
    {
        if (pic is null) return null;
        var (ptr, len) = pic.Buffer;
        long n = (long)(ulong)len;
        if (ptr == IntPtr.Zero || n <= 0) return null;
        var bytes = new byte[n];
        Marshal.Copy(ptr, bytes, 0, (int)n);
        return bytes;
    }

    private static string FourCc(uint codec)
    {
        Span<char> c =
        [
            (char)(codec        & 0xFF),
            (char)((codec >> 8)  & 0xFF),
            (char)((codec >> 16) & 0xFF),
            (char)((codec >> 24) & 0xFF),
        ];
        var s = new string(c).Trim('\0', ' ');
        return string.IsNullOrWhiteSpace(s) ? codec.ToString() : s.ToUpperInvariant();
    }

    private static string ChannelLabel(uint channels) => channels switch
    {
        0 => "—",
        1 => "Mono",
        2 => "Stereo",
        6 => "5.1",
        8 => "7.1",
        _ => $"{channels} ch",
    };

    private static string Format(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }

    private Task OnUi(Action action) => _shell?.RunOnUiAsync(action) ?? Run(action);
    private static Task Run(Action a) { a(); return Task.CompletedTask; }

    // ── Disposal ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }

        if (_mp is not null)
        {
            _mp.TimeChanged      -= OnTimeChanged;
            _mp.LengthChanged    -= OnLengthChanged;
            _mp.Playing          -= OnPlaying;
            _mp.Paused           -= OnPaused;
            _mp.Stopped          -= OnStopped;
            _mp.EncounteredError -= OnEncounteredError;
            try { _mp.Stop(); } catch { }
            try { _mp.Dispose(); } catch { }
            _mp = null;
        }

        // Free the render buffers only after the player (and thus its decode callbacks) has stopped.
        try { _sink?.Dispose(); } catch { }
        _sink = null;

        // The keyframe task may still be inside a native GenerateThumbnail call on _libVlc — freeing the
        // engine under it would crash. Hand ownership to a continuation that waits for it (already cancelled).
        var keyframeTask = _keyframeTask;
        var media        = _media;
        var libVlc       = _libVlc;
        var cts          = _cts;
        _media  = null;
        _libVlc = null;

        Task.Run(async () =>
        {
            try { if (keyframeTask is not null) await keyframeTask.ConfigureAwait(false); } catch { }
            try { media?.Dispose(); }  catch { }
            try { libVlc?.Dispose(); } catch { }
            try { cts.Dispose(); }     catch { }
        });
    }
}
