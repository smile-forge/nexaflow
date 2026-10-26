using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Pictures Nexaflow draws itself, in its AI host, from a prompt: Z-Image Turbo for everyday pictures, HiDream-O1 and
/// MOSS-Image for high-quality ones — each at the sizes it was trained on, by the method its catalog entry names. Its parts load when a
/// picture is asked for — an image model is not kept resident beside the language and speech models — and stay until
/// the room is needed. Each denoising step is shown on the activity as it finishes. Nothing leaves this machine.
/// </summary>
public sealed class OwnedImageGenerationProvider : IImageGenerationProvider, ILocalModelCatalog, IAsyncDisposable
{
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

    /// <summary>Its model's shapes at the sizes it was trained on, from any seed, with a negative prompt and its LoRAs where
    /// it takes them; one of a name it does not have draws nothing, so it offers a square alone.</summary>
    public ImageSettings Settings => _model?.Sampler is { } ? SettingsOf(_model) : new([new(1, 1)]);

    internal static ImageSettings SettingsOf(OwnedModel model)
    {
        var sampler = model.Sampler!;
        return new([.. Enum.GetValues<MediaShape>().Select(shape => MediaAspect.Of(sampler.Sizes[shape].Width, sampler.Sizes[shape].Height))])
        {
            Seeds           = true,
            NegativePrompts = sampler.NegativePrompts,
            Loras           = [.. model.Loras.Select(l => l.Offer)],
        };
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. _runtime.Runnable(ModelKind.ImageGeneration).Select(m => m.Id)]);

    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    /// <summary>Draws at the job priority: speech and replies take the GPU between its steps.</summary>
    public async Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{Name} has no image model of that name.");
        if (request.Sources.Count > 0 || request.Mask is not null) throw new ArgumentException($"{_model.Catalog.DisplayName} draws from a prompt only.", nameof(request));
        if (_model.Sampler is not { } sampler) throw new LlmProviderException($"{_model.Id} does not say how it samples.");

        if (SettingsOf(_model).Refusal(request) is { } refused) throw new ArgumentException($"{_model.Catalog.DisplayName}: {refused}", nameof(request));

        var (width, height) = sampler.Sizes.Values.First(size => new MediaAspect(size.Width, size.Height) == request.Aspect);
        var parts = new Dictionary<string, ModelSpec>();
        foreach (var role in DrawingParts.Of(sampler.Method))
            parts[role] = await _runtime.PlacedOnDemandAsync(_model, role, ct);
        var drawing = new DrawingModel(sampler.Method, parts, sampler.Steps, sampler.Shift, sampler.Guidance);
        long seed = request.Seed ?? Random.Shared.NextInt64();

        // A LoRA's files come the first time a picture asks for it; its trigger leads the prompt it is drawn with.
        var lora = _model.Loras.FirstOrDefault(l => l.Offer.Id == request.Lora);
        DrawingLora? drawingLora = null;
        if (lora is not null)
        {
            var files = await _runtime.FetchedAsync(_model, $"the {lora.Offer.Name} LoRA for {_model.Catalog.DisplayName}", lora.Files, ct);
            drawingLora = new DrawingLora(files[0], lora.Strength, lora.Sampler is null ? null : files[1]);
        }
        string prompt = lora?.Prompted(request.Prompt) ?? request.Prompt;
        string? negative = string.IsNullOrWhiteSpace(request.NegativePrompt) ? null : request.NegativePrompt;

        var label    = $"Drawing with {_model.Catalog.DisplayName}";
        var activity = _runtime.Activities.StartActivity(label + "…");
        var host     = _runtime.Host;
        host.Environment = _runtime.Environment;
        var madeWay  = new List<IDisposable>();   // the models that make way for this picture, back once it is done
        try
        {
            byte[]? png = null;
            await foreach (var reply in host.SendAsync(id => new Draw(id, drawing, prompt, width, height, seed, RequestPriority.Job, negative, drawingLora),
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
                    case MadeWay made:
                        madeWay.Add(_runtime.MakingWay(made.Models));
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
