using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.SceneDepth;

/// <summary>
/// Depth Anything V2 Small (Apache-2.0): a picture as <see cref="SceneDepthHandover"/> prepares it in, both sides multiples of
/// 14; its relative disparity at the same size out — the makers' forward, ReLU and all, larger nearer.
/// </summary>
internal sealed class SceneDepthNetwork(ModelSpec spec, InferenceSession session) : IHostModel
{
    public ModelSpec Spec => spec;

    public float[] Estimate(SceneDepthInput input)
    {
        if (input.Width % SceneDepthHandover.Patch != 0 || input.Height % SceneDepthHandover.Patch != 0)
            throw new HostRequestException(HostErrorKind.Invalid, $"The estimator takes multiples of {SceneDepthHandover.Patch} each way, not {input.Width}x{input.Height}.");
        if (input.Planes.Length != 3 * input.Width * input.Height)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {input.Width}x{input.Height} picture is {3 * input.Width * input.Height} values of planar RGB.");
        using var pixels = OrtValue.CreateTensorValueFromMemory(input.Planes, [1, 3, input.Height, input.Width]);
        // Each picture's size is its own: the session gives its scratch back after every one.
        using var run = GraphSessions.Releasing(Spec);
        using var outputs = session.Run(run, ["pixels"], [pixels], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class SceneDepthNetworkRunner : IModelRunner
{
    public string Id => "scene-depth-estimator";

    public IHostModel Load(ModelSpec spec)
    {
        var session = GraphSessions.Open(spec);
        var network = new SceneDepthNetwork(spec, session);
        try
        {
            // The makers' square picture tunes the kernels at load, not on somebody's first photo.
            int side = SceneDepthHandover.Side;
            network.Estimate(new SceneDepthInput(new float[3 * side * side], side, side));
            return network;
        }
        catch
        {
            network.Dispose();
            throw;
        }
    }
}
