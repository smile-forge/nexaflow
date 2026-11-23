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
/// The graph is <c>depth-anything-v2-small</c>'s from the test store (<see cref="TestStore"/>), run by a host on the CUDA pack there,
/// and on the CPU by the host's own handler in the test's process with no GPU to place it on (<see cref="InProcessHost"/>);
/// <c>NEXAFLOW_TEST_SCENE_DEPTH_REFERENCE</c> names the reference set. Without both every test is inconclusive.
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

    /// <summary>The scene depth tool, as the catalog names it.</summary>
    private const string Tool = "depth-anything-v2-small";

    /// <summary>Its estimator, as a hold names it.</summary>
    private static readonly ModelRef Estimator = new(Tool, SceneDepthParts.Estimator);

    private static AiHostClient? _client;
    private static string? _reference;
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SCENE_DEPTH_REFERENCE") is not { Length: > 0 } reference) return;
        _client = await TestStore.HostAsync();
        _reference = reference;
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private sealed record Case(string Name, int Width, int Height, int WorkingWidth, int WorkingHeight);

    private static IEnumerable<Case> Cases()
    {
        if (_client is null || _reference is null)
            Assert.Inconclusive($"{TestStore.Missing()} Or NEXAFLOW_TEST_SCENE_DEPTH_REFERENCE is not set.");
        return JsonNode.Parse(File.ReadAllText(Path.Combine(_reference!, "reference.json")))!.AsObject()
            .Select(c => new Case(c.Key, c.Value!["width"]!.GetValue<int>(), c.Value!["height"]!.GetValue<int>(),
                                  c.Value!["workingWidth"]!.GetValue<int>(), c.Value!["workingHeight"]!.GetValue<int>()));
    }

    private static float[] Floats(string file) => MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(file)).ToArray();

    private async Task<(SceneDepthEstimated Done, float[] Disparity)> EstimateAsync(Case c)
    {
        using var pixels = await _client!.ShareFileAsync(Path.Combine(_reference!, $"{c.Name}.rgba"));
        var replies = new List<HostReply>();
        await foreach (var reply in _client.SendAsync(id => new EstimateSceneDepth(id, Tool, pixels, c.Width, c.Height, RequestPriority.Job),
                                                      TimeSpan.FromMinutes(5)))
            replies.Add(reply);
        return Disparity(replies);
    }

    private static (SceneDepthEstimated Done, float[] Disparity) Disparity(List<HostReply> replies)
    {
        var done = replies.OfType<SceneDepthEstimated>().Single();
        using (done.Disparity) return (done, MemoryMarshal.Cast<byte, float>(done.Disparity.Span).ToArray());
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
        var cases = Cases();
        // Held, the estimator runs where the host placed it for the hold: a GPU, where one has room for it.
        await using var hold = await TestStore.HoldPlacedAsync(_client!, [Estimator], RequestPriority.Job);
        Assert.IsNotNull(hold.PlacementOf(Estimator)!.Device, "the host placed the estimator on a GPU");
        foreach (var c in cases)
        {
            var (done, disparity) = await EstimateAsync(c);
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
        using var host = InProcessHost.OnTheCpu();
        var request = new EstimateSceneDepth(1, Tool, SharedBytes.Of(File.ReadAllBytes(Path.Combine(_reference!, $"{c.Name}.rgba"))), c.Width, c.Height,
                                             RequestPriority.Job);
        var (done, disparity) = Disparity(await InProcessHost.RunAsync(new SceneDepthHandler(host.Held, host.Scheduler), request));
        double atWork = Worst(Floats(Path.Combine(_reference!, $"{c.Name}.disparity")), disparity);
        TestContext.WriteLine($"{c.Name} on the CPU: {atWork:P3} of its range from the network's");
        Assert.IsTrue(atWork < Agreement, $"{c.Name}: {atWork:P3} of its range from infer_image's on the CPU");
    }
}
