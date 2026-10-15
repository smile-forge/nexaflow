using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>A restored picture, in the file it was asked into.</summary>
/// <param name="FacesRestored">How many faces were found and remade in it.</param>
public sealed record RestoredPicture(int Width, int Height, int FacesRestored);

/// <summary>How far a restoration has got: tiles through the upscaler (<see cref="RestoringStages.Upscaling"/>), then
/// faces remade (<see cref="RestoringStages.Faces"/>).</summary>
public sealed record RestoringStep(string Stage, int Done, int Total);

/// <summary>
/// The picture tools Nexaflow runs in its AI host: a picture restored — made twice or four times its size, or kept at its
/// size and cleaned, its faces found and remade — with Real-ESRGAN and GFPGAN (<see cref="OwnedToolKind.PictureRestoration"/>).
/// Not a provider: a person or an assistant, local or in the cloud, uses the tool, and nothing in the cloud stands in for
/// it. Its models are fetched and placed as an owned model's are, and load only for a restoration.
/// </summary>
public sealed class OwnedPictureTools
{
    private readonly OwnedModelRuntime _runtime;
    private readonly OwnedTool? _tool;

    public OwnedPictureTools(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup, ComputeHardwareReport hardware)
        : this(new OwnedModelRuntime(activities, assets, host, setup, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == OwnedToolKind.PictureRestoration))
    {
    }

    internal OwnedPictureTools(OwnedModelRuntime runtime, OwnedTool? tool)
    {
        _runtime = runtime;
        _tool = tool;
        runtime.Assets.Changed += (_, asset) =>
        {
            if (_tool is not null && _runtime.IsFileOf(_tool, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Whether this machine can run them: there is a build for its runtime, and its GPU — if it has one — has
    /// room for the largest part, else the CPU takes it.</summary>
    public bool RunsHere => _tool is not null && _tool.RunsOn(_runtime.ExecutionProvider) && _runtime.Fits(_tool);

    /// <summary>The models as this machine fetches them; null where it cannot run them.</summary>
    public CatalogModel? Catalog => RunsHere ? _runtime.CatalogOf(_tool!) : null;

    /// <summary>The CUDA pack they run with where it suits this machine; null where they run on its CPU.</summary>
    public LocalRuntime? Runtime => _runtime.Runtime;

    /// <summary>Everything they need is here: their files, and the CUDA pack where it suits the machine.</summary>
    public bool IsReady => RunsHere && _runtime.IsReady(_tool!);

    /// <summary>Raised when one of their files arrives or goes.</summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>
    /// Restores the picture in <paramref name="pixels"/> into <paramref name="into"/>, both interleaved 8-bit RGBA with
    /// nothing else (<see cref="RestorePicture"/>), at <paramref name="scale"/> times its size, faces remade where
    /// <paramref name="faces"/>. Never downloads: the tools are fetched when a person asks for them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tools are not on this machine, there is no room to run them, or the
    /// host could not.</exception>
    public async Task<RestoredPicture> RestoreAsync(string pixels, int width, int height, int scale, bool faces, string into,
                                                    IProgress<RestoringStep>? progress, CancellationToken ct)
    {
        if (_tool is null || !IsReady) throw new InvalidOperationException("The picture tools are not on this machine yet.");
        var madeWay = new List<IDisposable>();   // the models that make way for this picture, back once it is done
        try
        {
            var parts = new Dictionary<string, ModelSpec>();
            foreach (var role in RestoringParts.Of(scale, faces))
                parts[role] = await _runtime.PlacedOnDemandAsync(_tool, _tool.Part(role, _runtime.ExecutionProvider).Name, ct);

            RestoredPicture? restored = null;
            await foreach (var reply in _runtime.Host.SendAsync(id => new RestorePicture(id, parts, pixels, width, height, scale, faces, into, RequestPriority.Job),
                                                                OwnedModelRuntime.StallLimit, ct))
            {
                switch (reply)
                {
                    case Progress { Total: > 0 } step:
                        progress?.Report(new RestoringStep(step.Stage, (int)step.Done, (int)step.Total));
                        break;
                    case MadeWay made:
                        madeWay.Add(_runtime.MakingWay(made.Models));
                        break;
                    case PictureRestored done:
                        restored = new RestoredPicture(done.Width, done.Height, done.FacesRestored);
                        break;
                }
            }
            return restored ?? throw new InvalidOperationException("The AI host finished without the restored picture.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            throw new InvalidOperationException($"The picture could not be restored: {ex.Message}", ex);
        }
        finally
        {
            foreach (var back in madeWay) back.Dispose();
        }
    }
}
