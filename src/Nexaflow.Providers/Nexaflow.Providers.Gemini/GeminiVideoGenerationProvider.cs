using Google.GenAI;
using Google.GenAI.Types;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.Gemini;

/// <summary>
/// Google's Veo models: a clip landscape or portrait, optionally starting from a picture of any size. The job is
/// started, watched until it is done and the MP4 downloaded. Veo 3 makes 4, 6 or 8 seconds at 720p, and 8 at 1080p or
/// — but for Veo 3.1 Lite — 4k; Veo 3.0 takes a seed. Veo 2 makes five to eight seconds at its one size.
/// </summary>
public sealed class GeminiVideoGenerationProvider : IVideoGenerationProvider
{
    /// <summary>How often a running job is asked whether it is done.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly GeminiConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;
    private Client?                             _client;

    public GeminiVideoGenerationProvider(IBackgroundActivityManager activityManager, GeminiConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
        Lengths          = LengthsOf(_model);
    }

    public string Name => GeminiLlmProvider.ProviderName;

    public string? LastModelListError { get; private set; }

    private Client Client => _client ??= new Client(apiKey: _config.ApiKey);

    public DataResidency Residency => DataResidency.LeavesMachine;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    public IReadOnlyList<int> Lengths { get; }

    public bool StartsFromImages => true;

    public VideoSettings Settings => SettingsOf(_model);

    /// <summary>What each Veo model makes a clip with.</summary>
    internal static VideoSettings SettingsOf(string model)
    {
        var veo2 = model.StartsWith("veo-2", StringComparison.OrdinalIgnoreCase);
        var lite = model.Contains("lite", StringComparison.OrdinalIgnoreCase);
        return new([new(16, 9), new(9, 16)])
        {
            Resolutions = veo2 ? [] :
            [
                new("720p"),
                new("1080p") { OnlyLengths = [8] },
                .. lite ? Array.Empty<VideoResolution>() : [new VideoResolution("4k") { OnlyLengths = [8] }],
            ],
            Seeds = model.StartsWith("veo-3.0", StringComparison.OrdinalIgnoreCase),
        };
    }

    public (int Width, int Height)? FirstFrameSize(MediaAspect aspect, string? resolution) => null;

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var names = await GeminiModels.ListAsync(Client, IsVideoModel, ct);
            LastModelListError = null;
            return names;
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    internal static bool IsVideoModel(string model) => model.StartsWith("veo", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<int> LengthsOf(string model) =>
        model.StartsWith("veo-2", StringComparison.OrdinalIgnoreCase) ? [5, 6, 7, 8] : [4, 6, 8];

    public async Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default)
    {
        if (!Lengths.Contains(request.Seconds))
                throw new ArgumentException($"{_model} makes clips of {string.Join(", ", Lengths)} seconds.", nameof(request));
            if (Settings.Refusal(request) is { } refused) throw new ArgumentException($"{_model}: {refused}", nameof(request));

        var activity = _activityManager.StartActivity($"Gemini video ({_model})…");
        try
        {
            var source = new GenerateVideosSource { Prompt = request.Prompt };
            if (request.FirstFrame is { } frame) source.Image = new Image { ImageBytes = frame.Bytes, MimeType = frame.MimeType };
            var config = new GenerateVideosConfig
            {
                NumberOfVideos  = 1,
                AspectRatio     = request.Aspect.ToString(),
                DurationSeconds = request.Seconds,
                Resolution      = request.Resolution,
                Seed            = request.Seed,
            };

            var job = await Client.Models.GenerateVideosAsync(_model, source, config, ct);
            while (job.Done != true)
            {
                await Task.Delay(PollInterval, ct);
                job = await Client.Operations.GetAsync(job, null, ct);
            }

            var clip = await ClipAsync(job, ct);
            activity.Complete();
            return new GeneratedMedia(clip);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity.Complete();
            throw;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw ex as LlmProviderException ?? new LlmProviderException($"Gemini video failed: {ex.Message}", ex);
        }
    }

    private async Task<MediaBytes> ClipAsync(GenerateVideosOperation job, CancellationToken ct)
    {
        var made = Made(job);
        if (made.Video!.VideoBytes is { Length: > 0 } inline) return new MediaBytes(inline, made.Video.MimeType ?? "video/mp4");

        await using var download = await Client.Files.DownloadAsync(made, null, ct);
        using var bytes = new MemoryStream();
        await download.CopyToAsync(bytes, ct);
        return new MediaBytes(bytes.ToArray(), made.Video.MimeType ?? "video/mp4");
    }

    /// <summary>The clip a finished job made, or why it made none.</summary>
    internal static GeneratedVideo Made(GenerateVideosOperation job)
    {
        if (job.Error is { Count: > 0 } error)
            throw new LlmProviderException(error.TryGetValue("message", out var message) ? $"{message}" : "the video could not be made");
        if (job.Response?.GeneratedVideos?.FirstOrDefault() is { Video: not null } made) return made;
        throw new LlmProviderException(job.Response?.RaiMediaFilteredReasons is [var reason, ..]
            ? $"the video was withheld: {reason}"
            : "no video came back");
    }
}
