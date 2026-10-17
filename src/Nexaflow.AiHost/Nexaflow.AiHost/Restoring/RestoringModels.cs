using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Restoring;

/// <summary>
/// One of a picture's restoration parts: an ONNX graph taking planar RGB from 0 to 1, <c>[1, 3, height, width]</c> — each
/// graph does its model's own normalisation, so every part is handed the same thing.
/// </summary>
internal abstract class RestoringModel(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    protected IDisposableReadOnlyCollection<OrtValue> Run(float[] rgb, int width, int height, bool lastCall)
    {
        if (rgb.Length != 3 * width * height)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {width}x{height} picture is {3 * width * height} values of planar RGB, not {rgb.Length}.");
        using var input = OrtValue.CreateTensorValueFromMemory(rgb, [1, 3, height, width]);
        using var run = lastCall ? GraphSessions.Releasing(Spec) : new RunOptions();
        return session.Run(run, ["rgb"], [input], session.OutputNames);
    }

    public void Dispose() => session.Dispose();

    /// <summary><paramref name="model"/> once <paramref name="probe"/> has run it on a small input, tuning its kernels at
    /// load; disposed if it could not.</summary>
    public static T Loaded<T>(T model, Action<T> probe) where T : RestoringModel
    {
        try
        {
            probe(model);
            return model;
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }
}

/// <summary>Real-ESRGAN: a tile of planar RGB in, the same tile <see cref="Factor"/> times the size out.</summary>
internal sealed class PictureUpscaler(ModelSpec spec, InferenceSession session, int factor) : RestoringModel(spec, session)
{
    /// <summary>How many times the size a tile comes back: 2 or 4.</summary>
    public int Factor => factor;

    /// <param name="lastCall">The picture's last tile: the session gives its scratch back after it.</param>
    public float[] Upscale(float[] rgb, int width, int height, bool lastCall)
    {
        if (factor == 2 && (width % 2 != 0 || height % 2 != 0))
            throw new HostRequestException(HostErrorKind.Invalid, "A tile for the 2x upscaler is an even number of pixels each way.");
        using var outputs = Run(rgb, width, height, lastCall);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }
}

internal sealed class PictureUpscalerRunner : IModelRunner
{
    public string Id => "esrgan-upscaler";

    public IHostModel Load(ModelSpec spec)
    {
        var session = GraphSessions.Open(spec);
        try
        {
            // A 16-pixel tile both tunes the kernels at load and says what the graph multiplies the size by.
            using var input = OrtValue.CreateTensorValueFromMemory(new float[3 * 16 * 16], [1, 3, 16, 16]);
            using var run = GraphSessions.Releasing(spec);
            using var outputs = session.Run(run, ["rgb"], [input], session.OutputNames);
            long side = outputs[0].GetTensorTypeAndShape().Shape[3];
            if (side is not (32 or 64))
                throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} makes a 16-pixel tile {side} pixels wide: not an upscaler this host runs.");
            return new PictureUpscaler(spec, session, (int)side / 16);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }
}

/// <summary>
/// RetinaFace (ResNet-50): a picture in; for each of its priors, the regressions of a face's box and five points against
/// the prior, and how surely it is a face — <see cref="RetinaFaceDecoding"/> reads them.
/// </summary>
internal sealed class FaceDetector(ModelSpec spec, InferenceSession session) : RestoringModel(spec, session)
{
    public (float[] Boxes, float[] Scores, float[] Landmarks) Detect(float[] rgb, int width, int height)
    {
        using var outputs = Run(rgb, width, height, lastCall: true);
        return (outputs[0].GetTensorDataAsSpan<float>().ToArray(), outputs[1].GetTensorDataAsSpan<float>().ToArray(),
                outputs[2].GetTensorDataAsSpan<float>().ToArray());
    }
}

internal sealed class FaceDetectorRunner : IModelRunner
{
    public string Id => "retinaface-detector";

    public IHostModel Load(ModelSpec spec)
        => RestoringModel.Loaded(new FaceDetector(spec, GraphSessions.Open(spec)), d => d.Detect(new float[3 * 64 * 64], 64, 64));
}

/// <summary>GFPGAN v1.4: a face aligned to its 512-pixel template in, the face remade out, as planar RGB from 0 to 1.</summary>
internal sealed class FaceRestorer(ModelSpec spec, InferenceSession session) : RestoringModel(spec, session)
{
    public const int Size = 512;

    public float[] Restore(float[] face, bool lastCall)
    {
        using var outputs = Run(face, Size, Size, lastCall);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }
}

internal sealed class FaceRestorerRunner : IModelRunner
{
    public string Id => "gfpgan-restorer";

    public IHostModel Load(ModelSpec spec)
        => RestoringModel.Loaded(new FaceRestorer(spec, GraphSessions.Open(spec)), r => r.Restore(new float[3 * FaceRestorer.Size * FaceRestorer.Size], lastCall: true));
}

/// <summary>ParseNet: a remade face in; out, 1 over the part of it pasted back on the picture — skin, brows, eyes, nose,
/// mouth — and 0 over the rest, before any blur.</summary>
internal sealed class FaceParser(ModelSpec spec, InferenceSession session) : RestoringModel(spec, session)
{
    public float[] Mask(float[] face, bool lastCall)
    {
        using var outputs = Run(face, FaceRestorer.Size, FaceRestorer.Size, lastCall);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }
}

internal sealed class FaceParserRunner : IModelRunner
{
    public string Id => "face-parser";

    public IHostModel Load(ModelSpec spec)
        => RestoringModel.Loaded(new FaceParser(spec, GraphSessions.Open(spec)), p => p.Mask(new float[3 * FaceRestorer.Size * FaceRestorer.Size], lastCall: true));
}
