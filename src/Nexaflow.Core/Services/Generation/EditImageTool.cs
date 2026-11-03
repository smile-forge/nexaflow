using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// Changes pictures given to it with the workspace's Image Editing model — by an instruction, restyled, or several
/// combined, and, for a model that masks, drawn again only where a mask leaves the first transparent — then saves and
/// opens what it made, and shows it to the model. It reads only pictures the user has given the assistant, as every tool
/// that takes a path does; what else an edit is asked for with — a seed, what to keep out, its proportions where the model
/// does not keep the first picture's — is what the model's <see cref="ImageSettings"/> offer. An edit runs on as one of
/// the workspace's background jobs, and while Enhanced AI Protection Mode is on what it made passes the picture check
/// before it is saved or shown (<see cref="PictureKeeping"/>).
/// </summary>
internal sealed class EditImageTool : IClientTool
{
    private readonly IImageGenerationProvider _model;
    private readonly GeneratedFiles _files;
    private readonly IPictureCheck? _check;

    /// <param name="model">The Image Editing model: one that works from pictures.</param>
    /// <param name="check">The check every picture passes while Enhanced AI Protection Mode is on; null while it is off.</param>
    public EditImageTool(IImageGenerationProvider model, GeneratedFiles files, IPictureCheck? check = null)
    {
        if (model.MaxSources < 1) throw new ArgumentException("An editing model works from pictures.", nameof(model));
        _model = model;
        _files = files;
        _check = check;
    }

    public string Name => "edit_image";

    private ImageSettings Settings => _model.Settings;

    public string Description =>
        "Changes a picture given to it and opens what it made for the user: by an instruction — \"make it night\", \"turn it into a watercolour\", "
      + "\"give the cat a red collar\" — " + (_model.MaxSources > 1 ? $"or combining up to {_model.MaxSources} pictures — \"put the dog from the second at the table in the first\" — " : "")
      + (_model.Masks ? "or drawing again only what a mask leaves transparent. " : "")
      + (Settings.KeepsSourceProportions ? "What it makes keeps the first picture's proportions. " : "")
      + "Say what to change, not the whole picture again. To draw a picture from nothing, use generate_image.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("instruction", "What to change, or how to combine the pictures, in a sentence."),
        new("sources", $"Full paths of 1 to {_model.MaxSources} PNG, JPEG or WebP picture(s) to work from: pictures open in a tab, or in a folder open in one. The first is the one changed.",
            Type: "array", ItemType: "string"),
        .. _model.Masks
            ? [new ClientToolParameter("mask", "Full path of a PNG the size of the first picture: only its transparent part is drawn again. The whole picture is changed when not given.", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        .. Settings.KeepsSourceProportions
            ? Array.Empty<ClientToolParameter>()
            : [new ClientToolParameter("shape", "The picture's proportions: square, landscape or portrait, or exactly width:height. Square when not given.", Required: false,
                                       Choices: [.. GenerateImageTool.Shapes, .. Settings.Aspects.Select(a => a.ToString())])],
        .. Settings.Seeds
            ? [new ClientToolParameter("seed", "0 or more: the same seed, pictures and instruction make the same picture again. A new one when not given.", Required: false, Type: "integer")]
            : Array.Empty<ClientToolParameter>(),
        .. Settings.NegativePrompts
            ? [new ClientToolParameter("negative_prompt", "What to keep out of what it makes.", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        new("path", GenerationTools.SavedWhere, Required: false),
    ];

    public ToolSafety Safety => ToolSafety.SafeOperation;

    public bool Parallelizable => true;

    /// <summary>An edit runs on as one of the workspace's background jobs: the model is told it has started.</summary>
    public bool RunsInBackground(JsonObject arguments) => true;

    /// <summary>How an edit is named among the background jobs, told of when it ends, and announced in a voice conversation.</summary>
    public ToolCallAnnouncement? Announce(JsonObject arguments) =>
        new(Str.Get("Shell.Generate.Announce.Edit"), Str.Get("Shell.Generate.Announce.ThePicture"), Str.Get("Shell.Generate.Announce.EditingPicture"),
            Str.Get("Shell.Generate.Announce.PictureEdited"), Str.Get("Shell.Generate.Announce.NoEdit"), Str.Get("Shell.Generate.Announce.EditingInBackground"));

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var instruction = ToolArgs.Str(arguments, "instruction", "prompt");
        if (string.IsNullOrWhiteSpace(instruction)) return ToolResult.Error("no instruction", "Give an 'instruction' saying what to change.");
        if (_files.Refusal(ToolArgs.Str(arguments, "path")) is { } refused) return ToolResult.Error("not saved there", refused);

        var paths = arguments["sources"] is JsonArray listed ? listed.Select(s => s?.ToString()).OfType<string>().ToList() : [];
        if (paths.Count == 0) return ToolResult.Error("no sources", "Give 'sources': the picture to change, and any to work in.");
        if (paths.Count > _model.MaxSources)
            return ToolResult.Error("too many sources", $"The picture-editing model works from at most {_model.MaxSources} picture(s).");
        var sources = new List<MediaBytes>();
        foreach (var path in paths)
        {
            if (_files.ReadPicture(path, out var unread) is not { } picture) return ToolResult.Error("source unreadable", unread!);
            sources.Add(picture);
        }

        MediaBytes? mask = null;
        if (ToolArgs.Str(arguments, "mask") is { Length: > 0 } maskPath)
        {
            if (!_model.Masks) return ToolResult.Error("mask unused", "The picture-editing model takes no mask.");
            if (_files.ReadPicture(maskPath, out var unread) is not { } stencil) return ToolResult.Error("mask unreadable", unread!);
            mask = stencil;
        }

        MediaAspect? aspect = Settings.KeepsSourceProportions
            ? Settings.Aspects[0]
            : GenerateImageTool.Aspect(ToolArgs.Str(arguments, "shape"), MediaShape.Square, Settings.AspectFor) is { } proportions
                ? Settings.Aspects.FirstOrDefault(a => a == proportions, proportions)
                : null;
        if (aspect is null) return ToolResult.Error("not offered", $"'shape' is square, landscape, portrait or width:height, such as {string.Join(", ", Settings.Aspects)}.");

        var request = new ImageGenerationRequest(instruction.Trim(), aspect.Value)
        {
            Seed           = ToolArgs.IntOrNull(arguments, "seed"),
            NegativePrompt = ToolArgs.Str(arguments, "negative_prompt") is { Length: > 0 } keptOut ? keptOut.Trim() : null,
            Sources        = sources,
            Mask           = mask,
        };
        if (Settings.Refusal(request) is { } problem) return ToolResult.Error("not offered", problem);

        GeneratedMedia made;
        try
        {
            made = await _model.GenerateImageAsync(request, ct);
        }
        catch (LlmProviderException ex)
        {
            return ToolResult.Error("could not edit it", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Error("could not edit it", ex.Message);
        }
        return await PictureKeeping.KeptAsync(made, _check, _files, ToolArgs.Str(arguments, "path"), instruction, ct);
    }
}
