using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using System.Text.Json;
using Nexaflow.Providers.Common;
using OpenAI;
using OpenAI.Videos;

namespace Nexaflow.Providers.OpenAI;

#pragma warning disable OPENAI001 // the videos client is marked for evaluation in the SDK

/// <summary>
/// OpenAI's Sora models: a clip of 4, 8 or 12 seconds at 720p, landscape or portrait — sora-2-pro also at 1792 by 1024
/// either way up — optionally starting from a picture of exactly that size. The job is created, watched until it
/// finishes — its progress shown on the activity — and the MP4 downloaded.
/// </summary>
public sealed class OpenAIVideoGenerationProvider : IVideoGenerationProvider
{
    /// <summary>How often a running job is asked how far it has got.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly OpenAIConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;
    private OpenAIClient?                       _client;

    public OpenAIVideoGenerationProvider(IBackgroundActivityManager activityManager, OpenAIConfig config, ProviderModel model)
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

    public IReadOnlyList<int> Lengths { get; } = [4, 8, 12];

    public bool StartsFromImages => true;

    public VideoSettings Settings => SettingsOf(_model);

    /// <summary>The proportions each model makes clips in; neither takes a seed or a size beyond its proportions.</summary>
    internal static VideoSettings SettingsOf(string model) => model.StartsWith("sora-2-pro", StringComparison.OrdinalIgnoreCase)
        ? new([new(16, 9), new(9, 16), new(7, 4), new(4, 7)])
        : new([new(16, 9), new(9, 16)]);

    public (int Width, int Height)? FirstFrameSize(MediaAspect aspect, string? resolution) => Frame(aspect);

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await Client.GetOpenAIModelClient().GetModelsAsync(ct);
            LastModelListError = null;
            return result.Value.Select(m => m.Id).Where(IsVideoModel).Order().ToList();
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    internal static bool IsVideoModel(string model) => model.StartsWith("sora", StringComparison.OrdinalIgnoreCase);

    /// <summary>The frame each of its proportions is made at.</summary>
    internal static (int Width, int Height) Frame(MediaAspect aspect) => (aspect.Width, aspect.Height) switch
    {
        (16, 9) => (1280, 720),
        (9, 16) => (720, 1280),
        (7, 4)  => (1792, 1024),
        (4, 7)  => (1024, 1792),
        _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Sora makes no clips in those proportions."),
    };

    public async Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default)
    {
        if (!Lengths.Contains(request.Seconds))
                throw new ArgumentException($"{_model} makes clips of {string.Join(", ", Lengths)} seconds.", nameof(request));
            if (Settings.Refusal(request) is { } refused) throw new ArgumentException($"{_model}: {refused}", nameof(request));

        var label    = $"OpenAI video ({_model})";
        var activity = _activityManager.StartActivity(label + "…");
        try
        {
            var videos  = Client.GetVideoClient();
            var options = new RequestOptions { CancellationToken = ct };
            var (body, contentType) = await RequestBodyAsync(_model, request, ct);
            var job = ReadJob((await videos.CreateVideoAsync(BinaryContent.Create(body), contentType, options)).GetRawResponse().Content);

            while (job.Status is not ("completed" or "failed"))
            {
                activity.Update($"{label} — {job.Progress}%");
                await Task.Delay(PollInterval, ct);
                job = ReadJob((await videos.GetVideoAsync(job.Id, options)).GetRawResponse().Content);
            }
            if (job.Status == "failed") throw new LlmProviderException(job.Error ?? "the video could not be made");

            var clip = (await videos.DownloadVideoAsync(job.Id, "video", options)).GetRawResponse().Content.ToArray();
            activity.Complete();
            return new GeneratedMedia(new MediaBytes(clip, "video/mp4"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity.Complete();
            throw;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw ex as LlmProviderException ?? new LlmProviderException($"OpenAI video failed: {ex.Message}", ex);
        }
    }

    /// <summary>The job as a form — the one encoding that also carries the picture to start from.</summary>
    internal static async Task<(BinaryData Body, string ContentType)> RequestBodyAsync(string model, VideoGenerationRequest request, CancellationToken ct)
    {
        var (width, height) = Frame(request.Aspect);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(model), "model" },
            { new StringContent(request.Prompt), "prompt" },
            { new StringContent(request.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)), "seconds" },
            { new StringContent($"{width}x{height}"), "size" },
        };
        if (request.FirstFrame is { } frame)
        {
            var picture = new ByteArrayContent(frame.Bytes);
            picture.Headers.ContentType = new MediaTypeHeaderValue(frame.MimeType);
            form.Add(picture, "input_reference", OpenAIImageGenerationProvider.FileName("frame", frame));
        }
        var body = await form.ReadAsByteArrayAsync(ct);
        return (BinaryData.FromBytes(body), form.Headers.ContentType!.ToString());
    }

    /// <summary>A job as the videos endpoint reports it.</summary>
    internal sealed record VideoJob(string Id, string Status, int Progress, string? Error);

    internal static VideoJob ReadJob(BinaryData json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new VideoJob(
            root.GetProperty("id").GetString()!,
            root.GetProperty("status").GetString()!,
            root.TryGetProperty("progress", out var progress) && progress.ValueKind == JsonValueKind.Number ? progress.GetInt32() : 0,
            root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message) ? message.GetString() : null);
    }
}
