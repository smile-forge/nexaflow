using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Nexaflow.Features.Audio;
using Nexaflow.Features.Audio.Models;
using Nexaflow.Features.Audio.Services;
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Audio;

/// <summary>
/// What the player shows and what it computes, with no audio device in the room: the now-playing readouts,
/// the queue position, the lyric highlight tracking the playhead, the tag editor's dirty/save contract, and
/// the two analysers (FFT bands and the waveform envelope) that the visualisers draw.
/// <para>
/// Anything that needs a real output device — starting playback, seeking a loaded stream, mid-queue
/// advance — is left to the UI journey; what is asserted here is the state either side of it, which is
/// where the readouts actually come from.
/// </para>
/// </summary>
[TestClass]
public class AudioSurfaceTests
{
    private static AudioViewModel Make(params string[] paths)
        => new(paths, 0, Substitute.For<IShellServices>(), new AudioConfig());

    private static string Sample(string name) => Path.Combine(TestSampleData.Path("audio"), name);

    // ── Now playing ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("audio-trackinfo")]
    public void NowPlaying_PrefersTheTitleTag_AndFallsBackToTheFileName()
    {
        var vm = Make("a.mp3");

        vm.FileName = "01 - unknown.mp3";
        Assert.AreEqual("01 - unknown.mp3", vm.NowPlayingText, "an untagged file still has to say something");

        vm.Title = "A Song";
        Assert.AreEqual("A Song", vm.NowPlayingText);

        vm.Artist = "A Band";
        Assert.AreEqual("A Band — A Song", vm.NowPlayingText);
    }

    [TestMethod]
    [CoversNode("audio-trackinfo")]
    public void NowPlaying_WithNothingLoadedAtAll_StillReadsAsThePlayer()
        => Assert.AreEqual("Audio player", Make().NowPlayingText);

    [TestMethod]
    [CoversNode("audio-queue-counter")]
    public void QueuePosition_CountsFromOne_AndIsBlankForASingleTrack()
    {
        var many = Make("a.mp3", "b.mp3", "c.mp3");
        Assert.IsTrue(many.HasQueue);
        Assert.AreEqual("1 / 3", many.QueueText, "the counter is human-numbered, not zero-based");

        many.MovePlaylistItem(0, 2);   // the current track moves to the end
        Assert.AreEqual("3 / 3", many.QueueText, "the counter follows the track, not the slot");

        Assert.IsFalse(Make("only.mp3").HasQueue);
    }

    [TestMethod]
    [CoversNode("audio-albumart")]
    public void AlbumArt_IsAbsentUntilATrackWithACoverLoads()
    {
        var vm = Make("a.mp3");

        Assert.IsNull(vm.AlbumArt, "the view shows its music-note placeholder while this is null");
    }

    // ── Transport readouts ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("audio-time-readout")]
    public void TimeReadouts_AreMinutesAndSeconds()
    {
        var vm = Make("a.mp3");
        vm.Position = TimeSpan.FromSeconds(65);
        vm.Duration = TimeSpan.FromSeconds(3 * 60 + 7);

        Assert.AreEqual("1:05", vm.PositionText);
        Assert.AreEqual("3:07", vm.DurationText);
    }

    [TestMethod]
    [CoversNode("audio-waveform")]
    public void WaveformSplit_TracksThePlayhead_AndIsSafeBeforeATrackLoads()
    {
        var vm = Make("a.mp3");
        Assert.AreEqual(0, vm.ProgressFraction, "no duration yet — the whole waveform reads as unplayed");

        vm.Duration = TimeSpan.FromSeconds(100);
        vm.Position = TimeSpan.FromSeconds(25);
        Assert.AreEqual(0.25, vm.ProgressFraction, 1e-9);

        vm.Position = TimeSpan.FromSeconds(500);   // a stale position must not draw past the end
        Assert.AreEqual(1.0, vm.ProgressFraction, 1e-9);
    }

    [TestMethod]
    [CoversNode("audio-playpause")]
    public void PlayPauseGlyph_FollowsTheTransportState()
    {
        var vm = Make("a.mp3");
        Assert.AreEqual("▶", vm.PlayPauseGlyph);

        vm.IsPlaying = true;
        Assert.AreEqual("⏸", vm.PlayPauseGlyph);
    }

    [TestMethod]
    [CoversNode("audio-seek")]
    public void SeekingBeforeAnythingIsLoaded_IsANoOp_NotACrash()
    {
        var vm = Make();          // empty queue — the waveform is still clickable

        vm.SeekToFraction(0.5);

        Assert.AreEqual(TimeSpan.Zero, vm.Position);
    }

    [TestMethod]
    [CoversNode("audio-playlist-play")]
    public async Task JumpingToATrackOutsideTheQueue_IsIgnored()
    {
        var vm = Make("a.mp3", "b.mp3");

        await vm.PlayAtAsync(99);

        Assert.AreEqual("1 / 2", vm.QueueText, "a bad index must not blank the player");
    }

    // ── Play queue ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("audio-queue")]
    public void ReorderingTheQueue_KeepsTheCurrentTrackCurrent()
    {
        var vm = Make("a.mp3", "b.mp3", "c.mp3");

        vm.MovePlaylistItem(2, 0);   // c jumps to the front; a (current) slides to index 1

        CollectionAssert.AreEqual(new[] { "c", "a", "b" }, vm.Playlist.Select(p => p.Display).ToList());
        Assert.AreEqual("2 / 3", vm.QueueText, "the playing track must not change because the list moved");
        Assert.IsTrue(vm.Playlist[1].IsCurrent);
    }

    [TestMethod]
    [CoversNode("audio-queue")]
    public void NonAudioFilesNeverEnterTheQueue()
    {
        var vm = Make("a.mp3", "cover.jpg", "notes.txt", "b.flac");

        CollectionAssert.AreEqual(new[] { "a", "b" }, vm.Playlist.Select(p => p.Display).ToList());
        Assert.AreEqual("1 / 2", vm.QueueText);
    }

    [TestMethod]
    [CoversNode("audio-next")]
    [CoversNode("audio-previous")]
    public void NextAndPreviousAreDisabledAtTheEndsOfTheQueue()
    {
        var vm = Make("a.mp3", "b.mp3");
        Assert.IsTrue(vm.HasNext);
        Assert.IsFalse(vm.HasPrevious, "there is nothing before the first track");

        var single = Make("only.mp3");
        Assert.IsFalse(single.HasNext);
        Assert.IsFalse(single.HasPrevious);
        Assert.IsFalse(single.NextCommand.CanExecute(null));
    }

    // ── Lyrics ────────────────────────────────────────────────────────────────

    private static LyricsViewModel Synced(params (int Seconds, string Text)[] lines)
    {
        var vm = new LyricsViewModel();
        vm.Load([.. lines.Select(l => new LyricLine(TimeSpan.FromSeconds(l.Seconds), l.Text))], synced: true);
        return vm;
    }

    [TestMethod]
    [CoversNode("audio-lyrics-sync")]
    public void TheHighlightedLine_IsTheLatestOneThePlayheadHasPassed()
    {
        var lyrics = Synced((0, "one"), (10, "two"), (20, "three"));

        lyrics.UpdatePosition(TimeSpan.FromSeconds(15));

        Assert.AreEqual(1, lyrics.ActiveIndex);
        Assert.IsTrue(lyrics.Lines[1].IsActive);
        Assert.IsFalse(lyrics.Lines[0].IsActive, "only one line is lit at a time");
    }

    [TestMethod]
    [CoversNode("audio-lyrics-sync")]
    public void SeekingBackwards_MovesTheHighlightBackToo()
    {
        var lyrics = Synced((0, "one"), (10, "two"), (20, "three"));
        lyrics.UpdatePosition(TimeSpan.FromSeconds(25));
        Assert.AreEqual(2, lyrics.ActiveIndex);

        lyrics.UpdatePosition(TimeSpan.FromSeconds(5));

        Assert.AreEqual(0, lyrics.ActiveIndex);
        Assert.IsFalse(lyrics.Lines[2].IsActive);
    }

    [TestMethod]
    [CoversNode("audio-lyrics-sync")]
    public void UnsyncedLyricsAreShownButNeverHighlighted()
    {
        var lyrics = new LyricsViewModel();
        lyrics.Load([new LyricLine(TimeSpan.Zero, "a whole verse")], synced: false);

        lyrics.UpdatePosition(TimeSpan.FromSeconds(30));

        Assert.IsTrue(lyrics.HasLyrics, "embedded lyrics still render");
        Assert.IsFalse(lyrics.IsSynced);
        Assert.AreEqual(-1, lyrics.ActiveIndex, "there are no timestamps to follow");
    }

    // ── Tag editor ────────────────────────────────────────────────────────────

    private static TagEditorViewModel Editor(out TrackTags? saved, TrackTags? initial = null)
    {
        TrackTags? captured = null;
        var editor = new TagEditorViewModel(
            initial ?? new TrackTags { Title = "Original", Artist = "Band" },
            Substitute.For<IShellServices>(),
            (tags, _, _) => { captured = tags; return Task.FromResult(true); });
        saved = captured;
        return editor;
    }

    [TestMethod]
    [CoversNode("audio-tageditor-fields")]
    public void EditingAFieldMarksTheEditorDirty_AndLoadingDoesNot()
    {
        var editor = Editor(out _);
        Assert.IsFalse(editor.IsDirty, "populating the fields from the file is not an edit");
        Assert.AreEqual("Original", editor.Title);

        editor.Title = "Renamed";

        Assert.IsTrue(editor.IsDirty, "Save is enabled off this flag");
    }

    [TestMethod]
    [CoversNode("audio-tageditor-art")]
    public void RemovingTheArtworkMarksTheEditorDirty_AndClearsThePreview()
    {
        var editor = Editor(out _);

        editor.RemoveArtCommand.Execute(null);

        Assert.IsNull(editor.ArtPreview);
        Assert.IsTrue(editor.IsDirty);
    }

    [TestMethod]
    [CoversNode("audio-tageditor-save")]
    public async Task Saving_HandsTheEditedFieldsToTheWriter_AndClearsTheDirtyFlag()
    {
        TrackTags? written = null;
        var editor = new TagEditorViewModel(
            new TrackTags { Title = "Original", Artist = "Band", Year = 1999 },
            Substitute.For<IShellServices>(),
            (tags, _, _) => { written = tags; return Task.FromResult(true); });

        editor.Title = "Renamed";
        editor.Year = "2024";
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.IsNotNull(written);
        Assert.AreEqual("Renamed", written!.Title);
        Assert.AreEqual(2024u, written.Year, "the year box is text; it has to parse back to a number");
        Assert.AreEqual("Band", written.Artist, "untouched fields are written back unchanged");
        Assert.IsFalse(editor.IsDirty, "a successful save leaves nothing outstanding");
    }

    // ── Analysers ─────────────────────────────────────────────────────────────

    /// <summary>A steady 440 Hz tone, so the FFT has something unambiguous to find.</summary>
    private sealed class Tone : ISampleProvider
    {
        private int _n;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);

        public int Read(Span<float> buffer)
        {
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = (float)Math.Sin(2 * Math.PI * 440 * _n++ / 44100);
            return buffer.Length;
        }
    }

    [TestMethod]
    [CoversNode("audio-fft")]
    public void SpectrumBands_AppearOnceEnoughAudioHasPassedThrough_AndAreNormalised()
    {
        var aggregator = new SampleAggregator(new Tone()) { BandCount = 32 };
        Assert.AreEqual(0, aggregator.LatestBands.Length, "nothing to draw before the first frame");

        var buffer = new float[4096];
        aggregator.Read(buffer);                        // pass-through fills the buffer and taps the FFT

        Assert.AreEqual(32, aggregator.LatestBands.Length, "one magnitude per bar");
        Assert.IsTrue(aggregator.LatestBands.All(b => b is >= 0 and <= 1), "bars are drawn from a 0..1 scale");
        Assert.IsTrue(aggregator.LatestBands.Any(b => b > 0), "a pure tone must light something up");
        Assert.AreEqual(1, buffer.Take(64).Count(v => v != 0) > 0 ? 1 : 0, "the audio itself passes through");

        aggregator.Reset();
        Assert.AreEqual(0, aggregator.LatestBands.Length, "pausing drops the bars to nothing");
    }

    [TestMethod]
    [CoversNode("audio-waveform-analysis")]
    public void WaveformEnvelope_IsBucketedAndNormalised()
    {
        var peaks = WaveformAnalyzer.Analyze(Sample("tone_stereo.wav"), 64);

        Assert.AreEqual(64, peaks.Length, "one peak per requested bucket");
        Assert.IsTrue(peaks.All(p => p is >= 0 and <= 1));
        Assert.IsTrue(peaks.Max() > 0.5f, "a full-scale tone should reach most of the way up the strip");
    }

    // ── Tag IO ────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("audio-tag-io")]
    public void ReadingAFileWithNoTags_ReturnsBlanks_RatherThanFailingTheLoad()
    {
        var tags = TagService.Read(Sample("tone_mono.wav"));

        Assert.AreEqual(string.Empty, tags.Title);
        Assert.AreEqual(string.Empty, tags.Artist);
        Assert.IsNull(tags.AlbumArt);
        Assert.IsTrue(tags.Duration > TimeSpan.Zero, "duration still comes from the audio properties");
    }

    [TestMethod]
    [CoversNode("audio-tag-io")]
    public void ReadingAFileThatIsNotAudioAtAll_IsSurvivable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexaudio_{Guid.NewGuid():N}.mp3");
        File.WriteAllText(path, "this is not an mp3");
        try
        {
            var tags = TagService.Read(path);   // must not throw — the player would fail to open the tab

            Assert.AreEqual(string.Empty, tags.Title);
            Assert.AreEqual(TimeSpan.Zero, tags.Duration);
        }
        finally { File.Delete(path); }
    }
}
