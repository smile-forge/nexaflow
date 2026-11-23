using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The AI host and its SDK on their own: what they reference of Nexaflow — nothing but themselves — and an app that is not
/// Nexaflow (<c>Nexaflow.AiHost.Sample</c>, copied into <c>AiHostSample\</c> beside these tests) running a private host
/// through the SDK alone. <c>NEXAFLOW_TEST_SAMPLE_GGUF</c> names the GGUF it runs; <c>NEXAFLOW_TEST_RUNTIME_PACK</c>, optional,
/// the CUDA pack it runs on where the machine suits it.
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
        string gguf = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SAMPLE_GGUF") ?? "";
        if (!File.Exists(gguf)) Assert.Inconclusive("NEXAFLOW_TEST_SAMPLE_GGUF names no GGUF on this machine.");

        string bin = AppContext.BaseDirectory;
        var start = new ProcessStartInfo(Path.Combine(bin, "AiHostSample", "Nexaflow.AiHost.Sample.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        foreach (var argument in new[] { "--host", Path.Combine(bin, "AiHost", "Nexaflow.AiHost.exe"), "--gguf", gguf })
            start.ArgumentList.Add(argument);
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is { Length: > 0 } pack)
        {
            start.ArgumentList.Add("--pack");
            start.ArgumentList.Add(pack);
        }

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
