using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Selecting;

/// <summary>
/// What SAM 3.1's picture encoder made of a picture: the interactive head the click decoder reads — its embedding with
/// the no-memory embedding added, and two finer maps — and the three maps of the head the finder reads.
/// </summary>
internal sealed record PictureFeatures(float[] Embedding, float[] High0, float[] High1, float[] Fpn0, float[] Fpn1, float[] Fpn2)
{
    public static readonly long[] EmbeddingShape = [1, 256, 72, 72];
    public static readonly long[] High0Shape = [1, 32, 288, 288];
    public static readonly long[] High1Shape = [1, 64, 144, 144];
    public static readonly long[] Fpn0Shape = [1, 256, 288, 288];
    public static readonly long[] Fpn1Shape = [1, 256, 144, 144];
    public static readonly long[] Fpn2Shape = [1, 256, 72, 72];
}

/// <summary>One of SAM 3.1's graphs, run by name: each takes and gives fp32, whatever it holds inside.</summary>
internal abstract class SelectingModel(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    /// <summary>Runs the graph on <paramref name="inputs"/>, which it disposes; <paramref name="release"/> gives the
    /// session's scratch back after a large run.</summary>
    protected IDisposableReadOnlyCollection<OrtValue> Run(IReadOnlyList<string> names, IReadOnlyList<OrtValue> inputs, bool release)
    {
        try
        {
            using var run = release ? GraphSessions.Releasing(Spec) : new RunOptions();
            return session.Run(run, names, inputs, session.OutputNames);
        }
        finally
        {
            foreach (var input in inputs) input.Dispose();
        }
    }

    protected static OrtValue Tensor(float[] values, long[] shape) => OrtValue.CreateTensorValueFromMemory(values, shape);

    protected static float[] Floats(OrtValue value) => value.GetTensorDataAsSpan<float>().ToArray();

    public void Dispose() => session.Dispose();

    /// <summary><paramref name="model"/>, once <paramref name="probe"/> has run it to tune its kernels where it runs on a
    /// GPU; disposed if it could not.</summary>
    public static T Loaded<T>(T model, Action<T> probe) where T : SelectingModel
    {
        try
        {
            if (model.Spec.Device is not null) probe(model);
            return model;
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }
}

/// <summary>The picture, squashed to <see cref="Side"/> square as planar RGB from -1 to 1, read once.</summary>
internal sealed class PictureEncoder(ModelSpec spec, InferenceSession session) : SelectingModel(spec, session)
{
    public const int Side = 1008;

    public PictureFeatures Encode(float[] image)
    {
        if (image.Length != 3 * Side * Side) throw new HostRequestException(HostErrorKind.Invalid, $"The encoder reads {Side}x{Side} planar RGB.");
        using var o = Run(["image"], [Tensor(image, [1, 3, Side, Side])], release: true);
        return new PictureFeatures(Floats(o[0]), Floats(o[1]), Floats(o[2]), Floats(o[3]), Floats(o[4]), Floats(o[5]));
    }
}

internal sealed class PictureEncoderRunner : IModelRunner
{
    public string Id => "sam-image-encoder";

    public IHostModel Load(ModelSpec spec)
        => SelectingModel.Loaded(new PictureEncoder(spec, GraphSessions.Open(spec)), e => e.Encode(new float[3 * PictureEncoder.Side * PictureEncoder.Side]));
}

/// <summary>Clicks and a box's corners, in the encoder's square, to four masks at <see cref="Side"/> square — 0 for
/// several points, 1-3 for one — their predicted IoUs, and whether anything is there.</summary>
internal sealed class ClickDecoder(ModelSpec spec, InferenceSession session) : SelectingModel(spec, session)
{
    public const int Side = 288;

    public (float[] Masks, float[] Ious, float Object) Decode(PictureFeatures features, float[] points, float[] labels)
    {
        int n = labels.Length;
        if (n == 0 || points.Length != 2 * n) throw new HostRequestException(HostErrorKind.Invalid, "A selection needs a point, each with its label.");
        using var o = Run(["embedding", "high0", "high1", "points", "labels"],
                          [Tensor(features.Embedding, PictureFeatures.EmbeddingShape), Tensor(features.High0, PictureFeatures.High0Shape),
                           Tensor(features.High1, PictureFeatures.High1Shape), Tensor(points, [1, n, 2]), Tensor(labels, [1, n])], release: false);
        return (Floats(o[0]), Floats(o[1]), o[2].GetTensorDataAsSpan<float>()[0]);
    }
}

internal sealed class ClickDecoderRunner : IModelRunner
{
    public string Id => "sam-click-decoder";

    public IHostModel Load(ModelSpec spec)
        => SelectingModel.Loaded(new ClickDecoder(spec, GraphSessions.Open(spec)),
                                 d => d.Decode(new PictureFeatures(new float[256 * 72 * 72], new float[32 * 288 * 288], new float[64 * 144 * 144], [], [], []),
                                               [504, 504], [1]));
}

/// <summary>A phrase, through the makers' tokenizer, to the features the finder reads and which of them are padding.</summary>
internal sealed class TextEncoder(ModelSpec spec, InferenceSession session, ClipTokenizer tokenizer) : SelectingModel(spec, session)
{
    public (float[] Text, bool[] Padding) Encode(string phrase)
    {
        using var o = Run(["tokens"], [OrtValue.CreateTensorValueFromMemory(tokenizer.Tokens(phrase), [1, ClipTokenizer.ContextLength])], release: false);
        return (Floats(o[0]), o[1].GetTensorDataAsSpan<bool>().ToArray());
    }
}

internal sealed class TextEncoderRunner : IModelRunner
{
    public string Id => "sam-text-encoder";

    public IHostModel Load(ModelSpec spec)
    {
        string vocabulary = Path.Combine(spec.Folder, ClipTokenizer.VocabularyFile);
        if (!File.Exists(vocabulary)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id}'s vocabulary is not in {spec.Folder}.");
        return SelectingModel.Loaded(new TextEncoder(spec, GraphSessions.Open(spec), ClipTokenizer.Load(vocabulary)), t => t.Encode("a picture"));
    }
}

/// <summary>Example boxes — centre, width and height from 0 to 1 — to the tokens the finder reads them by.</summary>
internal sealed class ExemplarEncoder(ModelSpec spec, InferenceSession session) : SelectingModel(spec, session)
{
    public float[] Encode(PictureFeatures features, float[] boxes, long[] labels)
    {
        int n = labels.Length;
        if (n == 0 || boxes.Length != 4 * n) throw new HostRequestException(HostErrorKind.Invalid, "Each example is a box and its label.");
        using var o = Run(["fpn2", "boxes", "box_labels"],
                          [Tensor(features.Fpn2, PictureFeatures.Fpn2Shape), Tensor(boxes, [n, 1, 4]), OrtValue.CreateTensorValueFromMemory(labels, [n, 1])],
                          release: false);
        return Floats(o[0]);
    }
}

internal sealed class ExemplarEncoderRunner : IModelRunner
{
    public string Id => "sam-exemplar-encoder";

    public IHostModel Load(ModelSpec spec) => new ExemplarEncoder(spec, GraphSessions.Open(spec));
}

/// <summary>Everything a phrase's features and its examples' tokens name: for each of <see cref="Queries"/> queries,
/// its score and the presence score (logits), its box — centre, width and height from 0 to 1 — and its mask at
/// <see cref="ClickDecoder.Side"/> square.</summary>
internal sealed class Finder(ModelSpec spec, InferenceSession session) : SelectingModel(spec, session)
{
    public const int Queries = 200;

    public (float[] Logits, float Presence, float[] Boxes, float[] Masks) Find(PictureFeatures features, float[] text, bool[] padding, float[] exampleTokens)
    {
        int examples = exampleTokens.Length / 256;
        using var o = Run(["fpn0", "fpn1", "fpn2", "text", "text_pad", "box_tokens"],
                          [Tensor(features.Fpn0, PictureFeatures.Fpn0Shape), Tensor(features.Fpn1, PictureFeatures.Fpn1Shape),
                           Tensor(features.Fpn2, PictureFeatures.Fpn2Shape), Tensor(text, [ClipTokenizer.ContextLength, 1, 256]),
                           OrtValue.CreateTensorValueFromMemory(padding, [1, ClipTokenizer.ContextLength]),
                           Tensor(exampleTokens, [examples, 1, 256])], release: true);
        return (Floats(o[0]), o[1].GetTensorDataAsSpan<float>()[0], Floats(o[2]), Floats(o[3]));
    }
}

internal sealed class FinderRunner : IModelRunner
{
    public string Id => "sam-finder";

    public IHostModel Load(ModelSpec spec) => new Finder(spec, GraphSessions.Open(spec));
}
