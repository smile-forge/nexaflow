using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Video.ViewModels;

namespace Nexaflow.Features.Video.ClientTools;

/// <summary>
/// Captures the video frame at the current playback position and hands it to the model as a vision
/// attachment. Enables the flow: the user pauses, asks "what's happening here?", the AI sees the frame
/// and answers, then the user resumes. Read-only (auto-runs) — it only observes the open video. Mirrors
/// the Web feature's page-capture tool (see <c>HtmlViewModel</c>).
/// </summary>
public sealed class VideoCaptureFrameTool(VideoViewModel vm) : IClientTool
{
    public string Name => "video_capture_frame";

    public string Description =>
        "Capture the frame the user is currently looking at (the video's current playback position) and " +
        "return it as an image you can see. Use it to answer questions about what is on screen.";

    public IReadOnlyList<ClientToolParameter> Parameters => [];

    public ToolSafety Safety => ToolSafety.ReadOnly;

    public bool Parallelizable => false;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        try
        {
            var shot = await vm.CaptureCurrentFrameAsync(ct).ConfigureAwait(true);
            if (shot is null)
                return ToolResult.Error("Couldn't capture a frame.",
                    "No frame is available — the video may have failed to open.");

            var (png, time) = shot.Value;
            return ToolResult.Ok(
                    $"Captured the frame at {time}.",
                    $"A frame from \"{vm.FileName}\" at {time} is attached for you to look at.")
                with { Images = [new ContextImage(png, "image/png", $"{vm.FileName} @ {time}")] };
        }
        catch (System.OperationCanceledException)
        {
            return ToolResult.Error("Frame capture cancelled.");
        }
        catch (System.Exception ex)
        {
            return ToolResult.Error("Couldn't capture a frame.", ex.Message);
        }
    }
}
