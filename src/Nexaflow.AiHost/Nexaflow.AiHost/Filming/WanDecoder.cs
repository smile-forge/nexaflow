using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Filming;

/// <summary>One slot of the decoder's cache: its channels, and its side as a multiple of the latents'.</summary>
internal sealed record WanCacheSlot(string Name, int Channels, int Scale);

/// <summary>
/// Wan2.2's VAE decoder, a latent frame at a time: the first latent frame to one picture, each after it to four, every
/// causal convolution keeping the last two frames it saw for the next call. Two graphs share its weights: the first
/// frame's, which starts the cache, and every later frame's. The cache — about 3 GB at 720p — lives in two sets of
/// buffers of the decoding's own on the GPU, each call reading one and writing the other: kept out of the sessions'
/// arenas, which would otherwise grow by a cache's worth of scattered blocks with every call. Its slots' shapes are the
/// graph's, in <c>caches.json</c> beside it. Each call gives its frames as interleaved 8-bit RGB, ready to encode.
/// </summary>
internal sealed class WanDecoder(ModelSpec spec, InferenceSession first, InferenceSession next, IReadOnlyList<WanCacheSlot> slots) : IHostModel
{
    public const string FirstGraph = "decoder-first.onnx";
    public const string CacheShapes = "caches.json";

    public ModelSpec Spec => spec;

    /// <summary>A clip's decoding, its latents <paramref name="rows"/> × <paramref name="cols"/>, from its first latent frame.</summary>
    public WanDecoding Start(int rows, int cols) => new(Spec, first, next, slots, rows, cols);

    public void Dispose()
    {
        first.Dispose();
        next.Dispose();
    }
}

/// <summary>One clip's decoding: each call takes the clip's next latent frame and gives its frames, the cache carried
/// from call to call in two sets of buffers on the GPU, read from one and written to the other.</summary>
internal sealed class WanDecoding : IDisposable
{
    private readonly ModelSpec _spec;
    private readonly InferenceSession _first, _next;
    private readonly int _rows, _cols;
    private readonly OrtMemoryInfo _memory;
    private readonly OrtAllocator _allocator;
    private readonly OrtValue[][] _caches;
    private int _calls;

    public WanDecoding(ModelSpec spec, InferenceSession first, InferenceSession next, IReadOnlyList<WanCacheSlot> slots, int rows, int cols)
    {
        (_spec, _first, _next, _rows, _cols) = (spec, first, next, rows, cols);
        _memory = spec.Device is { } device
            ? new OrtMemoryInfo(OrtMemoryInfo.allocatorCUDA, OrtAllocatorType.DeviceAllocator, device, OrtMemType.Default)
            : OrtMemoryInfo.DefaultInstance;
        _allocator = new OrtAllocator(next, _memory);
        _caches = new OrtValue[2][];
        for (int set = 0; set < 2; set++)
            _caches[set] = [.. slots.Select(slot => OrtValue.CreateAllocatedTensorValue(_allocator, TensorElementType.Float16,
                                                                                      [1, slot.Channels, 2, slot.Scale * rows, slot.Scale * cols]))];
    }

    /// <summary>The frames of <paramref name="latentFrame"/>, [frames, 16·rows, 16·cols, 3]: one for the clip's first
    /// latent frame, four for each after.</summary>
    /// <param name="latentFrame">[48, 1, rows, cols].</param>
    /// <param name="lastCall">The clip's last: the session gives its scratch memory back after it, as the first graph's
    /// does after its one call.</param>
    public byte[] Next(float[] latentFrame, bool lastCall)
    {
        var session = _calls == 0 ? _first : _next;
        var written = _caches[_calls % 2];
        var read    = _caches[(_calls + 1) % 2];
        using var latent = OrtValue.CreateTensorValueFromMemory(latentFrame, [1, WanSampler.Channels, 1, _rows, _cols]);
        using var binding = session.CreateIoBinding();
        binding.BindInput("latent", latent);
        if (_calls > 0)
        {
            var inputs = session.InputNames;
            for (int i = 1; i < inputs.Count; i++) binding.BindInput(inputs[i], read[i - 1]);
        }
        var outputs = session.OutputNames;
        binding.BindOutputToDevice(outputs[0], OrtMemoryInfo.DefaultInstance);
        for (int i = 1; i < outputs.Count; i++) binding.BindOutput(outputs[i], written[i - 1]);

        // The first graph runs once a clip, so its scratch goes back at once; the later one's at the clip's end.
        using var run = _calls == 0 || lastCall ? GraphSessions.Releasing(_spec) : new RunOptions();
        session.RunWithBinding(run, binding);
        using var values = binding.GetOutputValues();
        _calls++;
        return values[0].GetTensorDataAsSpan<byte>().ToArray();
    }

    public void Dispose()
    {
        foreach (var value in _caches.SelectMany(set => set)) value.Dispose();
        _allocator.Dispose();
        if (!ReferenceEquals(_memory, OrtMemoryInfo.DefaultInstance)) _memory.Dispose();
    }
}

internal sealed class WanDecoderRunner : IModelRunner
{
    public string Id => "wan-decoder";

    private sealed record Shapes(List<WanCacheSlot>? Caches);

    public IHostModel Load(ModelSpec spec)
    {
        string shapesPath = Path.Combine(spec.Folder, WanDecoder.CacheShapes);
        if (!File.Exists(shapesPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id} has no {WanDecoder.CacheShapes} in {spec.Folder}.");
        var slots = JsonSerializer.Deserialize<Shapes>(File.ReadAllText(shapesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Caches
                    ?? throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id}'s {WanDecoder.CacheShapes} names no cache.");

        var first = GraphSessions.Open(spec with { File = WanDecoder.FirstGraph });
        InferenceSession? next = null;
        try
        {
            next = GraphSessions.Open(spec);
            if (next.InputNames.Count != slots.Count + 1)
                throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id}'s graph takes {next.InputNames.Count - 1} cache slots, its {WanDecoder.CacheShapes} {slots.Count}.");
            var decoder = new WanDecoder(spec, first, next, slots);
            // Two latent frames of a small clip, so CUDA tunes both graphs' kernels at load rather than on somebody's first clip.
            using (var decoding = decoder.Start(4, 4))
            {
                var frame = new float[WanSampler.Channels * 4 * 4];
                decoding.Next(frame, lastCall: false);
                decoding.Next(frame, lastCall: true);
            }
            return decoder;
        }
        catch
        {
            first.Dispose();
            next?.Dispose();
            throw;
        }
    }
}
