using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Clips Nexaflow makes itself, in its AI host: Wan2.2-TI2V-5B distilled to three steps (FastWan), for clips from a
/// description in seconds, and its fifty guided steps, for clips from a description or from a picture to start from —
/// at the size and frame rate it was trained on. Its parts load when a clip is asked for, each for its own stage, and stay until the
/// room is needed. Each denoising step and decoded stretch is shown on the activity as it finishes. Nothing leaves this
/// machine.
/// </summary>
public sealed class OwnedVideoGenerationProvider : IVideoGenerationProvider, ILocalModelCatalog, IAsyncDisposable
{
    private readonly OwnedModelRuntime _runtime;
    private readonly OwnedModel?       _model;

    public OwnedVideoGenerationProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup,
                                        ComputeHardwareReport hardware, ProviderModel model)
    {
        _runtime = new OwnedModelRuntime(activities, assets, host, setup, hardware);
        _model   = OwnedModels.OfKind(ModelKind.VideoGeneration).FirstOrDefault(m => m.Id == model.Model);
        assets.Changed += OnAssetChanged;
    }

    public string Name => OwnedLlmProvider.ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;

    /// <summary>The video models this machine can run: they are built for CUDA only.</summary>
    public IReadOnlyList<CatalogModel> Catalog => [.. _runtime.Runnable(ModelKind.VideoGeneration).Select(_runtime.CatalogOf)];

    public IReadOnlyList<UnrunnableModel> Unrunnable => [.. _runtime.Unrunnable(ModelKind.VideoGeneration)];

    public LocalRuntime? RuntimeOf(string modelId) => _runtime.Runtime;

    public bool LoadsOnDemand => true;

    public IReadOnlyList<PlacementNeed> NeedsOf(string modelId)
        => OwnedModels.OfKind(ModelKind.VideoGeneration).FirstOrDefault(m => m.Id == modelId) is { } model ? _runtime.NeedsOf(model) : [];

    public bool IsAvailable => _model is not null && _runtime.IsHere(_model);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _runtime.IsFileOf(_model, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The lengths it was trained to make, in seconds.</summary>
    public IReadOnlyList<int> Lengths => _model?.Filming?.Seconds ?? [];

    /// <summary>Whether its model carries a picture on into a clip; FastWan, distilled from text alone, does not.</summary>
    public bool StartsFromImages => _model?.Filming?.StartsFromPictures ?? false;

    /// <summary>Its model's shapes at the sizes it was trained on, from any seed, and kept clear of what it is told to keep out
    /// where it is guided; one of a name it does not have makes nothing, so it offers its first shape alone.</summary>
    public VideoSettings Settings => _model?.Filming is { } filming ? SettingsOf(filming) : new([new(16, 9)]);

    internal static VideoSettings SettingsOf(OwnedFilming filming) =>
        new([.. OwnedFilming.Shapes.Select(shape => MediaAspect.Of(filming.Sizes[shape].Width, filming.Sizes[shape].Height))])
        {
            Seeds           = true,
            NegativePrompts = filming.Method == FilmingMethod.Wan,
        };

    /// <summary>The clip's own size: the picture becomes its first frame.</summary>
    public (int Width, int Height)? FirstFrameSize(MediaAspect aspect, string? resolution) => _model?.Filming is { } filming ? SizeOf(filming, aspect) : null;

    /// <summary>The size it films <paramref name="aspect"/> at, or null where it films none in those proportions.</summary>
    private static (int Width, int Height)? SizeOf(OwnedFilming filming, MediaAspect aspect) =>
        OwnedFilming.Shapes.Select(shape => filming.Sizes[shape]).Where(size => new MediaAspect(size.Width, size.Height) == aspect)
                           .Select(size => ((int Width, int Height)?)size).FirstOrDefault();

    /// <summary>What a guided model steers away from: its own negative prompt, then what it is told to keep out.</summary>
    internal static string NegativePrompt(OwnedFilming filming, string? asked) =>
        string.IsNullOrWhiteSpace(asked) ? filming.NegativePrompt : $"{filming.NegativePrompt}, {asked.Trim()}";

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. _runtime.Runnable(ModelKind.VideoGeneration).Select(m => m.Id)]);

    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    /// <summary>Makes the clip at the job priority: speech and replies take the GPU between its steps.</summary>
    public async Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{Name} has no video model of that name.");
        if (_model.Filming is not { } filming) throw new LlmProviderException($"{_model.Id} does not say how it films.");
        if (!filming.Seconds.Contains(request.Seconds))
            throw new ArgumentException($"{_model.Catalog.DisplayName} makes clips of {string.Join(", ", filming.Seconds)} seconds, not {request.Seconds}.", nameof(request));

        if (SettingsOf(filming).Refusal(request) is { } refused) throw new ArgumentException($"{_model.Catalog.DisplayName}: {refused}", nameof(request));

        var (width, height) = SizeOf(filming, request.Aspect)!.Value;
        byte[]? firstFrame = null;
        if (request.FirstFrame is { } picture)
        {
            if (!filming.StartsFromPictures)
                throw new ArgumentException($"{_model.Catalog.DisplayName} makes clips from a description alone.", nameof(request));
            RgbPicture rgb;
            try { rgb = PictureDecoder.DecodeRgb(picture.Bytes); }
            catch (InvalidDataException ex) { throw new ArgumentException($"The picture to start from cannot be read: {ex.Message}", nameof(request), ex); }
            if ((rgb.Width, rgb.Height) != (width, height))
                throw new ArgumentException($"The picture to start from is {rgb.Width}x{rgb.Height}, not the clip's {width}x{height}.", nameof(request));
            firstFrame = rgb.Rgb;
        }

        var parts = new Dictionary<string, ModelSpec>();
        foreach (var role in FilmingParts.Of(filming.Method))
            parts[role] = await _runtime.PlacedOnDemandAsync(_model, role, ct);
        var model = new FilmingModel(filming.Method, parts, filming.Steps, filming.Shift, filming.Guidance, filming.Timesteps,
                                     NegativePrompt(filming, request.NegativePrompt));
        long seed = request.Seed ?? Random.Shared.NextInt64();

        var label    = $"Filming with {_model.Catalog.DisplayName}";
        var activity = _runtime.Activities.StartActivity(label + "…");
        var host     = _runtime.Host;
        host.Environment = _runtime.Environment;
        var madeWay  = new List<IDisposable>();   // the models that make way for this clip, back once it is done
        try
        {
            byte[]? mp4 = null;
            await foreach (var reply in host.SendAsync(id => new Film(id, model, request.Prompt, width, height, filming.Frames(request.Seconds),
                                                                         filming.FramesPerSecond, seed, RequestPriority.Job) { FirstFrame = firstFrame },
                                                       OwnedModelRuntime.StallLimit, ct))
            {
                switch (reply)
                {
                    case Progress { Stage: FilmingStages.Decoding, Total: > 0 } decoding:
                        activity.Update($"{label} — decoding, {100 * decoding.Done / decoding.Total}%");
                        break;
                    case Progress { Total: > 0 } progress:
                        activity.Update($"{label} — step {progress.Done} of {progress.Total}");
                        break;
                    case Filmed filmed:
                        mp4 = filmed.Mp4;
                        break;
                    case MadeWay made:
                        madeWay.Add(_runtime.MakingWay(made.Models));
                        break;
                }
            }
            if (mp4 is null) throw new LlmProviderException("The AI host finished without a clip.");
            activity.Complete();
            return new GeneratedMedia(new MediaBytes(mp4, "video/mp4"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity.Complete();
            throw;
        }
        catch (AiHostException ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"{Name} could not make it: {ex.Message}", ex);
        }
        catch (LlmProviderException ex)
        {
            activity.Fail(ex.Message);
            throw;
        }
        finally
        {
            foreach (var back in madeWay) back.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        _runtime.Assets.Changed -= OnAssetChanged;
        return ValueTask.CompletedTask;
    }
}
