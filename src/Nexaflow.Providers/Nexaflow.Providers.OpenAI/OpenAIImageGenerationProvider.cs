using Nexaflow.Providers.Common;
using OpenAI;
using OpenAI.Images;

namespace Nexaflow.Providers.OpenAI;

#pragma warning disable OPENAI001 // the gpt-image qualities and backgrounds are marked for evaluation in the SDK

/// <summary>
/// OpenAI's image models. The gpt-image family draws from a prompt or works from one source picture, redrawing only
/// what a mask leaves transparent when one is given; dall-e-3 draws from a prompt only. Both answer in PNG.
/// </summary>
public sealed class OpenAIImageGenerationProvider : IImageGenerationProvider
{
    private readonly OpenAIConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;
    private OpenAIClient?                       _client;

    public OpenAIImageGenerationProvider(IBackgroundActivityManager activityManager, OpenAIConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    public string Name => OpenAILlmProvider.ProviderName;

    public string? LastModelListError { get; private set; }

    private OpenAIClient Client => _client ??= OpenAIClients.Create(_config);

    public DataResidency Residency => EndpointResidency.Of(_config.BaseUrl, DataResidency.LeavesMachine);

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    public int MaxSources => DrawsOnly(_model) ? 0 : 1;

    public bool Masks => MaxSources > 0;

    public ImageSettings Settings => SettingsOf(_model);

    /// <summary>
    /// What each model draws with: the gpt-image family at three levels of detail and on a transparent background when
    /// asked; dall-e-3 at standard and hd, taken as medium and high. Each draws one size of each shape (<see cref="Size"/>).
    /// </summary>
    internal static ImageSettings SettingsOf(string model) => DrawsOnly(model)
        ? new([new(1, 1), new(7, 4), new(4, 7)]) { Details = [ImageDetail.Medium, ImageDetail.High] }
        : new([new(1, 1), new(3, 2), new(2, 3)]) { Details = [ImageDetail.Low, ImageDetail.Medium, ImageDetail.High], TransparentBackgrounds = true };

    /// <summary>The quality each model is asked for at a level of detail.</summary>
    internal static GeneratedImageQuality Quality(string model, ImageDetail detail) => (DrawsOnly(model), detail) switch
    {
        (true, ImageDetail.High) => GeneratedImageQuality.High,
        (true, _)                => GeneratedImageQuality.Standard,
        (false, ImageDetail.Low) => GeneratedImageQuality.LowQuality,
        (false, ImageDetail.High) => GeneratedImageQuality.HighQuality,
        (false, _)               => GeneratedImageQuality.MediumQuality,
    };

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await Client.GetOpenAIModelClient().GetModelsAsync(ct);
            LastModelListError = null;
            return result.Value.Select(m => m.Id).Where(IsImageModel).Order().ToList();
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    /// <summary>The models the image endpoints take. dall-e-2 is left out: it draws only small square pictures.</summary>
    internal static bool IsImageModel(string model) =>
        model.Contains("gpt-image", StringComparison.OrdinalIgnoreCase) || model.Equals("dall-e-3", StringComparison.OrdinalIgnoreCase);

    /// <summary>dall-e-3 has no edit endpoint, and answers with a link unless asked for the bytes.</summary>
    internal static bool DrawsOnly(string model) => model.StartsWith("dall-e", StringComparison.OrdinalIgnoreCase);

    /// <summary>The size each model draws <paramref name="shape"/> at.</summary>
    internal static GeneratedImageSize Size(string model, MediaShape shape) => (DrawsOnly(model), shape) switch
    {
        (_, MediaShape.Square)        => new GeneratedImageSize(1024, 1024),
        (false, MediaShape.Landscape) => new GeneratedImageSize(1536, 1024),
        (false, _)                    => new GeneratedImageSize(1024, 1536),
        (true, MediaShape.Landscape)  => new GeneratedImageSize(1792, 1024),
        (true, _)                     => new GeneratedImageSize(1024, 1792),
    };

    public async Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        if (request.Sources.Count > MaxSources)
                throw new ArgumentException($"{_model} works from at most {MaxSources} picture(s).", nameof(request));
            if (Settings.Refusal(request) is { } refused) throw new ArgumentException($"{_model}: {refused}", nameof(request));

        var activity = _activityManager.StartActivity($"OpenAI image ({_model})…");
        try
        {
            var image = request.Sources.Count == 0
                ? await DrawAsync(request, ct)
                : await EditAsync(request, request.Sources[0], ct);
            activity.Complete();
            return new GeneratedMedia(new MediaBytes(image.ImageBytes.ToArray(), "image/png"), image.RevisedPrompt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity.Complete();
            throw;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"OpenAI image failed: {ex.Message}", ex);
        }
    }

    private async Task<GeneratedImage> DrawAsync(ImageGenerationRequest request, CancellationToken ct)
    {
        var options = new ImageGenerationOptions { Size = Size(_model, request.Aspect.Shape) };
        if (request.Detail is { } detail) options.Quality = Quality(_model, detail);
        if (request.TransparentBackground) options.Background = GeneratedImageBackground.Transparent;
        if (DrawsOnly(_model)) options.ResponseFormat = GeneratedImageFormat.Bytes;
        return (await Client.GetImageClient(_model).GenerateImageAsync(request.Prompt, options, ct)).Value;
    }

    private async Task<GeneratedImage> EditAsync(ImageGenerationRequest request, MediaBytes source, CancellationToken ct)
    {
        var options = new ImageEditOptions { Size = Size(_model, request.Aspect.Shape) };
        if (request.Detail is { } detail) options.Quality = Quality(_model, detail);
        if (request.TransparentBackground) options.Background = GeneratedImageBackground.Transparent;
        var images  = Client.GetImageClient(_model);
        using var picture = new MemoryStream(source.Bytes, writable: false);
        if (request.Mask is not { } mask)
            return (await images.GenerateImageEditAsync(picture, FileName("source", source), request.Prompt, options, ct)).Value;

        using var stencil = new MemoryStream(mask.Bytes, writable: false);
        return (await images.GenerateImageEditAsync(picture, FileName("source", source), request.Prompt,
                                                    stencil, FileName("mask", mask), options, ct)).Value;
    }

    /// <summary>The endpoint reads a picture's format from its file name.</summary>
    internal static string FileName(string stem, MediaBytes media) => media.MimeType.ToLowerInvariant() switch
    {
        "image/jpeg" => stem + ".jpg",
        "image/webp" => stem + ".webp",
        _            => stem + ".png",
    };
}
