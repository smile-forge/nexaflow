using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// One pass of MOSS-Image's denoiser: the latents, the prompt's rows and their rotary rows, the timestep's sinusoid,
/// whether block 22 is left out — as the negative prompt's pass leaves it — and a LoRA's strength in; the velocity the
/// sampler steps along out, [128, h, w]. A LoRA comes as an ONNX Runtime adapter active for the pass; without one, the
/// graph runs its plain path and its LoRA branches cost nothing.
/// </summary>
internal sealed class MossDenoiser : IHostModel
{
    private static readonly string[] Inputs = ["latents", "caption", "rope", "time", "skip", "lora_strength"];

    private readonly InferenceSession _session;
    private readonly OrtMemoryInfo? _gpu;
    private readonly OrtAllocator? _allocator;

    public MossDenoiser(ModelSpec spec, InferenceSession session)
    {
        Spec = spec;
        _session = session;
        if (spec.Device is not { } device) return;
        _gpu = new OrtMemoryInfo("Cuda", OrtAllocatorType.DeviceAllocator, device, OrtMemType.Default);
        _allocator = new OrtAllocator(session, _gpu);
    }

    public ModelSpec Spec { get; }

    /// <summary>The LoRA adapter at <paramref name="path"/>, its weights copied to the GPU once — not at every pass —
    /// where the denoiser runs on one; the caller lets it go when its picture is done.</summary>
    /// <exception cref="HostRequestException">The file is not there.</exception>
    public OrtLoraAdapter LoadLora(string path)
    {
        if (!File.Exists(path)) throw new HostRequestException(HostErrorKind.NotFound, $"The LoRA is not at {path}.");
        return OrtLoraAdapter.Create(path, _allocator);
    }

    /// <param name="caption">The prompt's rows, [rows, width] row-major.</param>
    /// <param name="rope">Its rotary rows (<see cref="MossSampler.Rope"/>).</param>
    /// <param name="time">The timestep's sinusoid (<see cref="MossSampler.TimeRows"/>).</param>
    /// <param name="skip">Leave out block 22: the negative prompt's pass.</param>
    /// <param name="lora">The adapter active for the pass, with <paramref name="strength"/>; null for none.</param>
    /// <param name="lastStep">The picture's last pass: the session gives its scratch memory back after it.</param>
    public float[] Velocity(float[] latents, int latentHeight, int latentWidth, float[] caption, int rows, float[] rope, float[] time,
                            bool skip, OrtLoraAdapter? lora, float strength, bool lastStep)
    {
        using var latentValue  = OrtValue.CreateTensorValueFromMemory(latents, [1, MossSampler.Channels, latentHeight, latentWidth]);
        using var captionValue = OrtValue.CreateTensorValueFromMemory(caption, [1, rows, caption.Length / rows]);
        using var ropeValue    = OrtValue.CreateTensorValueFromMemory(rope, [rope.Length / MossSampler.RopeWidth, MossSampler.RopeWidth]);
        using var timeValue    = OrtValue.CreateTensorValueFromMemory(time, [1, MossSampler.TimeWidth]);
        using var skipValue    = OrtValue.CreateTensorValueFromMemory(new[] { skip }, []);
        using var strengthValue = OrtValue.CreateTensorValueFromMemory(new[] { lora is null ? 0f : strength }, []);
        using var run = lastStep ? GraphSessions.Releasing(Spec) : new RunOptions();
        if (lora is not null) run.AddActiveLoraAdapter(lora);
        using var outputs = _session.Run(run, Inputs, [latentValue, captionValue, ropeValue, timeValue, skipValue, strengthValue], _session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose()
    {
        _allocator?.Dispose();
        _gpu?.Dispose();
        _session.Dispose();
    }
}

internal sealed class MossDenoiserRunner : IModelRunner
{
    public string Id => "moss-image-denoiser";

    public IHostModel Load(ModelSpec spec)
    {
        var denoiser = new MossDenoiser(spec, GraphSessions.Open(spec));
        try
        {
            // One pass at the common size, so CUDA tunes its kernels at load rather than on somebody's first picture.
            int h = 64, w = 64, rows = 32;
            denoiser.Velocity(new float[MossSampler.Channels * h * w], h, w, new float[rows * MossSampler.PromptWidth], rows, MossSampler.Rope(h, w, rows),
                              MossSampler.TimeRows(500f), skip: false, lora: null, strength: 0f, lastStep: true);
            return denoiser;
        }
        catch
        {
            denoiser.Dispose();
            throw;
        }
    }
}
