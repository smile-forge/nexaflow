using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// Makes a clip with the workspace's Video Generation model — from a prompt, and from a picture to start from where the
/// model takes one — then saves and opens it. A clip costs minutes and money, so it is approved before it is made.
/// </summary>
internal sealed class GenerateVideoTool(IVideoGenerationProvider model, GeneratedFiles files) : IClientTool
{
    public string Name => "generate_video";

    public string Description => model.StartsFromImages
        ? "Makes a short video clip from a description, optionally starting from a picture; saves it as a file and opens it. Takes minutes. Describe the action, the camera and the look."
        : "Makes a short video clip from a description; saves it as a file and opens it. Takes minutes. Describe the action, the camera and the look.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("prompt", "What happens in the clip."),
        new("shape", "Landscape or portrait; landscape when not given.", Required: false, Choices: Shapes),
        new("seconds", $"How long it runs: {string.Join(", ", model.Lengths)} seconds; {Default} when not given.", Required: false, Type: "integer"),
        .. model.StartsFromImages
            ? [new ClientToolParameter("first_frame", "Full path of a PNG, JPEG or WebP picture the clip starts from.", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        new("path", @"Where to save it: a folder, or a file path. Videos\Nexaflow when not given.", Required: false),
    ];

    internal static readonly IReadOnlyList<string> Shapes = ["landscape", "portrait"];

    /// <summary>The length a clip is made at when none is asked for: the one nearest eight seconds.</summary>
    private int Default => Nearest(model.Lengths, 8);

    public ToolSafety Safety => ToolSafety.RequiresApproval;

    public bool Parallelizable => false;

    public ToolCallAnnouncement? Announce(JsonObject arguments)
    {
        var seconds = Seconds(arguments);
        return new(Str.Get("Shell.Generate.Announce.Make"), Str.Format("Shell.Generate.Announce.VideoFormat", seconds),
                   Str.Format("Shell.Generate.Announce.MakingVideoFormat", seconds),
                   Str.Get("Shell.Generate.Announce.VideoMade"), Str.Get("Shell.Generate.Announce.NoVideo"));
    }

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var prompt = ToolArgs.Str(arguments, "prompt", "description");
        if (string.IsNullOrWhiteSpace(prompt)) return ToolResult.Error("no prompt", "Give a 'prompt' describing the clip.");

        var shape   = GenerateImageTool.Shape(ToolArgs.Str(arguments, "shape"), MediaShape.Landscape);
        if (shape == MediaShape.Square) shape = MediaShape.Landscape;
        var request = new VideoGenerationRequest(prompt, shape, Seconds(arguments));

        if (ToolArgs.Str(arguments, "first_frame") is { Length: > 0 } framePath)
        {
            if (!model.StartsFromImages) return ToolResult.Error("first frame unused", "This model does not start from a picture.");
            if (GeneratedFiles.ReadPicture(framePath, out var problem) is not { } picture) return ToolResult.Error("first frame unreadable", problem!);
            request = request with
            {
                FirstFrame = model.FirstFrameSize(shape) is var (width, height) ? FrameFit.Fit(picture, width, height) : picture,
            };
        }

        GeneratedMedia made;
        try
        {
            made = await model.GenerateVideoAsync(request, ct);
        }
        catch (LlmProviderException ex)
        {
            return ToolResult.Error("could not make it", ex.Message);
        }

        var saved = await files.KeepAsync(made.Media, MadeKind.Video, ToolArgs.Str(arguments, "path"), prompt, ct);
        return GeneratedFiles.Made(saved, made.Note, null);
    }

    /// <summary>The length asked for, made one the model has.</summary>
    private int Seconds(JsonObject arguments) => ToolArgs.IntOrNull(arguments, "seconds") is { } asked ? Nearest(model.Lengths, asked) : Default;

    internal static int Nearest(IReadOnlyList<int> lengths, int seconds) =>
        lengths.OrderBy(l => Math.Abs(l - seconds)).ThenByDescending(l => l).First();

    /// <summary>A clip takes minutes: it is made in the background.</summary>
    public bool RunsInBackground(JsonObject arguments) => true;
}
