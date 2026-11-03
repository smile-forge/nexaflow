using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.SceneDepth;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.SceneDepth;

/// <summary>
/// Scene depth through the real AI host, held to Depth Anything V2's own infer_image: each of the makers' sample pictures
/// against what their pipeline made of it in fp32 (tools/ai-models/depth-anything-v2-small makes them) — what the network was
/// given, what it gave at its working size, and that brought to the picture's size by the corner-aligned resize.
/// <para>
/// <c>NEXAFLOW_TEST_SCENE_DEPTH</c> names the graph's folder, <c>NEXAFLOW_TEST_SCENE_DEPTH_REFERENCE</c> the reference set and
/// <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the CUDA pack; without them every test is inconclusive.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("scene-depth")]
public class SceneDepthTests
{
    /// <summary>How far, as a share of the reference's range, any value may lie from it: the graph runs the makers' sums in
    /// another order, and on a GPU in other kernels.</summary>
    private const double Agreement = 0.01;

    private static AiHostClient? _client;
    private static string? _graph, _reference;
    private static string _work = "";

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SCENE_DEPTH") is not { Length: > 0 } graph) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SCENE_DEPTH_REFERENCE") is not { Length: > 0 } reference) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack) return;

        var aiDir = TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };
        (_graph, _reference) = (graph, reference);
        _work = TestTemp.CreateSubdirectory("nexaflow-scene-depth-").FullName;
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
        try { if (_work.Length > 0) Directory.Delete(_work, recursive: true); } catch (IOException) { }
    }

    private static IReadOnlyDictionary<string, ModelSpec> Parts(int? device)
    {
        if (_client is null || _graph is null)
            Assert.Inconclusive("NEXAFLOW_TEST_SCENE_DEPTH, NEXAFLOW_TEST_SCENE_DEPTH_REFERENCE and NEXAFLOW_TEST_RUNTIME_PACK are not all set.");
        return new Dictionary<string, ModelSpec>
        {
            [SceneDepthParts.Estimator] = new("depth-anything-v2-small/estimator", "scene-depth-estimator", Path.Combine(_graph!, "estimator"),
                                              "depth-anything-v2-small.onnx", null, 2L << 30, 0, Device: device),
        };
    }

    private sealed record Case(string Name, int Width, int Height, int WorkingWidth, int WorkingHeight);

    private static IEnumerable<Case> Cases()
    {
        _ = Parts(0);   // inconclusive without the graph
        return JsonNode.Parse(File.ReadAllText(Path.Combine(_reference!, "reference.json")))!.AsObject()
            .Select(c => new Case(c.Key, c.Value!["width"]!.GetValue<int>(), c.Value!["height"]!.GetValue<int>(),
                                  c.Value!["workingWidth"]!.GetValue<int>(), c.Value!["workingHeight"]!.GetValue<int>()));
    }

    private static float[] Floats(string file) => MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(file)).ToArray();

    private async Task<(SceneDepthEstimated Done, float[] Disparity)> EstimateAsync(Case c, int? device)
    {
        var into = Path.Combine(_work, $"{c.Name}-{device?.ToString() ?? "cpu"}.f32");
        var replies = new List<HostReply>();
        await foreach (var reply in _client!.SendAsync(id => new EstimateSceneDepth(id, Parts(device), Path.Combine(_reference!, $"{c.Name}.rgba"),
                                                                                    c.Width, c.Height, into, RequestPriority.Job),
                                                       TimeSpan.FromMinutes(5)))
            replies.Add(reply);
        return (replies.OfType<SceneDepthEstimated>().Single(), Floats(into));
    }

    /// <summary>The largest difference between two sets of values, as a share of the reference's range.</summary>
    private static double Worst(float[] expected, float[] actual)
    {
        Assert.AreEqual(expected.Length, actual.Length, "not the reference's size");
        double range = expected.Max() - expected.Min();
        return expected.Zip(actual, (e, a) => Math.Abs(e - a)).Max() / range;
    }

    [TestMethod]
    public void EachPicture_IsHandedToTheNetwork_AsInferImageHandsIt()
    {
        foreach (var c in Cases())
        {
            var input = SceneDepthHandover.Prepare(File.ReadAllBytes(Path.Combine(_reference!, $"{c.Name}.rgba")), c.Width, c.Height);
            Assert.AreEqual((c.WorkingWidth, c.WorkingHeight), (input.Width, input.Height), $"{c.Name}: the working size is the makers'");
            var expected = Floats(Path.Combine(_reference!, $"{c.Name}.input"));
            float worst = expected.Zip(input.Planes, (e, a) => Math.Abs(e - a)).Max();
            TestContext.WriteLine($"{c.Name}: input {worst:E2} from infer_image's");
            Assert.IsTrue(worst < 1e-5f, $"{c.Name}: a value of the input is {worst} from infer_image's");
        }
    }

    [TestMethod]
    public async Task EachPicture_HasTheDepthInferImageGives_OnTheGpu()
    {
        foreach (var c in Cases())
        {
            var (done, disparity) = await EstimateAsync(c, device: 0);
            Assert.AreEqual((c.WorkingWidth, c.WorkingHeight), (done.Width, done.Height), $"{c.Name}: the disparity is at the working size");
            double atWork = Worst(Floats(Path.Combine(_reference!, $"{c.Name}.disparity")), disparity);
            var full = PictureResampling.ResizePlaneBilinearAligned(disparity, done.Width, done.Height, c.Width, c.Height);
            double atSize = Worst(Floats(Path.Combine(_reference!, $"{c.Name}.depth")), full);
            TestContext.WriteLine($"{c.Name} on the GPU: {atWork:P3} of its range from the network's at its working size, {atSize:P3} at the picture's");
            Assert.IsTrue(atWork < Agreement && atSize < Agreement, $"{c.Name}: {atWork:P3} / {atSize:P3} of its range from infer_image's");
        }
    }

    [TestMethod]
    public async Task EachPicture_HasTheSameDepth_OnTheCpu()
    {
        var c = Cases().First();
        var (done, disparity) = await EstimateAsync(c, device: null);
        double atWork = Worst(Floats(Path.Combine(_reference!, $"{c.Name}.disparity")), disparity);
        TestContext.WriteLine($"{c.Name} on the CPU: {atWork:P3} of its range from the network's");
        Assert.IsTrue(atWork < Agreement, $"{c.Name}: {atWork:P3} of its range from infer_image's on the CPU");
    }
}
