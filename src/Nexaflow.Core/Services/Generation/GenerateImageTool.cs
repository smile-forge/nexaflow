using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// Draws a picture with the workspace's Image Generation model — from a prompt, or from pictures given to it where the
/// model works from them — then saves and opens it, and shows it to the model.
/// </summary>
internal sealed class GenerateImageTool(IImageGenerationProvider model, GeneratedFiles files) : IClientTool
{
    public string Name => "generate_image";

    public string Description => model.MaxSources > 0
        ? "Draws a picture from a description, or changes, restyles or combines pictures given to it; saves it as a file and opens it. Describe it fully: subject, style, composition, light."
        : "Draws a picture from a description; saves it as a file and opens it. Describe it fully: subject, style, composition, light.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("prompt", model.MaxSources > 0 ? "What to draw, or what to change in the sources." : "What to draw."),
        new("shape", "The picture's proportions; square when not given.", Required: false, Choices: Shapes),
        .. model.MaxSources > 0
            ? [new ClientToolParameter("sources", $"Full paths of up to {model.MaxSources} PNG, JPEG or WebP picture(s) to work from.",
                                       Required: false, Type: "array", ItemType: "string")]
            : Array.Empty<ClientToolParameter>(),
        .. model.Masks
            ? [new ClientToolParameter("mask", "Full path of a PNG the size of the first source; its transparent part is what gets redrawn.", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        new("path", @"Where to save it: a folder, or a file path. Pictures\Nexaflow when not given.", Required: false),
    ];

    internal static readonly IReadOnlyList<string> Shapes = ["square", "landscape", "portrait"];

    public ToolSafety Safety => ToolSafety.SafeOperation;

    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var prompt = ToolArgs.Str(arguments, "prompt", "description");
        if (string.IsNullOrWhiteSpace(prompt)) return ToolResult.Error("no prompt", "Give a 'prompt' describing the picture.");

        var paths = arguments["sources"] is JsonArray listed ? listed.Select(s => s?.ToString()).OfType<string>().ToList() : [];
        if (paths.Count > model.MaxSources)
            return ToolResult.Error("too many sources", $"This model works from at most {model.MaxSources} picture(s).");

        var sources = new List<MediaBytes>();
        foreach (var path in paths)
        {
            if (GeneratedFiles.ReadPicture(path, out var problem) is not { } picture) return ToolResult.Error("source unreadable", problem!);
            sources.Add(picture);
        }

        MediaBytes? mask = null;
        if (ToolArgs.Str(arguments, "mask") is { Length: > 0 } maskPath)
        {
            if (!model.Masks || sources.Count == 0) return ToolResult.Error("mask unused", "A mask needs a model that takes one and a source to lay it over.");
            if (GeneratedFiles.ReadPicture(maskPath, out var problem) is not { } stencil) return ToolResult.Error("mask unreadable", problem!);
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

        var saved = await files.KeepAsync(made.Media, MadeKind.Picture, ToolArgs.Str(arguments, "path"), prompt, ct);
        return GeneratedFiles.Made(saved, made.Note, new ContextImage(made.Media.Bytes, made.Media.MimeType, System.IO.Path.GetFileName(saved)));
    }

    /// <summary>The shape a model asked for, or <paramref name="fallback"/> where it named none it has.</summary>
    internal static MediaShape Shape(string? asked, MediaShape fallback) =>
        Enum.TryParse<MediaShape>(asked, ignoreCase: true, out var shape) && Enum.IsDefined(shape) ? shape : fallback;
}
