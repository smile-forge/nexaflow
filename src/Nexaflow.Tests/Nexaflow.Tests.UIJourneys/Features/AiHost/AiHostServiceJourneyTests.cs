using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.UIJourneys.Features.AiHost;

/// <summary>
/// The AI host service as a machine gets it, end to end and from the outside: the built MSI installed as an administrator installs
/// it, the service starts with the machine under its own account and keeps the model store, every user reading. An app with no host
/// of its own — the SDK's sample, given no host to start — joins it, has it fetch a model and answers with it; a worker killed
/// under it is replaced, and the app joins the next; with the service stopped, an app has no host — none of its own in the service's
/// place — and says why; and uninstalling gives the store back to every user, whose app's own host then runs what the store holds,
/// fetching nothing. It installs a Windows service, so it runs only in Windows
/// Sandbox (<c>Nexaflow.Tests.Sandbox</c>); build the solution and <c>aihostSetup</c> first. The sample's model is fetched from the
/// network the first time: a few GB.
/// </summary>
[TestClass]
[TestCategory("Machine")]
public class AiHostServiceJourneyTests
{
    private const string Service = "SmileForgeAiHost";
    private static readonly TimeSpan Answering = TimeSpan.FromMinutes(30);

    public TestContext TestContext { get; set; } = null!;

    /// <summary>The configuration this suite was built in, which the MSI and the sample are taken from too.</summary>
    private static string Configuration
        => AppContext.BaseDirectory.Contains(@"\Release\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

    private static string Msi => Path.Combine(TestSampleData.RepoRoot, "aihostSetup", "bin", "x64", Configuration, "en-US", "aihostSetup.msi");

    private static string Sample
        => Path.Combine(TestSampleData.RepoRoot, "src", "Nexaflow.AiHost", "Nexaflow.AiHost.Sample", "bin", "x64", Configuration, "net10.0-windows",
                        "Nexaflow.AiHost.Sample.exe");

    /// <summary>The host the app starts for itself once no service is installed: the build's own.</summary>
    private static string BuiltHost
        => Path.Combine(TestSampleData.RepoRoot, "src", "Nexaflow.AiHost", "Nexaflow.AiHost", "bin", "x64", Configuration, "net10.0-windows", "win-x64",
                        "Nexaflow.AiHost.exe");

    private static string Store
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Smile-Forge", "Models");

    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    [TestMethod]
    public async Task TheAiHostService_ServesAnApp_OutlivesItsWorker_IsTheOnlyHostWhileInstalled_AndGivesTheStoreBack()
    {
        if (!string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase))
            Assert.Inconclusive("It installs a Windows service, so it runs only in Windows Sandbox (Nexaflow.Tests.Sandbox).");
        string msi = Path.GetFullPath(Msi);
        if (!File.Exists(msi)) Assert.Inconclusive($"{msi} is not built: dotnet build aihostSetup/aihostSetup.wixproj -c {Configuration} -p:Platform=x64");
        if (!File.Exists(Sample)) Assert.Inconclusive($"{Sample} is not built: build the solution.");

        string logs = Path.Combine(TestSampleData.RepoRoot, "TestResults", "aihost-service", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(logs);
        try
        {
            // Installed, it runs with the machine as its own account, and the store is its, every user reading.
            Assert.AreEqual(0, (await RunAsync("msiexec", ["/i", msi, "/qn", "/l*v", Path.Combine(logs, "install.log")], TimeSpan.FromMinutes(10))).Exit);
            string config = (await RunAsync("sc", ["qc", Service], TimeSpan.FromSeconds(30))).Output;
            StringAssert.Contains(config, "AUTO_START", "started with the machine, whoever is signed in");
            StringAssert.Contains(config, $@"NT SERVICE\{Service}", "as its own account");
            await UntilAsync(async () => (await RunAsync("sc", ["query", Service], TimeSpan.FromSeconds(30))).Output.Contains("RUNNING"));
            Assert.AreEqual(FileSystemRights.ReadAndExecute, UsersRights() & ~FileSystemRights.Synchronize, "every user reads the store, none writes it");

            // An app given no host of its own joins the service, which fetches the model into its store, and answers with it.
            var answer = await RunAsync(Sample, [], Answering, Path.Combine(logs, "sample-joined.log"));
            Assert.AreEqual(0, answer.Exit, answer.Errors);
            Assert.IsFalse(string.IsNullOrWhiteSpace(answer.Output), "the sample wrote the model's answer");
            Assert.IsTrue(Directory.EnumerateFiles(Store, "*", SearchOption.AllDirectories).Any(f => !f.EndsWith(".verified", StringComparison.Ordinal)),
                          "the model was fetched into the service's store");

            // The worker dies; the supervisor starts another, and the app joins it.
            int supervisor = await ServiceProcessAsync();
            var worker = Process.GetProcessesByName("Nexaflow.AiHost").Single(p => p.Id != supervisor);
            Process.EnterDebugMode();   // the worker runs as the service's account
            worker.Kill();
            await UntilAsync(() => Task.FromResult(Process.GetProcessesByName("Nexaflow.AiHost").Any(p => p.Id != supervisor && p.Id != worker.Id)));
            var again = await RunAsync(Sample, [], Answering, Path.Combine(logs, "sample-after-the-worker.log"));
            Assert.AreEqual(0, again.Exit, again.Errors);

            // The service stopped, an app has no host: none of its own in the service's place, even given one to start.
            Assert.AreEqual(0, (await RunAsync("sc", ["stop", Service], TimeSpan.FromSeconds(60))).Exit);
            await UntilAsync(async () => (await RunAsync("sc", ["query", Service], TimeSpan.FromSeconds(30))).Output.Contains("STOPPED"));
            var stopped = await RunAsync(Sample, ["--host", BuiltHost], Answering, Path.Combine(logs, "sample-service-stopped.log"));
            Assert.AreNotEqual(0, stopped.Exit, "with the service installed, it is the only host");
            StringAssert.Contains(stopped.Errors, "not running", "and the app is told why");

            // Uninstalled, the service is gone and the store is every user's again: an app's own host runs the model the service fetched.
            Assert.AreEqual(0, (await RunAsync("msiexec", ["/x", msi, "/qn", "/l*v", Path.Combine(logs, "uninstall.log")], TimeSpan.FromMinutes(10))).Exit);
            Assert.AreNotEqual(0, (await RunAsync("sc", ["query", Service], TimeSpan.FromSeconds(30))).Exit, "the service is gone");
            Assert.AreEqual(FileSystemRights.FullControl, UsersRights() & FileSystemRights.FullControl, "every user has full control of the store again");
            var own = await RunAsync(Sample, ["--host", BuiltHost], Answering, Path.Combine(logs, "sample-own-host.log"));
            Assert.AreEqual(0, own.Exit, own.Errors);
            StringAssert.DoesNotMatch(own.Errors, new System.Text.RegularExpressions.Regex("fetching"), "it fetched nothing: the store held the model");
        }
        finally
        {
            if ((await RunAsync("sc", ["query", Service], TimeSpan.FromSeconds(30))).Exit == 0)
                await RunAsync("msiexec", ["/x", msi, "/qn"], TimeSpan.FromMinutes(10));
        }
    }

    /// <summary>What every user may do in the store, as its folder's rules say.</summary>
    private static FileSystemRights UsersRights()
        => new DirectoryInfo(Store).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
               .Where(r => r.IdentityReference == Users && r.AccessControlType == AccessControlType.Allow)
               .Aggregate((FileSystemRights)0, (all, r) => all | r.FileSystemRights);

    /// <summary>The service's own process — its supervisor — as the service manager names it.</summary>
    private async Task<int> ServiceProcessAsync()
    {
        string output = (await RunAsync("sc", ["queryex", Service], TimeSpan.FromSeconds(30))).Output;
        string line = output.Split('\n').Single(l => l.TrimStart().StartsWith("PID", StringComparison.Ordinal));
        return int.Parse(line[(line.IndexOf(':') + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var until = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        while (!await condition())
        {
            if (DateTime.UtcNow > until) Assert.Fail("It never came to pass.");
            await Task.Delay(500);
        }
    }

    private sealed record Ran(int Exit, string Output, string Errors);

    /// <summary>Runs <paramref name="file"/>, its output kept — and written to <paramref name="log"/> where one is named.</summary>
    private async Task<Ran> RunAsync(string file, IReadOnlyList<string> arguments, TimeSpan limit, string? log = null)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var cancel = new CancellationTokenSource(limit);
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new AssertFailedException($"{Path.GetFileName(file)} {string.Join(' ', arguments)} ran past {limit.TotalMinutes:0} minutes.");
        }
        var ran = new Ran(process.ExitCode, await output, await errors);
        if (log is not null) await File.WriteAllTextAsync(log, $"exit {ran.Exit}\n--- output\n{ran.Output}\n--- errors\n{ran.Errors}");
        TestContext.WriteLine($"{Path.GetFileName(file)} {string.Join(' ', arguments)} → {ran.Exit}");
        return ran;
    }
}
