using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Audio;
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Audio;

/// <summary>
/// The Audio player's AI surface — honest now-playing context, the file-scoped security context, and the
/// client tools (read_now_playing + the transport tools). Playback/seek/queue-advance drive a live
/// <c>AudioPlaybackEngine</c> and an output device, so the actual sound isn't unit-testable here — these
/// pin the wording, the scope, the reads, and every argument/no-track/boundary guard, which is what runs
/// off the UI thread when the model calls a tool.
/// </summary>
[TestClass]
public class AudioAiTests
{
    private static IShellServices Shell()
    {
        // Marshalled paths aren't exercised by these tests (reads + guards return before RunOnUiAsync),
        // but wire it to run inline anyway so a future assertion on a marshalled path can't silently no-op.
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private static AudioViewModel NewPlayer(IReadOnlyList<string> paths, int startIndex = 0)
        => new(paths, startIndex, Shell(), new AudioConfig());

    private static Task<ToolResult> Invoke(AudioViewModel vm, string tool, JsonObject? args = null)
        => vm.GetClientTools().Single(t => t.Name == tool).InvokeAsync(args ?? new JsonObject(), CancellationToken.None);

    // ── Context + scope ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("audio-ai-context")]
    public async Task NoTrack_Context_Scope_And_Read_AreHonest()
    {
        var vm = NewPlayer([]);

        StringAssert.Contains(vm.GetContext(), "no track");
        Assert.IsNull(vm.GetSecurityContext());

        var r = await Invoke(vm, "read_now_playing");
        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "No track");
    }

    [TestMethod]
    [CoversNode("audio-ai-context")]
    public void SecurityScope_IsTheTrackPath_AndDistinctPerTrack()
    {
        var files = TestSampleData.Files("audio");
        Assert.IsTrue(files.Count >= 2, "audio fixtures should provide at least two tracks");

        var a = NewPlayer(files, startIndex: 0);
        var b = NewPlayer(files, startIndex: 1);

        Assert.AreEqual(files[0], a.GetSecurityContext());
        Assert.AreEqual(files[1], b.GetSecurityContext());
        Assert.AreNotEqual(a.GetSecurityContext(), b.GetSecurityContext());

        // context is honest about the queued track + position
        StringAssert.Contains(a.GetContext(), "track 1 of 2");
    }

    // ── read_now_playing over a real queue (no device touched) ──────────────────

    [TestMethod]
    [CoversNode("audio-ai-act")]
    public async Task ReadNowPlaying_ReportsTrackAndQueue()
    {
        var files = TestSampleData.Files("audio");
        var vm = NewPlayer(files, startIndex: 0);

        var r = await Invoke(vm, "read_now_playing");

        Assert.IsFalse(r.IsError, r.ModelText);
        StringAssert.Contains(r.ModelText, "track 1 of 2");
        StringAssert.Contains(r.ModelText.ToLowerInvariant(), "tone_mono");
    }

    // ── Tool wiring, safety, and the guard paths (no engine needed) ─────────────

    [TestMethod]
    [CoversNode("audio-ai-act")]
    public void ToolSurface_IsFiveSafeOperations()
    {
        var tools = NewPlayer([]).GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "read_now_playing", "control_playback", "seek", "next_track", "previous_track" },
            tools.Select(t => t.Name).ToArray(),
            "the Audio AI act tool surface changed — update the tree's audio-ai-act leaves to match");
        Assert.IsTrue(tools.All(t => t.Safety == ToolSafety.SafeOperation),
            "player tools are reversible view-state changes / reads — no approval prompt");
    }

    [TestMethod]
    [CoversNode("audio-ai-act")]
    public async Task ControlPlayback_RejectsUnknownAction()
    {
        var r = await Invoke(NewPlayer(TestSampleData.Files("audio")), "control_playback",
            new JsonObject { ["action"] = "warp" });
        Assert.IsTrue(r.IsError);
    }

    [TestMethod]
    [CoversNode("audio-ai-act")]
    public async Task TransportTools_ErrorWhenNothingLoaded()
    {
        var vm = NewPlayer([]);
        Assert.IsTrue((await Invoke(vm, "control_playback", new JsonObject { ["action"] = "play" })).IsError);
        Assert.IsTrue((await Invoke(vm, "seek", new JsonObject { ["position"] = "30" })).IsError);
        Assert.IsTrue((await Invoke(vm, "next_track")).IsError);
        Assert.IsTrue((await Invoke(vm, "previous_track")).IsError);
    }

    [TestMethod]
    [CoversNode("audio-ai-act")]
    public async Task Navigation_NoOpsAtQueueBoundaries()
    {
        var files = TestSampleData.Files("audio");

        // At the first track, previous_track is a graceful no-op (never touches the engine).
        var prev = await Invoke(NewPlayer(files, startIndex: 0), "previous_track");
        Assert.IsFalse(prev.IsError);
        StringAssert.Contains(prev.ModelText.ToLowerInvariant(), "first");

        // At the last track, next_track is a graceful no-op.
        var next = await Invoke(NewPlayer(files, startIndex: files.Count - 1), "next_track");
        Assert.IsFalse(next.IsError);
        StringAssert.Contains(next.ModelText.ToLowerInvariant(), "last");
    }
}
