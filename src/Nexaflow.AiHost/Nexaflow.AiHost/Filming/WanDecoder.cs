using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Tiling;

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

    /// <summary>A clip's decoding, its latents <paramref name="rows"/> × <paramref name="cols"/>, from its first latent frame: whole, or
    /// in tiles where its spec says so.</summary>
    public IClipDecoding Start(int rows, int cols)
        => Spec.TileCells > 0 ? new WanTiledDecoding(Spec, first, next, slots, rows, cols) : new WanDecoding(Spec, first, next, slots, rows, cols);

    public void Dispose()
    {
        first.Dispose();
        next.Dispose();
    }
}

/// <summary>A clip's decoding, a latent frame at a time: its frames, as interleaved 8-bit RGB.</summary>
internal interface IClipDecoding : IDisposable
{
    /// <summary>The frames of <paramref name="latentFrame"/>, [frames, 16·rows, 16·cols, 3]: one for the clip's first latent frame,
    /// four for each after.</summary>
    /// <param name="latentFrame">[48, 1, rows, cols].</param>
    /// <param name="lastCall">The clip's last: the decoder gives its scratch memory back after it.</param>
    byte[] Next(float[] latentFrame, bool lastCall);
}

/// <summary>One clip's decoding: each call takes the clip's next latent frame and gives its frames, the cache carried
/// from call to call in two sets of buffers on the GPU, read from one and written to the other.</summary>
internal sealed class WanDecoding : IClipDecoding
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

/// <summary>
/// One clip's decoding in tiles (<see cref="ModelSpec.TileCells"/>), for a GPU that does not hold a whole frame's decoding: each
/// latent frame cut into overlapping tiles, each tile decoded with a cache of its own and the tiles' frames blended back
/// (<see cref="TileBlend"/>). A tile's cache spans the clip, so every tile's is kept in pinned memory between its calls — they
/// come to more than a whole frame's, where tiles overlap — and copied to the GPU for its next call, into one of two sets of
/// buffers while the tile before runs from the other; what a call writes is copied back. The GPU holds four tile-sized cache sets
/// and one tile's work.
/// </summary>
internal sealed class WanTiledDecoding : IClipDecoding
{
    private readonly ModelSpec _spec;
    private readonly int _device;
    private readonly InferenceSession _first, _next;
    private readonly IReadOnlyList<WanCacheSlot> _slots;
    private readonly int _rows, _cols;
    private readonly LatentTiles _grid;
    private readonly long _cacheBytes;
    private readonly IntPtr[] _parked;
    private readonly IntPtr[] _read = new IntPtr[2], _written = new IntPtr[2];
    private readonly IntPtr _copies;
    private readonly OrtMemoryInfo _onDevice;
    private int _calls;

    /// <summary>Pixels a latent cell decodes to, along each side.</summary>
    private const int Scale = 16;

    public WanTiledDecoding(ModelSpec spec, InferenceSession first, InferenceSession next, IReadOnlyList<WanCacheSlot> slots, int rows, int cols)
    {
        if (spec.Device is not { } device) throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} decodes in tiles on a GPU, and was asked for on the CPU.");
        (_spec, _device, _first, _next, _slots, _rows, _cols) = (spec, device, first, next, slots, rows, cols);
        _grid = LatentTiles.Over(cols, rows, spec.TileCells, Math.Max(1, spec.TileCells / 8));
        int side = _grid.Tiles.Max(t => Math.Max(t.Width, t.Height));
        _cacheBytes = CacheBytes(slots, side, side);
        _onDevice = new OrtMemoryInfo(OrtMemoryInfo.allocatorCUDA, OrtAllocatorType.DeviceAllocator, device, OrtMemType.Default);
        _parked = new IntPtr[_grid.Tiles.Count];
        try
        {
            for (int t = 0; t < _parked.Length; t++)
                _parked[t] = CudaRuntime.PinnedAlloc(device, CacheBytes(slots, _grid.Tiles[t].Height, _grid.Tiles[t].Width));
            for (int b = 0; b < 2; b++)
            {
                _read[b] = CudaRuntime.DeviceAlloc(device, _cacheBytes);
                _written[b] = CudaRuntime.DeviceAlloc(device, _cacheBytes);
            }
            _copies = CudaRuntime.StreamCreate(device);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>What a tile of <paramref name="rows"/> × <paramref name="cols"/> cells' cache holds: every slot's last two frames, in 16 bits.</summary>
    public static long CacheBytes(IReadOnlyList<WanCacheSlot> slots, int rows, int cols)
        => slots.Sum(slot => (long)slot.Channels * 2 * slot.Scale * rows * slot.Scale * cols * 2);

    /// <summary>One slot's place in a tile's cache, its values over <paramref name="at"/>, as the graph names and shapes it.</summary>
    private IEnumerable<(string Name, OrtValue Value)> Slots(IReadOnlyList<string> names, int first, IntPtr at, LatentTile tile)
    {
        long offset = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            long[] shape = [1, slot.Channels, 2, slot.Scale * tile.Height, slot.Scale * tile.Width];
            long bytes = shape.Aggregate(2L, (product, dim) => product * dim);
            yield return (names[first + i], OrtValue.CreateTensorValueWithData(_onDevice, TensorElementType.Float16, shape, at + (nint)offset, bytes));
            offset += bytes;
        }
    }

    public byte[] Next(float[] latentFrame, bool lastCall)
    {
        var session = _calls == 0 ? _first : _next;
        bool reads = _calls > 0;
        var blends = new List<TileBlend>();
        int frames = 0;
        if (reads) CudaRuntime.CopyToDevice(_device, _read[0], _parked[0], CacheBytes(_slots, _grid.Tiles[0].Height, _grid.Tiles[0].Width), _copies);
        for (int t = 0; t < _grid.Tiles.Count; t++)
        {
            var tile = _grid.Tiles[t];
            bool lastTile = t == _grid.Tiles.Count - 1;
            CudaRuntime.Wait(_device, _copies);              // this tile's cache is in, and the tile before's is out
            if (reads && !lastTile)                          // the next tile's goes in beside it as it runs
                CudaRuntime.CopyToDevice(_device, _read[(t + 1) % 2], _parked[t + 1], CacheBytes(_slots, _grid.Tiles[t + 1].Height, _grid.Tiles[t + 1].Width), _copies);

            using var latent = OrtValue.CreateTensorValueFromMemory(LatentTiling.Planar(latentFrame, WanSampler.Channels, _rows, _cols, tile),
                                                                    [1, WanSampler.Channels, 1, tile.Height, tile.Width]);
            using var binding = session.CreateIoBinding();
            var held = new List<OrtValue>();
            binding.BindInput("latent", latent);
            if (reads)
                foreach (var (name, value) in Slots(session.InputNames, 1, _read[t % 2], tile))
                {
                    held.Add(value);
                    binding.BindInput(name, value);
                }
            binding.BindOutputToDevice(session.OutputNames[0], OrtMemoryInfo.DefaultInstance);
            foreach (var (name, value) in Slots(session.OutputNames, 1, _written[t % 2], tile))
            {
                held.Add(value);
                binding.BindOutput(name, value);
            }

            // Scratch goes back after each first-graph call, and after the clip's last tile.
            using var run = !reads || lastCall && lastTile ? GraphSessions.Releasing(_spec) : new RunOptions();
            try
            {
                session.RunWithBinding(run, binding);
                using var values = binding.GetOutputValues();
                var rgb = values[0].GetTensorDataAsSpan<byte>();
                int plane = tile.Height * Scale * tile.Width * Scale * 3;
                frames = rgb.Length / plane;
                while (blends.Count < frames) blends.Add(new TileBlend(_grid, 3, Scale));
                for (int f = 0; f < frames; f++) blends[f].Add(tile, LatentTiling.PlanarOf(rgb.Slice(f * plane, plane)));
            }
            finally
            {
                foreach (var value in held) value.Dispose();
            }
            CudaRuntime.CopyToHost(_device, _parked[t], _written[t % 2], CacheBytes(_slots, tile.Height, tile.Width), _copies);
        }
        CudaRuntime.Wait(_device, _copies);
        _calls++;
        return [.. blends.SelectMany(blend => LatentTiling.RgbOf(blend.Result()))];
    }

    public void Dispose()
    {
        if (_copies != IntPtr.Zero)
        {
            CudaRuntime.Wait(_device, _copies);
            CudaRuntime.StreamDestroy(_device, _copies);
        }
        foreach (var buffer in _read.Concat(_written).Where(b => b != IntPtr.Zero)) CudaRuntime.DeviceFree(_device, buffer);
        foreach (var parked in _parked.Where(p => p != IntPtr.Zero)) CudaRuntime.PinnedFree(parked);
        _onDevice.Dispose();
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
