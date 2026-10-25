using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// A distillation's own sampler, which its LoRA brings in place of MOSS-Image's fifty guided steps. From the noise z
/// and a clean estimate x0 of zeros, each step asks the denoiser at z + Q·x0 — Q its latent mix, 128 × 128 over the
/// channels — with the step's code as the timestep, and takes z − v as the new estimate. It reads no negative prompt
/// and steers by nothing; the last estimate is the picture's latents. Read from the file the build recipe's
/// convert_moss_loras.py writes beside the adapter.
/// </summary>
internal sealed class DataSpaceSampler
{
    private const string Kind = "dataspace";

    /// <summary>Q, [out channel, in channel] row-major.</summary>
    private readonly float[] _mix;

    private DataSpaceSampler(IReadOnlyList<float> stepCodes, float[] mix)
    {
        StepCodes = stepCodes;
        _mix = mix;
    }

    /// <summary>The timestep the denoiser is shown at each step, in order.</summary>
    public IReadOnlyList<float> StepCodes { get; }

    /// <exception cref="HostRequestException">The file is not there, or is not a data-space sampler for 128 channels.</exception>
    public static DataSpaceSampler Read(string path)
    {
        if (!File.Exists(path)) throw new HostRequestException(HostErrorKind.NotFound, $"The LoRA's sampler is not at {path}.");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.GetProperty("kind").GetString() != Kind)
                throw new HostRequestException(HostErrorKind.Invalid, $"{path} is not a {Kind} sampler.");
            var codes = root.GetProperty("step_codes").EnumerateArray().Select(c => (float)c.GetDouble()).ToArray();
            var rows = root.GetProperty("latent_mix").EnumerateArray().Select(r => r.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray()).ToArray();
            if (codes.Length == 0) throw new HostRequestException(HostErrorKind.Invalid, $"{path} takes no step.");
            if (rows.Length != MossSampler.Channels || rows.Any(r => r.Length != MossSampler.Channels))
                throw new HostRequestException(HostErrorKind.Invalid, $"{path}'s latent mix is not {MossSampler.Channels} × {MossSampler.Channels}.");
            return new DataSpaceSampler(codes, [.. rows.SelectMany(r => r)]);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new HostRequestException(HostErrorKind.Invalid, $"{path} is not a sampler this host reads: {ex.Message}");
        }
    }

    /// <summary>Where a step asks the denoiser: z + Q·x0, [128, cells].</summary>
    public float[] Input(ReadOnlySpan<float> noise, ReadOnlySpan<float> clean)
    {
        int cells = noise.Length / MossSampler.Channels;
        var input = noise.ToArray();
        for (int o = 0; o < MossSampler.Channels; o++)
        {
            var into = input.AsSpan(o * cells, cells);
            for (int c = 0; c < MossSampler.Channels; c++)
            {
                float q = _mix[o * MossSampler.Channels + c];
                if (q != 0f) AddScaled(into, clean.Slice(c * cells, cells), q);
            }
        }
        return input;
    }

    /// <summary>The step's clean estimate, z − v, into <paramref name="clean"/>.</summary>
    public static void Estimate(Span<float> clean, ReadOnlySpan<float> noise, ReadOnlySpan<float> velocity)
    {
        for (int i = 0; i < clean.Length; i++) clean[i] = noise[i] - velocity[i];
    }

    /// <summary><paramref name="into"/> += <paramref name="scale"/> × <paramref name="values"/>, a vector at a time.</summary>
    private static void AddScaled(Span<float> into, ReadOnlySpan<float> values, float scale)
    {
        int whole = into.Length - into.Length % Vector<float>.Count;
        var target = MemoryMarshal.Cast<float, Vector<float>>(into[..whole]);
        var source = MemoryMarshal.Cast<float, Vector<float>>(values[..whole]);
        var factor = new Vector<float>(scale);
        for (int i = 0; i < target.Length; i++) target[i] += source[i] * factor;
        for (int i = whole; i < into.Length; i++) into[i] += values[i] * scale;
    }
}
