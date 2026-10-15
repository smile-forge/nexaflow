using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// Makes a clip with the workspace's Video Generation model — or, when the clip starts from a picture, its Video
/// Generation (HQ) model, which carries the picture on where a fast model made for descriptions would cut away from it —
/// then saves and opens it. A clip costs minutes and money, so it is approved before it is made.
/// </summary>
internal sealed class GenerateVideoTool : IClientTool
{
    private readonly IVideoGenerationProvider? _standard;
    private readonly IVideoGenerationProvider? _highQuality;
    private readonly GeneratedFiles _files;

    /// <summary>Either model may be missing, not both; with both, a picture to start from chooses between them.</summary>
    public GenerateVideoTool(IVideoGenerationProvider? standard, IVideoGenerationProvider? highQuality, GeneratedFiles files)
    {
        if (standard is null && highQuality is null) throw new ArgumentException("A video tool needs a model to make clips with.");
        _standard    = standard;
        _highQuality = highQuality;
        _files       = files;
    }

    public string Name => "generate_video";

    /// <summary>The model a clip from a description alone is made with: the standard one, else the high-quality one.</summary>
    private IVideoGenerationProvider FromDescription => _standard ?? _highQuality!;

    /// <summary>The model a clip from a picture is made with (<see cref="ForPictures"/>); null where none carries one on.</summary>
    private IVideoGenerationProvider? FromPicture => ForPictures(_standard, _highQuality);

    /// <summary>Of a workspace's two video models, the one a clip from a picture is made with: the high-quality one where it
    /// carries a picture on, else the standard one where it does; null where neither does.</summary>
    internal static IVideoGenerationProvider? ForPictures(IVideoGenerationProvider? standard, IVideoGenerationProvider? highQuality) =>
        highQuality is { StartsFromImages: true } ? highQuality : standard is { StartsFromImages: true } ? standard : null;

    public string Description => FromPicture is not null
        ? "Makes a short video clip from a description, optionally starting from a picture; saves it as a file and opens it. Takes minutes. Describe the action, the camera and the look."
        : "Makes a short video clip from a description; saves it as a file and opens it. Takes minutes. Describe the action, the camera and the look.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("prompt", "What happens in the clip."),
        new("shape", "Landscape or portrait; landscape when not given.", Required: false, Choices: Shapes),
        new("seconds", $"How long it runs: {Lengths(FromDescription)}{FromPictureLengths}; {Default(FromDescription)} when not given.",
            Required: false, Type: "integer"),
        .. FromPicture is not null
            ? [new ClientToolParameter("first_frame", "Full path of a PNG, JPEG or WebP picture the clip starts from.", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        new("path", @"Where to save it: a folder, or a file path. Videos\Nexaflow when not given.", Required: false),
    ];

    internal static readonly IReadOnlyList<string> Shapes = ["landscape", "portrait"];

    private static string Lengths(IVideoGenerationProvider model) => $"{string.Join(", ", model.Lengths)} seconds";

    /// <summary>Where a clip from a picture is made by a model of other lengths, which.</summary>
    private string FromPictureLengths => FromPicture is { } picture && !picture.Lengths.SequenceEqual(FromDescription.Lengths)
        ? $" ({Lengths(picture)} from a picture)"
        : "";

    /// <summary>The length a clip is made at when none is asked for: the one nearest eight seconds.</summary>
    private static int Default(IVideoGenerationProvider model) => Nearest(model.Lengths, 8);

    public ToolSafety Safety => ToolSafety.RequiresApproval;

    public bool Parallelizable => false;

    public ToolCallAnnouncement? Announce(JsonObject arguments)
    {
        var seconds = Seconds(Model(arguments), arguments);
        return new(Str.Get("Shell.Generate.Announce.Make"), Str.Format("Shell.Generate.Announce.VideoFormat", seconds),
                   Str.Format("Shell.Generate.Announce.MakingVideoFormat", seconds),
                   Str.Get("Shell.Generate.Announce.VideoMade"), Str.Get("Shell.Generate.Announce.NoVideo"),
                   Str.Get("Shell.Generate.Announce.MakingInBackground"));
    }

    private static bool StartsFromPicture(JsonObject arguments) => ToolArgs.Str(arguments, "first_frame") is { Length: > 0 };

    /// <summary>The model a call makes its clip with: the one for pictures when it starts from one, else the one for descriptions.</summary>
    private IVideoGenerationProvider Model(JsonObject arguments)
        => StartsFromPicture(arguments) && FromPicture is { } picture ? picture : FromDescription;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var prompt = ToolArgs.Str(arguments, "prompt", "description");
        if (string.IsNullOrWhiteSpace(prompt)) return ToolResult.Error("no prompt", "Give a 'prompt' describing the clip.");

        var model   = Model(arguments);
        var shape   = GenerateImageTool.Shape(ToolArgs.Str(arguments, "shape"), MediaShape.Landscape);
        if (shape == MediaShape.Square) shape = MediaShape.Landscape;
        var request = new VideoGenerationRequest(prompt, shape, Seconds(model, arguments));

        if (ToolArgs.Str(arguments, "first_frame") is { Length: > 0 } framePath)
        {
            if (!model.StartsFromImages) return ToolResult.Error("first frame unused", "No model here starts a clip from a picture.");
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

        var saved = await _files.KeepAsync(made.Media, MadeKind.Video, ToolArgs.Str(arguments, "path"), prompt, ct);
        return GeneratedFiles.Made(saved, made.Note, null);
    }

    /// <summary>The length asked for, made one <paramref name="model"/> has.</summary>
    private static int Seconds(IVideoGenerationProvider model, JsonObject arguments)
        => ToolArgs.IntOrNull(arguments, "seconds") is { } asked ? Nearest(model.Lengths, asked) : Default(model);

    internal static int Nearest(IReadOnlyList<int> lengths, int seconds) =>
        lengths.OrderBy(l => Math.Abs(l - seconds)).ThenByDescending(l => l).First();

    /// <summary>A clip takes minutes: it is made in the background.</summary>
    public bool RunsInBackground(JsonObject arguments) => true;
}
