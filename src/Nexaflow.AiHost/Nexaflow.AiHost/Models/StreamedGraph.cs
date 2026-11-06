using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>A graph a part runs, however it holds its weights: named inputs in, named outputs out, as a session runs.</summary>
internal interface IGraph : IDisposable
{
    IReadOnlyList<string> OutputNames { get; }

    /// <inheritdoc cref="InferenceSession.Run(RunOptions, IReadOnlyCollection{string}, IReadOnlyCollection{OrtValue}, IReadOnlyCollection{string})"/>
    IDisposableReadOnlyCollection<OrtValue> Run(RunOptions options, IReadOnlyCollection<string> inputNames, IReadOnlyCollection<OrtValue> inputs,
                                                IReadOnlyCollection<string> outputNames);
}

/// <summary>A graph in one session, its weights held where the session runs.</summary>
internal sealed class WholeGraph(InferenceSession session) : IGraph
{
    public IReadOnlyList<string> OutputNames => session.OutputNames;

    public IDisposableReadOnlyCollection<OrtValue> Run(RunOptions options, IReadOnlyCollection<string> inputNames, IReadOnlyCollection<OrtValue> inputs,
                                                       IReadOnlyCollection<string> outputNames)
        => session.Run(options, inputNames, inputs, outputNames);

    public void Dispose() => session.Dispose();
}

/// <summary>
/// How a graph is cut to be streamed (<see cref="StreamedGraph"/>): the file its weights are in, beside the graph, what the graph
/// gives, and its groups in the order they run — each a graph of its own, the tensors it reads from those before it and leaves for
/// those after, the graph's own outputs among them where it makes them, and the
/// weights it takes as inputs, where they lie in that file. Written by <c>tools/ai-models/streaming/split_graph.py</c>.
/// </summary>
internal sealed record StreamManifest(string Data, IReadOnlyList<string> Outputs, IReadOnlyList<StreamGroup> Groups)
{
    /// <summary>What a manifest's file name ends with: a part whose file is one is streamed.</summary>
    public const string Suffix = ".stream.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static StreamManifest Read(string path)
        => JsonSerializer.Deserialize<StreamManifest>(File.ReadAllText(path), Json) is { Groups.Count: > 0 } manifest ? manifest
           : throw new HostRequestException(HostErrorKind.Invalid, $"{path} names no group to run.");
}

/// <param name="Streamed">Its weights the graph takes as inputs, in the order they are laid out on the GPU.</param>
internal sealed record StreamGroup(string File, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs, IReadOnlyList<StreamedWeight> Streamed)
{
    [JsonIgnore]
    public long StreamedBytes => Streamed.Sum(w => w.Length);
}

/// <param name="Type">Its ONNX element type.</param>
internal sealed record StreamedWeight(string Name, int Type, long[] Shape, long Offset, long Length)
{
    [JsonIgnore]
    public TensorElementType ElementType => Type switch
    {
        1  => TensorElementType.Float,
        2  => TensorElementType.UInt8,
        3  => TensorElementType.Int8,
        10 => TensorElementType.Float16,
        16 => TensorElementType.BFloat16,
        _  => throw new HostRequestException(HostErrorKind.Invalid, $"{Name} is held as ONNX type {Type}, which the host does not stream."),
    };
}

/// <summary>
/// A graph whose weights are kept in this machine's memory and brought to its GPU a group at a time — a denoiser larger than a
/// card can hold whole. Its groups (<see cref="StreamManifest"/>) each run in a session of its own, every session sharing one
/// arena on the GPU, so only one group's scratch is held at once; each group's weights are read once into pinned memory, and
/// copied on a stream of their own into one of two buffers on the GPU while the group before runs from the other — so the card
/// holds two groups' weights, the graph's small ones, and one group's work. What the groups hand on stays on the GPU; only the
/// graph's own inputs and outputs cross the link.
/// </summary>
internal sealed class StreamedGraph : IGraph
{
    private readonly int _device;
    private readonly StreamManifest _manifest;
    private readonly InferenceSession[] _sessions;
    private readonly IntPtr[] _pinned;
    private readonly IntPtr[] _buffers;
    private readonly IntPtr _copies;
    private readonly OrtMemoryInfo _onDevice;
    private readonly object _gate = new();
    private bool _disposed;

    private StreamedGraph(int device, StreamManifest manifest, InferenceSession[] sessions, IntPtr[] pinned, IntPtr[] buffers, IntPtr copies, OrtMemoryInfo onDevice)
    {
        _device = device;
        _manifest = manifest;
        _sessions = sessions;
        _pinned = pinned;
        _buffers = buffers;
        _copies = copies;
        _onDevice = onDevice;
    }

    public IReadOnlyList<string> OutputNames => _manifest.Outputs;

    /// <summary>What it holds in this machine's memory: every group's weights, pinned.</summary>
    public long PinnedBytes => _manifest.Groups.Sum(g => g.StreamedBytes);

    /// <exception cref="HostRequestException">It is asked for on the CPU, or its manifest, a graph or its weights are not there.</exception>
    public static StreamedGraph Open(ModelSpec spec)
    {
        if (spec.Device is not { } device)
            throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} is streamed to a GPU, and was asked for on the CPU.");
        string manifestPath = Path.Combine(spec.Folder, spec.File!);
        if (!File.Exists(manifestPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id}'s {spec.File} is not in {spec.Folder}.");
        var manifest = StreamManifest.Read(manifestPath);
        string dataPath = Path.Combine(spec.Folder, manifest.Data);
        if (!File.Exists(dataPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id}'s weights, {manifest.Data}, are not in {spec.Folder}.");

        var sessions = new List<InferenceSession>();
        var pinned = new IntPtr[manifest.Groups.Count];
        var buffers = new IntPtr[2];
        IntPtr copies = IntPtr.Zero;
        var onDevice = new OrtMemoryInfo(OrtMemoryInfo.allocatorCUDA, OrtAllocatorType.DeviceAllocator, device, OrtMemType.Default);
        try
        {
            foreach (var group in manifest.Groups)
                sessions.Add(GraphSessions.Open(spec with { File = group.File }, sharedArena: true));

            using (var data = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                for (int g = 0; g < pinned.Length; g++)
                {
                    pinned[g] = CudaRuntime.PinnedAlloc(device, Math.Max(1, manifest.Groups[g].StreamedBytes));
                    long at = 0;
                    foreach (var weight in manifest.Groups[g].Streamed)
                    {
                        data.Position = weight.Offset;
                        unsafe { data.ReadExactly(new Span<byte>((byte*)pinned[g] + at, checked((int)weight.Length))); }
                        at += weight.Length;
                    }
                }

            long most = Math.Max(1, manifest.Groups.Max(g => g.StreamedBytes));
            for (int b = 0; b < 2; b++) buffers[b] = CudaRuntime.DeviceAlloc(device, most);
            copies = CudaRuntime.StreamCreate(device);
            var graph = new StreamedGraph(device, manifest, [.. sessions], pinned, buffers, copies, onDevice);
            graph.CopyIn(0);
            return graph;
        }
        catch
        {
            foreach (var session in sessions) session.Dispose();
            foreach (var p in pinned.Where(p => p != IntPtr.Zero)) CudaRuntime.PinnedFree(p);
            foreach (var b in buffers.Where(b => b != IntPtr.Zero)) CudaRuntime.DeviceFree(device, b);
            if (copies != IntPtr.Zero) CudaRuntime.StreamDestroy(device, copies);
            onDevice.Dispose();
            throw;
        }
    }

    /// <summary>Group <paramref name="group"/>'s buffer on the GPU: they take turns.</summary>
    private IntPtr Buffer(int group) => _buffers[group % 2];

    /// <summary>Starts copying group <paramref name="group"/>'s weights into its buffer.</summary>
    private void CopyIn(int group)
    {
        long bytes = _manifest.Groups[group].StreamedBytes;
        if (bytes > 0) CudaRuntime.CopyToDevice(_device, Buffer(group), _pinned[group], bytes, _copies);
    }

    public IDisposableReadOnlyCollection<OrtValue> Run(RunOptions options, IReadOnlyCollection<string> inputNames, IReadOnlyCollection<OrtValue> inputs,
                                                       IReadOnlyCollection<string> outputNames)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var finals = new HashSet<string>(_manifest.Outputs, StringComparer.Ordinal);
            var carried = new Dictionary<string, OrtValue>(StringComparer.Ordinal);
            foreach (var (name, value) in inputNames.Zip(inputs)) carried[name] = value;
            var made = new List<IDisposableReadOnlyCollection<OrtValue>>();
            var weights = new List<OrtValue>();
            bool handedOn = false;
            try
            {
                for (int g = 0; g < _sessions.Length; g++)
                {
                    var group = _manifest.Groups[g];
                    CudaRuntime.Wait(_device, _copies);                  // its weights are in its buffer
                    if (g + 1 < _sessions.Length) CopyIn(g + 1);         // the next group's go into the other as it runs

                    using var binding = _sessions[g].CreateIoBinding();
                    foreach (var name in group.Inputs)
                        binding.BindInput(name, carried.TryGetValue(name, out var value) ? value
                            : throw new HostRequestException(HostErrorKind.Invalid, $"The graph's group {g} reads {name}, which nothing before it gave."));
                    long offset = 0;
                    foreach (var weight in group.Streamed)
                    {
                        var value = OrtValue.CreateTensorValueWithData(_onDevice, weight.ElementType, weight.Shape, Buffer(g) + (nint)offset, weight.Length);
                        weights.Add(value);
                        binding.BindInput(weight.Name, value);
                        offset += weight.Length;
                    }
                    // What the graph gives comes back to this machine's memory from the group that makes it; the rest stays on the GPU.
                    foreach (var name in group.Outputs)
                        binding.BindOutputToDevice(name, finals.Contains(name) ? OrtMemoryInfo.DefaultInstance : _onDevice);

                    _sessions[g].RunWithBinding(options, binding);
                    var values = binding.GetOutputValues();
                    made.Add(values);
                    for (int o = 0; o < group.Outputs.Count; o++) carried[group.Outputs[o]] = values[o];
                }
                CopyIn(0);                                               // ready for the next run

                var asked = outputNames.Select(name => carried.TryGetValue(name, out var value) && finals.Contains(name) ? value
                    : throw new HostRequestException(HostErrorKind.Invalid, $"A streamed graph gives {string.Join(", ", _manifest.Outputs)}; {name} was asked for.")).ToList();
                var result = new GraphOutputs(asked, made.Where(values => values.Any(asked.Contains)).ToList());
                handedOn = true;
                foreach (var values in made.Where(values => !result.Owns(values))) values.Dispose();
                return result;
            }
            finally
            {
                foreach (var value in weights) value.Dispose();
                if (!handedOn) foreach (var values in made) values.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CudaRuntime.Wait(_device, _copies);
            foreach (var session in _sessions) session.Dispose();
            foreach (var buffer in _buffers.Where(b => b != IntPtr.Zero)) CudaRuntime.DeviceFree(_device, buffer);
            foreach (var p in _pinned) CudaRuntime.PinnedFree(p);
            CudaRuntime.StreamDestroy(_device, _copies);
            _onDevice.Dispose();
        }
    }
}

/// <summary>What a streamed graph gives, in the order asked: values its groups made, which it owns — and the rest of what those
/// groups made with them, let go with them.</summary>
internal sealed class GraphOutputs(IReadOnlyList<OrtValue> values, IReadOnlyList<IDisposableReadOnlyCollection<OrtValue>> owned)
    : IDisposableReadOnlyCollection<OrtValue>
{
    public bool Owns(IDisposableReadOnlyCollection<OrtValue> collection) => owned.Contains(collection);

    public OrtValue this[int index] => values[index];
    public int Count => values.Count;
    public IEnumerator<OrtValue> GetEnumerator() => values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        foreach (var collection in owned) collection.Dispose();
    }
}
