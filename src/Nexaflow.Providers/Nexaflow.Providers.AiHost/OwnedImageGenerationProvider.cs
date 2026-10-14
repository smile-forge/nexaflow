using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Pictures Nexaflow draws itself, in its AI host: Z-Image Turbo from a prompt. Its three parts load when a picture is
/// asked for — an image model is not kept resident beside the language and speech models — and stay until the room is
/// needed. Each denoising step is shown on the activity as it finishes. Nothing leaves this machine.
/// </summary>
public sealed class OwnedImageGenerationProvider : IImageGenerationProvider, ILocalModelCatalog, IAsyncDisposable
{
    /// <summary>The pixel size each shape is drawn at: about a megapixel, in whole patches the model takes.</summary>
    internal static (int Width, int Height) Size(MediaShape shape) => shape switch
    {
        MediaShape.Landscape => (1344, 768),
        MediaShape.Portrait  => (768, 1344),
        _                    => (1024, 1024),
    };

    private readonly OwnedModelRuntime _runtime;
    private readonly OwnedModel?       _model;

    public OwnedImageGenerationProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup,
                                        ComputeHardwareReport hardware, ProviderModel model)
    {
        _runtime = new OwnedModelRuntime(activities, assets, host, setup, hardware);
        _model   = OwnedModels.OfKind(ModelKind.ImageGeneration).FirstOrDefault(m => m.Id == model.Model);
        assets.Changed += OnAssetChanged;
    }

    public string Name => OwnedLlmProvider.ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;

    /// <summary>The image models this machine can run: they are built for CUDA only.</summary>
    public IReadOnlyList<CatalogModel> Catalog => [.. _runtime.Runnable(ModelKind.ImageGeneration).Select(_runtime.CatalogOf)];

    public IReadOnlyList<UnrunnableModel> Unrunnable => [.. _runtime.Unrunnable(ModelKind.ImageGeneration)];

    public LocalRuntime? RuntimeOf(string modelId) => _runtime.Runtime;

    public bool LoadsOnDemand => true;

    public IReadOnlyList<PlacementNeed> NeedsOf(string modelId)
        => OwnedModels.OfKind(ModelKind.ImageGeneration).FirstOrDefault(m => m.Id == modelId) is { } model ? _runtime.NeedsOf(model) : [];

    public bool IsAvailable => _model is not null && _runtime.IsHere(_model);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _runtime.IsFileOf(_model, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public int MaxSources => 0;

    public bool Masks => false;

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. _runtime.Runnable(ModelKind.ImageGeneration).Select(m => m.Id)]);

    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    /// <summary>Draws at the job priority: speech and replies take the GPU between its steps.</summary>
    public async Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{Name} has no image model of that name.");
        if (request.Sources.Count > 0 || request.Mask is not null) throw new ArgumentException($"{_model.Catalog.DisplayName} draws from a prompt only.", nameof(request));
        if (_model.Sampler is not { } sampler) throw new LlmProviderException($"{_model.Id} does not say how it samples.");

        var (width, height) = Size(request.Shape);
        var drawing = new DrawingModel(await _runtime.PlacedOnDemandAsync(_model, OwnedRoles.PromptEncoder, ct),
                                       await _runtime.PlacedOnDemandAsync(_model, OwnedRoles.Denoiser, ct),
                                       await _runtime.PlacedOnDemandAsync(_model, OwnedRoles.Decoder, ct), sampler.Steps, sampler.Shift);
        long seed = Random.Shared.NextInt64();

        var label    = $"Drawing with {_model.Catalog.DisplayName}";
        var activity = _runtime.Activities.StartActivity(label + "…");
        var host     = _runtime.Host;
        host.Environment = _runtime.Environment;
        try
        {
            byte[]? png = null;
            await foreach (var reply in host.SendAsync(id => new Draw(id, drawing, request.Prompt, width, height, seed, RequestPriority.Job),
                                                       OwnedModelRuntime.StallLimit, ct))
            {
                switch (reply)
                {
                    case Progress { Total: > 0 } progress:
                        activity.Update($"{label} — step {progress.Done} of {progress.Total}");
                        break;
                    case Drawn drawn:
                        png = drawn.Png;
                        break;
                }
            }
            if (png is null) throw new LlmProviderException("The AI host finished without a picture.");
            activity.Complete();
            return new GeneratedMedia(new MediaBytes(png, "image/png"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity.Complete();
            throw;
        }
        catch (AiHostException ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"{Name} could not draw it: {ex.Message}", ex);
        }
        catch (LlmProviderException ex)
        {
            activity.Fail(ex.Message);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        _runtime.Assets.Changed -= OnAssetChanged;
        return ValueTask.CompletedTask;
    }
}
