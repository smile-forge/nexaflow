using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Speech;

/// <summary>
/// Silero's voice activity detector: a 2 MB graph that judges each 32 ms frame, on one CPU thread whatever the host
/// runs on — a GPU call per frame would cost more than the frame. Each stream keeps its own recurrent state.
/// </summary>
internal sealed class SileroVoiceActivity(ModelSpec spec, InferenceSession session) : IHostModel
{
    /// <summary>The samples before each frame the model also reads.</summary>
    private const int Context = 64;
    private const int StateSize = 2 * 128;

    public ModelSpec Spec { get; } = spec;

    public IVoiceActivityStream Open() => new Stream(session);

    public void Dispose() => session.Dispose();

    private sealed class Stream(InferenceSession session) : IVoiceActivityStream
    {
        private readonly float[] _state = new float[StateSize];
        private readonly float[] _input = new float[Context + ListenSession.FrameSamples];
        private static readonly DenseTensor<long> Rate = new(new long[] { ListenSession.SampleRate }, []);

        public float Next(ReadOnlySpan<float> frame)
        {
            if (frame.Length != ListenSession.FrameSamples)
                throw new ArgumentException($"A frame is {ListenSession.FrameSamples} samples.", nameof(frame));

            // The context is the end of the previous frame, already at the front of the buffer.
            frame.CopyTo(_input.AsSpan(Context));
            using var results = session.Run(
            [
                NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(_input, [1, _input.Length])),
                NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(_state, [2, 1, 128])),
                NamedOnnxValue.CreateFromTensor("sr", Rate),
            ]);
            float probability = 0;
            foreach (var result in results)
            {
                if (result.Name == "stateN") result.AsTensor<float>().ToArray().CopyTo(_state, 0);
                else if (result.Name == "output") probability = result.AsTensor<float>().GetValue(0);
            }
            _input.AsSpan(_input.Length - Context).CopyTo(_input);
            return probability;
        }
    }
}

internal sealed class SileroVoiceActivityRunner : IModelRunner
{
    public string Id => "silero-vad";

    public IHostModel Load(ModelSpec spec, string executionProvider)
        => new SileroVoiceActivity(spec, CpuSessions.Open(spec, threads: 1));
}

/// <summary>Plain ONNX Runtime sessions for the small models that stay on the CPU.</summary>
internal static class CpuSessions
{
    public static InferenceSession Open(ModelSpec spec, int threads)
    {
        string path = Path.Combine(spec.Folder, spec.File ?? throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} does not name its file."));
        if (!File.Exists(path)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id}'s file is not there: {path}.");
        using var options = new SessionOptions
        {
            IntraOpNumThreads      = threads,
            InterOpNumThreads      = 1,
            ExecutionMode          = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel       = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        return new InferenceSession(path, options);
    }
}
