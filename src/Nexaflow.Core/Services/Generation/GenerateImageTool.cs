using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// Draws a picture with the workspace's Image Generation model, or its Image Generation (HQ) model when a finished,
/// polished picture is asked for — from a prompt, or from pictures given to it where the model works from them — then
/// saves and opens it, and shows it to the model. A high-quality picture is slow and dearer, so it is approved first.
/// </summary>
internal sealed class GenerateImageTool : IClientTool
{
    private readonly IImageGenerationProvider? _standard;
    private readonly IImageGenerationProvider? _highQuality;
    private readonly GeneratedFiles _files;

    /// <summary>Either model may be missing, not both; with both, a <c>quality</c> argument chooses between them.</summary>
    public GenerateImageTool(IImageGenerationProvider? standard, IImageGenerationProvider? highQuality, GeneratedFiles files)
    {
        if (standard is null && highQuality is null) throw new ArgumentException("An image tool needs a model to draw with.");
        _standard    = standard;
        _highQuality = highQuality;
        _files       = files;
    }

    public string Name => "generate_image";

    private int MostSources => Math.Max(_standard?.MaxSources ?? 0, _highQuality?.MaxSources ?? 0);

    private bool OffersQuality => _standard is not null && _highQuality is not null;

    public string Description => (MostSources > 0
        ? "Draws a picture from a description, or changes, restyles or combines pictures given to it, and opens it for the user."
        : "Draws a picture from a description and opens it for the user.")
        + " Describe it fully: subject, style, composition, light."
        + (OffersQuality ? " Use high quality only for a finished, polished picture the user asks for: it is slower and is approved first." : "");

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("prompt", MostSources > 0 ? "What to draw, or what to change in the sources." : "What to draw."),
        new("shape", "The picture's proportions; square when not given.", Required: false, Choices: Shapes),
        .. OffersQuality
            ? [new ClientToolParameter("quality", "Standard when not given.", Required: false, Choices: Qualities)]
            : Array.Empty<ClientToolParameter>(),
        .. MostSources > 0
            ? [new ClientToolParameter("sources", $"Full paths of up to {MostSources} PNG, JPEG or WebP picture(s) to work from: pictures open in a tab, or in a folder open in one.{SourcesNote}",
                                       Required: false, Type: "array", ItemType: "string")]
            : Array.Empty<ClientToolParameter>(),
        .. (_standard?.Masks ?? false) || (_highQuality?.Masks ?? false)
            ? [new ClientToolParameter("mask", $"Full path of a PNG the size of the first source; its transparent part is what gets redrawn.{MaskNote}", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        new("path", GenerationTools.SavedWhere, Required: false),
    ];

    internal static readonly IReadOnlyList<string> Shapes = ["square", "landscape", "portrait"];

    internal static readonly IReadOnlyList<string> Qualities = ["standard", "high"];

    /// <summary>Where the two qualities take different sources, which takes how many.</summary>
    private string SourcesNote => OffersQuality && _standard!.MaxSources != _highQuality!.MaxSources
        ? $" Standard takes {_standard.MaxSources}, high {_highQuality.MaxSources}."
        : "";

    private string MaskNote => OffersQuality && _standard!.Masks != _highQuality!.Masks
        ? _standard.Masks ? " Standard quality only." : " High quality only."
        : "";

    public ToolSafety Safety => ToolSafety.SafeOperation;

    public ToolSafety SafetyOf(JsonObject arguments) =>
        ReferenceEquals(Model(arguments), _highQuality) ? ToolSafety.RequiresApproval : ToolSafety.SafeOperation;

    public bool Parallelizable => true;

    public ToolCallAnnouncement? Announce(JsonObject arguments) =>
        ReferenceEquals(Model(arguments), _highQuality)
            ? new(Str.Get("Shell.Generate.Announce.Draw"), Str.Get("Shell.Generate.Announce.HighQualityPicture"),
                  Str.Get("Shell.Generate.Announce.DrawingHighQualityPicture"),
                  Str.Get("Shell.Generate.Announce.PictureMade"), Str.Get("Shell.Generate.Announce.NoPicture"),
                  Str.Get("Shell.Generate.Announce.DrawingInBackground"))
            : null;

    /// <summary>The model a call draws with: the high-quality one when asked for, or when it is the only one.</summary>
    private IImageGenerationProvider Model(JsonObject arguments) =>
        _standard is null || (_highQuality is not null && string.Equals(ToolArgs.Str(arguments, "quality"), "high", StringComparison.OrdinalIgnoreCase))
            ? _highQuality!
            : _standard;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var prompt = ToolArgs.Str(arguments, "prompt", "description");
        if (string.IsNullOrWhiteSpace(prompt)) return ToolResult.Error("no prompt", "Give a 'prompt' describing the picture.");

        if (_files.Refusal(ToolArgs.Str(arguments, "path")) is { } refused) return ToolResult.Error("not saved there", refused);

        var model = Model(arguments);
        var paths = arguments["sources"] is JsonArray listed ? listed.Select(s => s?.ToString()).OfType<string>().ToList() : [];
        if (paths.Count > model.MaxSources)
            return ToolResult.Error("too many sources", $"This quality works from at most {model.MaxSources} picture(s).");

        var sources = new List<MediaBytes>();
        foreach (var path in paths)
        {
            if (_files.ReadPicture(path, out var problem) is not { } picture) return ToolResult.Error("source unreadable", problem!);
            sources.Add(picture);
        }

        MediaBytes? mask = null;
        if (ToolArgs.Str(arguments, "mask") is { Length: > 0 } maskPath)
        {
            if (!model.Masks || sources.Count == 0) return ToolResult.Error("mask unused", "A mask needs a model that takes one and a source to lay it over.");
            if (_files.ReadPicture(maskPath, out var problem) is not { } stencil) return ToolResult.Error("mask unreadable", problem!);
            mask = stencil;
        }

        var request = new ImageGenerationRequest(prompt, Shape(ToolArgs.Str(arguments, "shape"), MediaShape.Square)) { Sources = sources, Mask = mask };
        GeneratedMedia made;
        try
        {
            made = await model.GenerateImageAsync(request, ct);
        }
        catch (LlmProviderException ex)
        {
            return ToolResult.Error("could not draw it", ex.Message);
        }

        var saved = await _files.KeepAsync(made.Media, MadeKind.Picture, ToolArgs.Str(arguments, "path"), prompt, ct);
        return GeneratedFiles.Made(saved, made.Note, new ContextImage(made.Media.Bytes, made.Media.MimeType, System.IO.Path.GetFileName(saved.Path)));
    }

    /// <summary>The shape a model asked for, or <paramref name="fallback"/> where it named none it has.</summary>
    internal static MediaShape Shape(string? asked, MediaShape fallback) =>
        Enum.TryParse<MediaShape>(asked, ignoreCase: true, out var shape) && Enum.IsDefined(shape) ? shape : fallback;

    /// <summary>A picture at high quality takes minutes: it is drawn in the background.</summary>
    public bool RunsInBackground(JsonObject arguments) => ReferenceEquals(Model(arguments), _highQuality);
}
