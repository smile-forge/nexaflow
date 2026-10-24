using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// Makes a clip with the workspace's Video Generation model — or, when the clip starts from a picture, its Video
/// Generation (HQ) model, which carries the picture on where a fast model made for descriptions would cut away from it —
/// then saves and opens it. What else a clip may be asked for with — its exact proportions, size, seed, what to keep out
/// — is what the model's <see cref="VideoSettings"/> offer. A clip costs minutes and money, so it is approved before it
/// is made.
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
    private IVideoGenerationProvider FromDescription => FromDescriptionOf(_standard, _highQuality)!;

    /// <summary>Of a workspace's two video models, the one a clip from a description alone is made with; null where it has neither.</summary>
    internal static IVideoGenerationProvider? FromDescriptionOf(IVideoGenerationProvider? standard, IVideoGenerationProvider? highQuality) =>
        standard ?? highQuality;

    /// <summary>The model a clip from a picture is made with (<see cref="ForPictures"/>); null where none carries one on.</summary>
    private IVideoGenerationProvider? FromPicture => ForPictures(_standard, _highQuality);

    /// <summary>Of a workspace's two video models, the one a clip from a picture is made with: the high-quality one where it
    /// carries a picture on, else the standard one where it does; null where neither does.</summary>
    internal static IVideoGenerationProvider? ForPictures(IVideoGenerationProvider? standard, IVideoGenerationProvider? highQuality) =>
        highQuality is { StartsFromImages: true } ? highQuality : standard is { StartsFromImages: true } ? standard : null;

    /// <summary>The models a call may make its clip with, the one for descriptions first.</summary>
    private IEnumerable<IVideoGenerationProvider> Offered => new[] { FromDescription, FromPicture }.OfType<IVideoGenerationProvider>().Distinct();

    public string Description => FromPicture is not null
        ? "Makes a short video clip from a description, optionally starting from a picture; saves it as a file and opens it. Takes minutes. Describe the action, the camera and the look."
        : "Makes a short video clip from a description; saves it as a file and opens it. Takes minutes. Describe the action, the camera and the look.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("prompt", "What happens in the clip."),
        new("shape", $"Its proportions: landscape or portrait, or exactly width:height.{FromPictureNote(m => string.Join(", ", m.Settings.Aspects))} Landscape when not given.",
            Required: false, Choices: [.. Shapes, .. Offered.SelectMany(m => m.Settings.Aspects).Select(a => a.ToString()).Distinct()]),
        new("seconds", $"How long it runs: {Lengths(FromDescription)}{FromPictureLengths}; {Default(FromDescription)} when not given.",
            Required: false, Type: "integer"),
        .. Offered.Any(m => m.Settings.Resolutions.Count > 0)
            ? [new ClientToolParameter("resolution", $"How large it is made: {FromPictureNote(m => Resolutions(m.Settings))}. The model's own when not given.",
                                       Required: false, Choices: [.. Offered.SelectMany(m => m.Settings.Resolutions).Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase)])]
            : Array.Empty<ClientToolParameter>(),
        .. Offered.Any(m => m.Settings.Seeds)
            ? [new ClientToolParameter("seed", $"0 or more: the same seed and prompt make much the same clip again.{Only(s => s.Seeds)} A new one when not given.",
                                       Required: false, Type: "integer")]
            : Array.Empty<ClientToolParameter>(),
        .. Offered.Any(m => m.Settings.NegativePrompts)
            ? [new ClientToolParameter("negative_prompt", $"What to keep out of the clip.{Only(s => s.NegativePrompts)}", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        .. FromPicture is not null
            ? [new ClientToolParameter("first_frame", "Full path of a PNG, JPEG or WebP picture the clip starts from: one open in a tab, or in a folder open in one.", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        new("path", GenerationTools.SavedWhere, Required: false),
    ];

    internal static readonly IReadOnlyList<string> Shapes = ["landscape", "portrait"];

    private static string Lengths(IVideoGenerationProvider model) => $"{string.Join(", ", model.Lengths)} seconds";

    /// <summary>A model's sizes, each with the only lengths it makes at it.</summary>
    private static string Resolutions(VideoSettings settings) => settings.Resolutions.Count == 0
        ? "one size"
        : string.Join(", ", settings.Resolutions.Select(r => r.OnlyLengths.Count == 0 ? r.Name : $"{r.Name} ({string.Join(", ", r.OnlyLengths)} seconds only)"));

    /// <summary>What the model for descriptions offers, and where a clip from a picture is made by a model offering otherwise, that too.</summary>
    private string FromPictureNote(Func<IVideoGenerationProvider, string> offered)
    {
        var fromDescription = offered(FromDescription);
        return FromPicture is { } picture && !ReferenceEquals(picture, FromDescription) && offered(picture) is var other && other != fromDescription
            ? $" {fromDescription}; from a picture, {other}"
            : $" {fromDescription}";
    }

    /// <summary>Where only one of the two models offers something, which.</summary>
    private string Only(Func<VideoSettings, bool> offers) =>
        FromPicture is { } picture && !ReferenceEquals(picture, FromDescription) && offers(picture.Settings) != offers(FromDescription.Settings)
            ? offers(picture.Settings) ? " From a picture only." : " Not from a picture."
            : "";

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

        if (_files.Refusal(ToolArgs.Str(arguments, "path")) is { } refused) return ToolResult.Error("not saved there", refused);

        var model = Model(arguments);
        if (Request(model, prompt, arguments, out var problem) is not { } request) return ToolResult.Error("not offered", problem!);

        if (ToolArgs.Str(arguments, "first_frame") is { Length: > 0 } framePath)
        {
            if (!model.StartsFromImages) return ToolResult.Error("first frame unused", "No model here starts a clip from a picture.");
            if (_files.ReadPicture(framePath, out problem) is not { } picture) return ToolResult.Error("first frame unreadable", problem!);
            request = request with
            {
                FirstFrame = model.FirstFrameSize(request.Aspect, request.Resolution) is var (width, height) ? FrameFit.Fit(picture, width, height) : picture,
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

    /// <summary>
    /// What <paramref name="arguments"/> ask <paramref name="model"/> to make, or null with why it does not make that — a
    /// shape named or exact, at a length it makes at the size asked for where none is asked.
    /// </summary>
    internal static VideoGenerationRequest? Request(IVideoGenerationProvider model, string prompt, JsonObject arguments, out string? problem)
    {
        problem = null;
        var settings = model.Settings;
        if (GenerateImageTool.Aspect(ToolArgs.Str(arguments, "shape"), MediaShape.Landscape, settings.AspectFor) is not { } asked)
        {
            problem = $"'shape' is landscape, portrait or width:height, such as {string.Join(", ", settings.Aspects)}.";
            return null;
        }
        var aspect = settings.Aspects.FirstOrDefault(a => a == asked, asked);   // in the terms the model names it

        var resolution = ToolArgs.Str(arguments, "resolution") is { Length: > 0 } size ? settings.Resolution(size)?.Name ?? size : null;
        var request = new VideoGenerationRequest(prompt, aspect, Seconds(model, arguments))
        {
            Resolution     = resolution,
            Seed           = ToolArgs.IntOrNull(arguments, "seed"),
            NegativePrompt = ToolArgs.Str(arguments, "negative_prompt") is { Length: > 0 } keptOut ? keptOut.Trim() : null,
        };
        problem = settings.Refusal(request);
        return problem is null ? request : null;
    }

    /// <summary>The length asked for, made one <paramref name="model"/> has; where none is asked, the default among those it
    /// makes at the size asked for.</summary>
    private static int Seconds(IVideoGenerationProvider model, JsonObject arguments)
    {
        if (ToolArgs.IntOrNull(arguments, "seconds") is { } asked) return Nearest(model.Lengths, asked);
        var only = model.Settings.Resolution(ToolArgs.Str(arguments, "resolution"))?.OnlyLengths ?? [];
        var fits = model.Lengths.Where(l => only.Count == 0 || only.Contains(l)).ToList();
        return Nearest(fits.Count > 0 ? fits : model.Lengths, 8);
    }

    internal static int Nearest(IReadOnlyList<int> lengths, int seconds) =>
        lengths.OrderBy(l => Math.Abs(l - seconds)).ThenByDescending(l => l).First();

    /// <summary>A clip takes minutes: it is made in the background.</summary>
    public bool RunsInBackground(JsonObject arguments) => true;
}
