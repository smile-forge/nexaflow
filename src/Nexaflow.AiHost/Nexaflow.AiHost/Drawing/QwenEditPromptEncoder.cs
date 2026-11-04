using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Qwen-Image-Edit's prompt encoder, Qwen2.5-VL-7B's language model: the instruction written as the pipeline writes it —
/// its system prompt, each picture's vision tokens, the instruction — tokenised by Qwen2.5's byte-level BPE (its
/// tokenizer.json, beside the graph), each picture's tokens where its placeholders are, numbered as the model numbers
/// them; out, the last hidden states after the system prompt, [tokens, 3584], which the denoiser reads.
/// </summary>
internal sealed class QwenEditPromptEncoder(ModelSpec spec, InferenceSession session, Tokenizer tokenizer) : IHostModel
{
    private static readonly string[] Inputs = ["input_ids", "vision_embeds", "positions"];

    public ModelSpec Spec => spec;

    /// <summary>The tokens <paramref name="instruction"/> is read as after <paramref name="pictures"/>, each its count of
    /// vision tokens.</summary>
    public long[] PromptIds(string instruction, IReadOnlyList<int> pictures) =>
        QwenEditSampler.Widened([.. tokenizer.EncodeToIds(QwenEditSampler.PromptText(instruction, pictures.Count)).Select(id => (long)id)], pictures);

    /// <param name="pictures">Each picture's vision tokens, [tokens, 3584], with its merged grid, in the prompt's order.</param>
    public float[] Encode(string instruction, IReadOnlyList<(float[] Embeds, int Rows, int Cols)> pictures)
    {
        var ids = PromptIds(instruction, [.. pictures.Select(p => p.Rows * p.Cols)]);
        var positions = VisionPromptPositions.Of(ids, QwenEditSampler.ImageToken, [.. pictures.Select(p => (p.Rows, p.Cols))]);
        float[] embeds = [.. pictures.SelectMany(p => p.Embeds)];
        int vision = embeds.Length / QwenEditPictureReader.TokenWidth;
        using var idValue = OrtValue.CreateTensorValueFromMemory(ids, [1, ids.Length]);
        using var embedValue = embeds.Length > 0
            ? OrtValue.CreateTensorValueFromMemory(embeds, [vision, QwenEditPictureReader.TokenWidth])
            : OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorElementType.Float, [0, QwenEditPictureReader.TokenWidth]);
        using var positionValue = OrtValue.CreateTensorValueFromMemory(positions, [3, ids.Length]);
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, Inputs, [idValue, embedValue, positionValue], session.OutputNames);
        var hidden = outputs[0].GetTensorDataAsSpan<float>();
        return hidden[(QwenEditSampler.TemplateTokens * QwenEditPictureReader.TokenWidth)..].ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class QwenEditPromptEncoderRunner : IModelRunner
{
    public string Id => "qwen-edit-prompt-encoder";

    public IHostModel Load(ModelSpec spec)
    {
        string tokenizerPath = Path.Combine(spec.Folder, "tokenizer.json");
        if (!File.Exists(tokenizerPath)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id} has no tokenizer.json in {spec.Folder}.");
        var encoder = new QwenEditPromptEncoder(spec, GraphSessions.Open(spec), QwenTokenizer.FromJson(File.ReadAllText(tokenizerPath)));
        try
        {
            encoder.Encode("warm up", []);   // CUDA tunes its kernels here, not on somebody's first picture
            return encoder;
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }
}
