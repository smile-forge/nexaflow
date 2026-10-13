using Google.GenAI;
using Google.GenAI.Types;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.Gemini;

/// <summary>
/// Google's image models. Gemini's own (gemini-…-image) draw from a prompt or work from up to three pictures, and may
/// say something about what they drew; Imagen draws from a prompt only.
/// </summary>
public sealed class GeminiImageGenerationProvider : IImageGenerationProvider
{
    /// <summary>How many pictures a Gemini image model is given to work from — the most it is documented to work well with.</summary>
    private const int GeminiSources = 3;

    private readonly GeminiConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;
    private Client?                             _client;

    public GeminiImageGenerationProvider(IBackgroundActivityManager activityManager, GeminiConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    public string Name => GeminiLlmProvider.ProviderName;

    public string? LastModelListError { get; private set; }

    private Client Client => _client ??= new Client(apiKey: _config.ApiKey);

    public DataResidency Residency => DataResidency.LeavesMachine;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    public int MaxSources => IsImagen(_model) ? 0 : GeminiSources;

    public bool Masks => false;

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var names = await GeminiModels.ListAsync(Client, IsImageModel, ct);
            LastModelListError = null;
            return names;
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    internal static bool IsImageModel(string model) =>
        IsImagen(model) || (model.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) && model.Contains("-image", StringComparison.OrdinalIgnoreCase));

    internal static bool IsImagen(string model) => model.StartsWith("imagen", StringComparison.OrdinalIgnoreCase);

    internal static string AspectRatio(MediaShape shape) => shape switch
    {
        MediaShape.Landscape => "16:9",
        MediaShape.Portrait  => "9:16",
        _                    => "1:1",
    };

    public async Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        if (request.Sources.Count > MaxSources)
            throw new ArgumentException($"{_model} works from at most {MaxSources} picture(s).", nameof(request));
        if (request.Mask is not null)
            throw new ArgumentException($"{_model} takes no mask.", nameof(request));

        var activity = _activityManager.StartActivity($"Gemini image ({_model})…");
        try
        {
            var made = IsImagen(_model) ? await DrawAsync(request, ct) : await ConverseAsync(request, ct);
            activity.Complete();
            return made;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity.Complete();
            throw;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw ex as LlmProviderException ?? new LlmProviderException($"Gemini image failed: {ex.Message}", ex);
        }
    }

    private async Task<GeneratedMedia> DrawAsync(ImageGenerationRequest request, CancellationToken ct)
    {
        var config   = new GenerateImagesConfig { NumberOfImages = 1, AspectRatio = AspectRatio(request.Shape) };
        var response = await Client.Models.GenerateImagesAsync(_model, request.Prompt, config, ct);
        var made     = response.GeneratedImages?.FirstOrDefault();
        if (made?.Image?.ImageBytes is not { Length: > 0 } bytes)
            throw new LlmProviderException(made?.RaiFilteredReason is { } reason ? $"the picture was withheld: {reason}" : "no picture came back");
        return new GeneratedMedia(new MediaBytes(bytes, made.Image.MimeType ?? "image/png"), made.EnhancedPrompt);
    }

    private async Task<GeneratedMedia> ConverseAsync(ImageGenerationRequest request, CancellationToken ct)
    {
        var config = new GenerateContentConfig
        {
            ResponseModalities = ["TEXT", "IMAGE"],
            ImageConfig        = new ImageConfig { AspectRatio = AspectRatio(request.Shape) },
        };
        var response = await Client.Models.GenerateContentAsync(_model, Ask(request), config, ct);
        return Read(response);
    }

    /// <summary>The prompt, then the pictures it speaks of.</summary>
    internal static Content Ask(ImageGenerationRequest request) => new()
    {
        Role  = "user",
        Parts =
        [
            new Part { Text = request.Prompt },
            .. request.Sources.Select(s => new Part { InlineData = new Blob { Data = s.Bytes, MimeType = s.MimeType } }),
        ],
    };

    /// <summary>The picture a reply holds, with whatever it said beside it.</summary>
    internal static GeneratedMedia Read(GenerateContentResponse response)
    {
        var candidate = response.Candidates?.FirstOrDefault();
        var parts     = candidate?.Content?.Parts ?? [];
        var picture   = parts.FirstOrDefault(p => p.InlineData is { Data.Length: > 0 } blob
                                                  && (blob.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false));
        var said = string.Join("\n", parts.Where(p => p.Thought != true && !string.IsNullOrWhiteSpace(p.Text)).Select(p => p.Text!.Trim()));
        if (picture?.InlineData is not { } blob)
            throw new LlmProviderException(said.Length > 0 ? $"no picture came back: {said}"
                                                            : $"no picture came back ({candidate?.FinishReason?.ToString() ?? "no answer"})");
        return new GeneratedMedia(new MediaBytes(blob.Data!, blob.MimeType!), said.Length > 0 ? said : null);
    }
}
