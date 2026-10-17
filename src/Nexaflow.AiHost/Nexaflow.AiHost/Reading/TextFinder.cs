using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Speech;

namespace Nexaflow.AiHost.Reading;

/// <summary>
/// PaddleOCR's PP-OCRv5 mobile text detector (Apache-2.0): the probability of text at every pixel of a picture prepared
/// as <see cref="TextFinding.Prepare"/> prepares it. Five megabytes, on four CPU threads — a picture costs tens of
/// milliseconds, so no GPU is woken for it.
/// </summary>
internal sealed class TextFinder(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec { get; } = spec;

    /// <returns>The map, <paramref name="width"/> × <paramref name="height"/>, row by row.</returns>
    public float[] Find(float[] input, int width, int height)
    {
        using var results = session.Run([NamedOnnxValue.CreateFromTensor("x", new DenseTensor<float>(input, [1, 3, height, width]))]);
        return results.First().AsTensor<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class TextFinderRunner : IModelRunner
{
    public string Id => "text-finder";

    public IHostModel Load(ModelSpec spec) => new TextFinder(spec, CpuSessions.Open(spec, threads: 4));
}
