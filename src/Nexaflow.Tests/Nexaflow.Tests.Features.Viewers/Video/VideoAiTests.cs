using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Video;

/// <summary>
/// Covers the Video AI-integration surface: the headless-testable read/steer tools driven exactly as the
/// conversation hub drives them (get_media_info reports the parsed facts + honest playback state; seek moves
/// the playhead through RunOnUiAsync), the file-scoped security context (aspect 4), and honest get_context.
/// The vision tool <c>video_capture_frame</c> is asserted to still be present but is NOT invoked here — a real
/// frame grab needs a live media decode/render pipeline and is exercised by the UI journey instead.
/// </summary>
[TestClass]
public class VideoAiTests
{
    /// <summary>A shell whose RunOnUiAsync actually runs the action — the plain substitute's default swallows
    /// it, no-opping the UI-marshalled seek.</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private static VideoViewModel Make() => new("clip.mp4", RunningShell());

    [TestMethod]
    [CoversNode("video-ai-act")]
    [CoversNode("video-ai-context")]
    public async Task AiTools_MediaInfoAndSeek_ThroughToolSurface_WithHonestContextAndScope()
    {
        var vm = Make();

        var tools = vm.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "video_capture_frame", "get_media_info", "seek" },
            tools.Select(t => t.Name).ToArray(),
            "the Video AI act tool surface changed — update the tree's video-ai-act leaves to match");

        // video_capture_frame stays the vision tool (auto-run, safe). Not invoked: needs a live decode pipeline.
        var capture = tools.Single(t => t.Name == "video_capture_frame");
        Assert.AreEqual(ToolSafety.SafeOperation, capture.Safety);

        // get_media_info: reports container / position / state from the metadata the VM already holds — fully
        // headless (no decode), gracefully noting track details aren't parsed yet.
        var info = tools.Single(t => t.Name == "get_media_info");
        Assert.AreEqual(ToolSafety.SafeOperation, info.Safety);
        var r = await info.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "clip.mp4");   // the open file
        StringAssert.Contains(r.ModelText, "MP4");        // container derived from the extension
        StringAssert.Contains(r.ModelText, "paused");     // honest playback state (not loaded / not playing)

        // seek: steers the playhead (approval-gated), marshalled through RunOnUiAsync. No engine, but the
        // position state (the timeline-slider binding) moves — so the capability is observable headlessly.
        var seek = tools.Single(t => t.Name == "seek");
        Assert.AreEqual(ToolSafety.RequiresApproval, seek.Safety);
        var s = await seek.InvokeAsync(new JsonObject { ["seconds"] = 12.5 }, CancellationToken.None);
        Assert.IsFalse(s.IsError);
        Assert.AreEqual(12.5, vm.PositionSeconds, 1e-9);

        // A malformed seek is a recoverable tool error, not a throw.
        var bad = await seek.InvokeAsync(new JsonObject { ["seconds"] = "not-a-time" }, CancellationToken.None);
        Assert.IsTrue(bad.IsError);

        // Context is honest about the file + state; scope pins to the file path so two pinned video tabs
        // stay distinguishable in the hub (aspect 4).
        StringAssert.Contains(vm.GetContext(), "clip.mp4");
        Assert.AreEqual(vm.FilePath, vm.GetSecurityContext());
    }
}
