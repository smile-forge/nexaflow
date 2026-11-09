using System.IO;
using Nexaflow.AiHost.Client;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The CUDA pack's download at launch: a machine that had the pack gets this version's in place of it, and the earlier one
/// goes — at once where the pack does not suit the machine, only once this one is whole where it does.
/// </summary>
[TestClass]
[CoversNode("ai-local-models")]
public class CudaPackDownloadTests
{
    private static readonly ComputeHardwareReport Gpu =
        new([new ComputeAdapter("NVIDIA GeForce RTX 3080", ComputeVendor.Nvidia, 10L << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);

    private static readonly ComputeHardwareReport NoGpu = new([], null);

    private string _root = null!;

    [TestInitialize]
    public void Init() => _root = Path.Combine(TestTemp.Root, "nexaflow-test-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Done()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>An earlier version's pack, under the folder of an id this version's is not.</summary>
    private string EarlierPack()
    {
        var dir = Path.Combine(_root, "runtime", "cuda-ort0.1.0");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "cublas64_13.dll"), [1, 2, 3]);
        return dir;
    }

    /// <summary>The pack's download over a store that fetches nothing: what can come, comes from disk.</summary>
    private CudaPackDownload Pack(ComputeHardwareReport machine, IBackgroundActivityManager? activities = null)
        => new(new ModelAssetStore(_root, CudaRuntimePack.EarlierIn(_root), downloadsAllowed: false), _root, machine, activities);

    [TestMethod]
    public async Task OnAMachineThePackDoesNotSuit_AnEarlierPack_IsDeletedAtLaunch_AndNothingIsFetched()
    {
        var earlier    = EarlierPack();
        var activities = Substitute.For<IBackgroundActivityManager>();
        var pack       = Pack(NoGpu, activities);

        await pack.ReplaceEarlierAsync();

        Assert.IsFalse(Directory.Exists(earlier), "nothing on this machine will read it");
        activities.DidNotReceive().StartActivity(Arg.Any<string>());
        Assert.IsNull(pack.Failure);
    }

    [TestMethod]
    public async Task OnAMachineItSuits_AnEarlierPack_StartsThisVersionsDownload_AndStaysUntilItHasCome()
    {
        var earlier    = EarlierPack();
        var activities = Substitute.For<IBackgroundActivityManager>();
        var pack       = Pack(Gpu, activities);

        await pack.ReplaceEarlierAsync();

        activities.Received(1).StartActivity(Arg.Any<string>());
        Assert.IsNotNull(pack.Failure, "downloads are off in this run, so the pack did not come, and says why");
        Assert.IsFalse(pack.IsDownloading);
        Assert.IsTrue(Directory.Exists(earlier), "the earlier pack goes only once this one is whole");
    }

    [TestMethod]
    public async Task WithNoEarlierPack_NothingStartsAtLaunch()
    {
        var activities = Substitute.For<IBackgroundActivityManager>();

        await Pack(Gpu, activities).ReplaceEarlierAsync();

        activities.DidNotReceive().StartActivity(Arg.Any<string>());
    }
}
