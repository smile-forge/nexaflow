using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Nexaflow.AiHost.Catalog;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The AI host and its SDK on their own: what they reference of Nexaflow — nothing but themselves — and an app that is not
/// Nexaflow (<c>Nexaflow.AiHost.Sample</c>, copied into <c>AiHostSample\</c> beside these tests) running a private host
/// through the SDK alone, on the test store (<see cref="TestStore"/>): Gemma 4 E4B, on the CUDA pack where the machine suits it.
/// The app fetches what its host's store lacks, so it runs only where the test store holds all it would ask for.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class StandaloneSdkTests
{
    /// <summary>The SDK's assemblies, the host executable, and the app that uses them, by their files beside these tests.</summary>
    private static IEnumerable<string> Standalone()
    {
        string bin = AppContext.BaseDirectory;
        yield return Path.Combine(bin, "Nexaflow.AiHost.Contracts.dll");
        yield return Path.Combine(bin, "Nexaflow.AiHost.Provisioning.dll");
        yield return Path.Combine(bin, "Nexaflow.AiHost.Client.dll");
        yield return Path.Combine(bin, "AiHost", "Nexaflow.AiHost.dll");
        yield return Path.Combine(bin, "AiHostSample", "Nexaflow.AiHost.Sample.dll");
    }

    private static IEnumerable<string> ReferencesOf(string assembly)
    {
        using var file = File.OpenRead(assembly);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.AssemblyReferences)
            yield return metadata.GetString(metadata.GetAssemblyReference(handle).Name);
    }

    [TestMethod]
    public void TheHostItsSdkAndAnAppUsingThem_ReferenceNothingOfNexaflowButThemselves()
    {
        var offenders = Standalone()
            .SelectMany(file => ReferencesOf(file).Select(reference => (File: Path.GetFileName(file), Reference: reference)))
            .Where(r => r.Reference.StartsWith("Nexaflow.", StringComparison.Ordinal)
                        && !r.Reference.StartsWith("Nexaflow.AiHost.", StringComparison.Ordinal) && r.Reference != "Nexaflow.AiHost")
            .Select(r => $"{r.File} → {r.Reference}")
            .ToList();

        Assert.AreEqual(0, offenders.Count, $"The host and its SDK stand on their own. Offenders: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    [TestCategory("OwnedModel")]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AnAppThatIsNotNexaflow_RunsAPrivateHost_ThroughTheSdkAlone()
    {
        // What the app asks its host for: the model's files for the runtime this machine runs it on, and the pack for the GPU.
        const string model = "gemma-4-e4b-it";
        var entry = HostCatalog.Models.Single(m => m.Id == model);
        bool onGpu = CudaRuntimePack.Applies(MachineCards.Probe()) && entry.RunsOn(HostCatalog.Cuda);
        if (!TestStore.HoldsAll([.. entry.FilesFor(onGpu ? HostCatalog.Cuda : HostCatalog.Cpu), .. onGpu ? CudaRuntimePack.Files : []]))
            Assert.Inconclusive($"{TestStore.Missing(gpu: false)} Or it does not hold all of {entry.DisplayName}{(onGpu ? " and the CUDA pack" : "")}.");

        string bin = AppContext.BaseDirectory;
        var start = new ProcessStartInfo(Path.Combine(bin, "AiHostSample", "Nexaflow.AiHost.Sample.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        foreach (var argument in new[] { "--host", Path.Combine(bin, "AiHost", "Nexaflow.AiHost.exe"), "--model", model, "--store", TestStore.Root! })
            start.ArgumentList.Add(argument);

        using var app = Process.Start(start)!;
        var output = app.StandardOutput.ReadToEndAsync();
        var errors = app.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try { await app.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            app.Kill(entireProcessTree: true);
            Assert.Fail("The app did not finish within eight minutes.");
        }

        string said = await output, where = await errors;
        Assert.AreEqual(0, app.ExitCode, $"{where}\n{said}");
        Assert.IsTrue(said.Trim().Length > 0, "the model replied");
        Console.WriteLine($"{where.Trim()}: {said.Trim()}");
    }
}
