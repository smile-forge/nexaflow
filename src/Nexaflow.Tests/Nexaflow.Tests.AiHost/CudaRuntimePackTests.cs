using System.Security.Cryptography;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The CUDA runtime pack: pinned to the host's own runtime, offered where it can run, and run on.</summary>
[TestClass]
[CoversNode("ai-host")]
public class CudaRuntimePackTests
{
    private static string HostFolder => Path.Combine(AppContext.BaseDirectory, "AiHost");

    [TestMethod]
    [DataRow("onnxruntime.dll")]
    [DataRow("onnxruntime_providers_shared.dll")]
    [DataRow("onnxruntime-genai.dll")]
    public void ThePackCarriesTheHostsOwnRuntime(string library)
    {
        var packed    = CudaRuntimePack.Files.Single(f => Path.GetFileName(f.RelativePath) == library);
        var installed = Path.Combine(HostFolder, library);

        Assert.AreEqual(packed.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(installed))),
            $"the host's {library} and the pack's are different builds — the pack must be re-pinned with the host's runtime");
    }

    [TestMethod]
    public void EveryFile_ComesFromAnArchiveWherePublished_IntoThePacksOwnFolder()
    {
        Assert.IsTrue(CudaRuntimePack.Files.Count > 0);
        foreach (var file in CudaRuntimePack.Files)
        {
            Assert.IsNotNull(file.Archive, file.RelativePath);
            Assert.AreEqual($"runtime/{CudaRuntimePack.Id}", Path.GetDirectoryName(file.RelativePath)!.Replace('\\', '/'));
        }
    }

    private static ComputeAdapter Gpu(ComputeVendor vendor, bool integrated = false) => new("gpu", vendor, 24L << 30, integrated, IsNpu: false);

    [TestMethod]
    public void ThePackApplies_ToAnNvidiaGpuOfItsOwn_WithACuda13Driver()
    {
        Assert.IsTrue(CudaRuntimePack.Applies(new([Gpu(ComputeVendor.Nvidia)], new Version(13, 0))));
        Assert.IsFalse(CudaRuntimePack.Applies(new([Gpu(ComputeVendor.Nvidia)], new Version(12, 8))), "an older driver cannot run CUDA 13");
        Assert.IsFalse(CudaRuntimePack.Applies(new([Gpu(ComputeVendor.Nvidia)], null)), "no driver, no CUDA");
        Assert.IsFalse(CudaRuntimePack.Applies(new([Gpu(ComputeVendor.Amd)], new Version(13, 0))));
        Assert.IsFalse(CudaRuntimePack.Applies(new([Gpu(ComputeVendor.Nvidia, integrated: true)], new Version(13, 0))));
    }

    /// <summary>The real pack, where a machine has one: <c>NEXAFLOW_TEST_RUNTIME_PACK</c> names its folder.</summary>
    [TestMethod]
    public async Task TheHostLoadsItsRuntimeFromThePack()
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack)
        {
            Assert.Inconclusive("NEXAFLOW_TEST_RUNTIME_PACK names no CUDA pack on this machine.");
            return;
        }
        var aiDir = Directory.CreateTempSubdirectory("nexaflow-aihost-").FullName;
        await using var client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(HostFolder, "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };

        HostDescription? description = null;
        await foreach (var reply in client.SendAsync(id => new DescribeHost(id), TimeSpan.FromSeconds(30)))
            description = reply as HostDescription;

        Assert.AreEqual(pack, description?.RuntimePack);
        CollectionAssert.Contains(description!.ExecutionProviders.ToArray(), "CUDAExecutionProvider");
    }
}
