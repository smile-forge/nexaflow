using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Speech;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Checking;

/// <summary>
/// OwenElliott's image safety classifier, M (SwiftFormer-M, MIT): the chances a picture shows gore, sexual content, or
/// neither. Its published graph takes the whole picture at 224 × 224 in RGB values of 0 to 255 — its normalisation and
/// softmax are in the graph — and costs a few milliseconds on four CPU threads, so no GPU is woken for it.
/// </summary>
internal sealed class PictureSafetyClassifier(ModelSpec spec, InferenceSession session) : IHostModel
{
    /// <summary>The size the classifier reads a picture at, each way.</summary>
    public const int Side = 224;

    public ModelSpec Spec { get; } = spec;

    /// <summary>The chances, in the graph's order: gore (NSFL), sexual content (NSFW), neither (SFW).</summary>
    public (float Gore, float Sexual, float Neither) Check(RgbPicture picture)
    {
        using var results = session.Run([NamedOnnxValue.CreateFromTensor("image", new DenseTensor<float>(Planes(picture), [1, 3, Side, Side]))]);
        var chances = results.First().AsTensor<float>().ToArray();
        return (chances[0], chances[1], chances[2]);
    }

    /// <summary>The whole picture scaled to <see cref="Side"/> square — squeezed, not cropped, so nothing at its edges
    /// goes unseen — as three planes of 0 to 255.</summary>
    internal static float[] Planes(RgbPicture picture)
    {
        var scaled = PictureResampling.ResizeLanczos(picture.Rgb, picture.Width, picture.Height, 3, Side, Side);
        var planes = new float[3 * Side * Side];
        for (int i = 0; i < Side * Side; i++)
            for (int c = 0; c < 3; c++)
                planes[c * Side * Side + i] = scaled[i * 3 + c];
        return planes;
    }

    public void Dispose() => session.Dispose();
}

internal sealed class PictureSafetyClassifierRunner : IModelRunner
{
    public string Id => "picture-safety-classifier";

    public IHostModel Load(ModelSpec spec) => new PictureSafetyClassifier(spec, CpuSessions.Open(spec, threads: 4));
}
