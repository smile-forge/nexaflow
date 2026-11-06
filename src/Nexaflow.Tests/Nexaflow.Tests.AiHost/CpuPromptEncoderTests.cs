using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// A prompt encoder's CPU build — its graph with its 8-bit products reading 8-bit inputs, <c>*.cpu.onnx</c> beside it — held to
/// the graph as built, both run on the CPU, where a machine has the model (<c>NEXAFLOW_TEST_IMAGE</c>). A build is offered only
/// where every row it reads a prompt to points where the graph's does: Z-Image's does; Klein's (worst row 0.749) and
/// Qwen-Image-Edit's (0.9975) do not, and run on the CPU as built.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images-cpu-encoders")]
public class CpuPromptEncoderTests
{
    private const string Prompt = "A weathered lighthouse on a rocky cliff at dusk, waves crashing below, warm light spilling from its windows, "
                                + "seagulls circling, a small red boat moored in the cove, painted in soft watercolour with fine ink lines";

    /// <summary>The least cosine between a row the CPU build gives and the graph's: what still points the denoiser the same way.</summary>
    private const double LeastCosine = 0.999;

    private static string Folder(string variable, string part)
    {
        string root = Environment.GetEnvironmentVariable(variable) ?? "";
        string folder = Path.Combine(root, part);
        if (root.Length == 0 || !Directory.EnumerateFiles(folder, "*.cpu.onnx").Any()) Assert.Inconclusive($"{variable} names no folder holding a CPU build of its {part}.");
        return folder;
    }

    private static double WorstCosine(float[] a, float[] b, int width)
    {
        double worst = 1;
        for (int row = 0; row < a.Length / width; row++)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = row * width; i < (row + 1) * width; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            if (na > 0 && nb > 0) worst = Math.Min(worst, dot / Math.Sqrt(na * nb));
        }
        return worst;
    }

    private static ModelSpec OnCpu(string id, string runner, string folder, string file) => new(id, runner, folder, file, null, 0, 0);

    [TestMethod]
    public void ZImagesEncoder_OnTheCpu_ReadsAPromptAsItsGraphDoes()
    {
        string folder = Folder("NEXAFLOW_TEST_IMAGE", "encoder");
        (float[] Caption, int Rows, int Width) Encode(string file)
        {
            using var encoder = (ZImagePromptEncoder)new ZImagePromptEncoderRunner().Load(OnCpu("z-image-turbo/promptEncoder", "z-image-prompt-encoder", folder, file));
            return encoder.Encode(encoder.Tokenize(Prompt));
        }
        var graph = Encode("encoder.onnx");
        var cpu = Encode("encoder.cpu.onnx");
        double cosine = WorstCosine(graph.Caption, cpu.Caption, graph.Width);
        Assert.IsTrue(cosine >= LeastCosine, $"its worst row's cosine is {cosine:0.000000}");
    }
}
