using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// Draws a picture from a description with the workspace's Image Generation model, or its Image Generation (HQ) model
/// when a finished, polished picture is asked for, then saves and opens it, and shows it to the model; a picture changed
/// from pictures given to it is edit_image's (<see cref="EditImageTool"/>). What else a picture may be asked for with — its exact proportions,
/// size, detail, seed, what to keep out, a transparent background, a LoRA — is what the models' <see cref="ImageSettings"/>
/// offer; a LoRA meant for adults only is offered only while Enhanced AI Protection Mode is off, and while it is on every
/// picture passes its check (<see cref="IPictureCheck"/>) before it is saved or shown. A high-quality picture is slow and
/// dearer, so it is approved first.
/// </summary>
internal sealed class GenerateImageTool : IClientTool
{
    private readonly IImageGenerationProvider? _standard;
    private readonly IImageGenerationProvider? _highQuality;
    private readonly GeneratedFiles _files;
    private readonly bool _allowsAdultContent;
    private readonly IPictureCheck? _check;

    /// <summary>Either model may be missing, not both; with both, a <c>quality</c> argument chooses between them.</summary>
    /// <param name="allowsAdultContent">Enhanced AI Protection Mode is off: a LoRA meant for adults only is offered.</param>
    /// <param name="check">The check every picture passes while Enhanced AI Protection Mode is on; null while it is off.</param>
    public GenerateImageTool(IImageGenerationProvider? standard, IImageGenerationProvider? highQuality, GeneratedFiles files, bool allowsAdultContent = false,
                             IPictureCheck? check = null)
    {
        if (standard is null && highQuality is null) throw new ArgumentException("An image tool needs a model to draw with.");
        _standard           = standard;
        _highQuality        = highQuality;
        _files              = files;
        _allowsAdultContent = allowsAdultContent;
        _check              = check;
    }

    /// <summary>A model's settings as they are offered: a LoRA meant for adults only left out unless the app allows adult
    /// content — to a person setting a picture up and to the assistant alike.</summary>
    internal static ImageSettings Offerable(ImageSettings settings, bool allowsAdultContent) =>
        allowsAdultContent || !settings.Loras.Any(l => l.Adult) ? settings : settings with { Loras = [.. settings.Loras.Where(l => !l.Adult)] };

    private ImageSettings Offering(IImageGenerationProvider model) => Offerable(model.Settings, _allowsAdultContent);

    public string Name => "generate_image";

    private bool OffersQuality => _standard is not null && _highQuality is not null;

    /// <summary>The models' settings, standard first.</summary>
    private IEnumerable<ImageSettings> Offered => new[] { _standard, _highQuality }.OfType<IImageGenerationProvider>().Select(Offering);

    public string Description => "Draws a picture from a description and opens it for the user."
        + " Describe it fully: subject, style, composition, light."
        + (OffersQuality ? " Use high quality only for a finished, polished picture the user asks for: it is slower and is approved first." : "");

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("prompt", "What to draw."),
        new("shape", $"The picture's proportions: square, landscape or portrait, or exactly width:height.{Offer(s => s.Aspects)} Square when not given.",
            Required: false, Choices: [.. Shapes, .. Offered.SelectMany(s => s.Aspects).Select(a => a.ToString()).Distinct()]),
        .. OffersQuality
            ? [new ClientToolParameter("quality", "Standard when not given.", Required: false, Choices: Qualities)]
            : Array.Empty<ClientToolParameter>(),
        .. Offered.Any(s => s.Resolutions.Count > 0)
            ? [new ClientToolParameter("resolution", $"How large it is drawn.{Offer(s => s.Resolutions)} The model's own when not given.", Required: false,
                                       Choices: [.. Offered.SelectMany(s => s.Resolutions).Distinct(StringComparer.OrdinalIgnoreCase)])]
            : Array.Empty<ClientToolParameter>(),
        .. Offered.Any(s => s.Details.Count > 0)
            ? [new ClientToolParameter("detail", $"How much care is taken: more is slower and dearer.{Offer(s => s.Details.Select(DetailName).ToList())} The model's own when not given.",
                                       Required: false, Choices: [.. Offered.SelectMany(s => s.Details).Distinct().Order().Select(DetailName)])]
            : Array.Empty<ClientToolParameter>(),
        .. Offered.Any(s => s.Seeds)
            ? [new ClientToolParameter("seed", $"0 or more: the same seed and prompt draw the same picture again.{Only(s => s.Seeds)} A new one when not given.",
                                       Required: false, Type: "integer")]
            : Array.Empty<ClientToolParameter>(),
        .. Offered.Any(s => s.NegativePrompts)
            ? [new ClientToolParameter("negative_prompt", $"What to keep out of the picture.{Only(s => s.NegativePrompts)}", Required: false)]
            : Array.Empty<ClientToolParameter>(),
        .. Offered.Any(s => s.TransparentBackgrounds)
            ? [new ClientToolParameter("transparent_background", $"Draws it on a transparent background.{Only(s => s.TransparentBackgrounds)}",
                                       Required: false, Type: "boolean")]
            : Array.Empty<ClientToolParameter>(),
        .. Offered.Any(s => s.Loras.Count > 0)
            ? [new ClientToolParameter("lora", $"A LoRA to draw it with, one at a time — {LoraList}.{Only(s => s.Loras.Count > 0)} None when not given.",
                                       Required: false, Choices: [.. Offered.SelectMany(s => s.Loras).Select(l => l.Id).Distinct()])]
            : Array.Empty<ClientToolParameter>(),
        new("path", GenerationTools.SavedWhere, Required: false),
    ];

    internal static readonly IReadOnlyList<string> Shapes = ["square", "landscape", "portrait"];

    internal static readonly IReadOnlyList<string> Qualities = ["standard", "high"];

    /// <summary>A level of detail as the tool names it.</summary>
    internal static string DetailName(ImageDetail detail) => detail.ToString().ToLowerInvariant();

    /// <summary>Where the two qualities offer different choices, which offers which.</summary>
    private string Offer<T>(Func<ImageSettings, IReadOnlyList<T>> choices)
    {
        if (!OffersQuality) return "";
        var (standard, high) = (choices(Offering(_standard!)), choices(Offering(_highQuality!)));
        if (standard.SequenceEqual(high)) return "";
        static string Listed(IReadOnlyList<T> offered) => offered.Count == 0 ? "none" : string.Join(", ", offered);
        return $" Standard quality: {Listed(standard)}; high: {Listed(high)}.";
    }

    /// <summary>Each LoRA offered, by its id, and what it does.</summary>
    private string LoraList => string.Join("; ", Offered.SelectMany(s => s.Loras).DistinctBy(l => l.Id).Select(l => $"{l.Id}: {l.Description}"));

    /// <summary>Where only one quality offers something, which.</summary>
    private string Only(Func<ImageSettings, bool> offers) =>
        OffersQuality && offers(Offering(_standard!)) != offers(Offering(_highQuality!))
            ? offers(Offering(_standard!)) ? " Standard quality only." : " High quality only."
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
        if (Request(Offering(model), prompt, arguments, out var problem) is not { } asked) return ToolResult.Error("not offered", problem!);

        GeneratedMedia made;
        try
        {
            made = await model.GenerateImageAsync(asked, ct);
        }
        catch (LlmProviderException ex)
        {
            return ToolResult.Error("could not draw it", ex.Message);
        }

        return await PictureKeeping.KeptAsync(made, _check, _files, ToolArgs.Str(arguments, "path"), prompt, ct);
    }

    /// <summary>
    /// What <paramref name="arguments"/> ask a model with <paramref name="settings"/> to draw, or null with why it does not
    /// draw that — a shape named or exact, its own size and detail where none is asked for.
    /// </summary>
    internal static ImageGenerationRequest? Request(ImageSettings settings, string prompt, JsonObject arguments, out string? problem)
    {
        problem = null;
        if (Aspect(ToolArgs.Str(arguments, "shape"), MediaShape.Square, settings.AspectFor) is not { } proportions)
        {
            problem = $"'shape' is square, landscape, portrait or width:height, such as {string.Join(", ", settings.Aspects)}.";
            return null;
        }
        var aspect = settings.Aspects.FirstOrDefault(a => a == proportions, proportions);   // in the terms the model names it

        ImageDetail? detail = null;
        if (ToolArgs.Str(arguments, "detail") is { Length: > 0 } asked)
        {
            if (Enum.GetValues<ImageDetail>().Where(d => string.Equals(DetailName(d), asked.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Cast<ImageDetail?>().FirstOrDefault() is not { } named)
            {
                problem = $"'detail' is one of {string.Join(", ", settings.Details.Select(DetailName))}.";
                return null;
            }
            detail = named;
        }

        var request = new ImageGenerationRequest(prompt, aspect)
        {
            Resolution            = ToolArgs.Str(arguments, "resolution") is { Length: > 0 } size
                                        ? settings.Resolutions.FirstOrDefault(r => string.Equals(r, size, StringComparison.OrdinalIgnoreCase)) ?? size
                                        : null,
            Detail                = detail,
            Seed                  = ToolArgs.IntOrNull(arguments, "seed"),
            NegativePrompt        = ToolArgs.Str(arguments, "negative_prompt") is { Length: > 0 } keptOut ? keptOut.Trim() : null,
            TransparentBackground = ToolArgs.Bool(arguments, "transparent_background"),
            Lora                  = ToolArgs.Str(arguments, "lora") is { Length: > 0 } lora ? lora.Trim() : null,
        };
        problem = settings.Refusal(request);
        return problem is null ? request : null;
    }

    /// <summary>The proportions asked for — a shape, at the model's proportions for it (<paramref name="forShape"/>), or
    /// exactly width:height — or <paramref name="fallback"/>'s where nothing is asked; null for what is neither.</summary>
    internal static MediaAspect? Aspect(string? asked, MediaShape fallback, Func<MediaShape, MediaAspect> forShape)
    {
        if (string.IsNullOrWhiteSpace(asked)) return forShape(fallback);
        var named = asked.Trim();
        return Shapes.Contains(named, StringComparer.OrdinalIgnoreCase) ? forShape(Enum.Parse<MediaShape>(named, ignoreCase: true)) : MediaAspect.Parse(named);
    }

    /// <summary>A picture at high quality takes minutes: it is drawn in the background.</summary>
    public bool RunsInBackground(JsonObject arguments) => ReferenceEquals(Model(arguments), _highQuality);
}
