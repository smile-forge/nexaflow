using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// The AI host service as a machine gets it, end to end and from the outside. The built MSI, installed as an administrator, moves this
/// user's models from an earlier Nexaflow into the service's store and installs the service, which starts with the machine under its
/// own account and keeps the store, every user reading. Nexaflow, started with no host of its own, answers from the service on the
/// model moved in; Settings show the share of memory the service's models may hold as the service's, not the user's to change there,
/// and About that its models run on the CUDA pack the service took up, as the service itself found it. Another app — the SDK's sample,
/// given no host of its own — answers from the same service. With no app using it, the service runs no worker; a worker killed under
/// the apps is replaced by one started for the next app to come. With the service stopped, the AI's line says Nexaflow's own models
/// cannot run, and why, rather
/// than each request failing, and the other app has no host — none of its own in the service's place; once the service is back, the
/// line clears and Nexaflow answers again. Uninstalling gives the store back to every user, and an app's own host then runs what the
/// store holds, fetching nothing.
/// <para>
/// It installs a Windows service, so it runs only in Windows Sandbox (<c>Nexaflow.Tests.Sandbox</c>); build the solution and
/// <c>aihostSetup</c> first. <c>NEXAFLOW_TEST_STORE</c> names the test model store, holding Gemma 4 26B A4B and the CUDA pack, copied
/// into the sandbox user's earlier model folder for the installer to move in; without it the journey is inconclusive. What each tool
/// it runs said is written to <c>TestResults\aihost-service\</c>.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[TestCategory("Machine")]
[CoversNode("ai-host-service")]
public class AiHostServiceJourneyTests : OptionsOverlayJourney
{
    private const string StoreVariable = "NEXAFLOW_TEST_STORE";
    private const string Service = "SmileForgeAiHost";
    private const string Model = "gemma-4-26b-a4b-it";
    private const string Question = "Name the three primary colours of light, in one line.";

    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    private string _msi = "";
    private string _logs = "";
    private string _earlier = "";
    private bool _storeHadModel;

    /// <summary>The service's worker loads the model on the first request, and the other app loads it again after the worker is
    /// replaced and once the service is gone.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(25);

    /// <summary>Started as a journey's app is, yet joining the machine's service as the app does outside a test.</summary>
    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_TEST_JOIN_SERVICE"] = "1" };

    private static string Configuration
        => AppContext.BaseDirectory.Contains(@"\Release\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

    private static string Sample
        => Path.Combine(TestSampleData.RepoRoot, "src", "Nexaflow.AiHost", "Nexaflow.AiHost.Sample", "bin", "x64", Configuration, "net10.0-windows",
                        "Nexaflow.AiHost.Sample.exe");

    /// <summary>The host an app starts for itself once no service is installed: the build's own.</summary>
    private static string BuiltHost
        => Path.Combine(TestSampleData.RepoRoot, "src", "Nexaflow.AiHost", "Nexaflow.AiHost", "bin", "x64", Configuration, "net10.0-windows", "win-x64",
                        "Nexaflow.AiHost.exe");

    private static string Store
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Smile-Forge", "Models");

    /// <summary>The model and the CUDA pack, from the test store into this user's earlier model folder; the service installed, moving
    /// them in; and a workspace that converses on the model.</summary>
    protected override void SeedConfig(string configDir)
    {
        if (!string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase))
            Assert.Inconclusive("It installs a Windows service, so it runs only in Windows Sandbox (Nexaflow.Tests.Sandbox).");
        var store = Environment.GetEnvironmentVariable(StoreVariable);
        if (string.IsNullOrEmpty(store) || !Directory.Exists(store))
        {
            Assert.Inconclusive($"{StoreVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        OwnedModelFolder.RequireModel(store, Model, StoreVariable);
        _msi = Path.Combine(TestSampleData.RepoRoot, "aihostSetup", "bin", "x64", Configuration, "en-US", "aihostSetup.msi");
        if (!File.Exists(_msi)) Assert.Inconclusive($"{_msi} is not built: dotnet build aihostSetup/aihostSetup.wixproj -c {Configuration} -p:Platform=x64");
        if (!File.Exists(Sample) || !File.Exists(BuiltHost)) Assert.Inconclusive("The SDK's sample and the host are not built: build the solution.");
        _logs = Path.Combine(TestSampleData.RepoRoot, "TestResults", "aihost-service", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(_logs);

        _earlier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "ai");
        foreach (var file in OwnedModelFolder.FilesOf(Model))
            foreach (var each in new[] { file, file + ".verified" })
                Copy(Path.Combine(store, each), Path.Combine(_earlier, each));
        foreach (var pack in Directory.EnumerateDirectories(Path.Combine(store, "runtime"), "cuda-*"))
            foreach (var file in Directory.EnumerateFiles(pack, "*", SearchOption.AllDirectories))
                Copy(file, Path.Combine(_earlier, Path.GetRelativePath(store, file)));

        _storeHadModel = File.Exists(Path.Combine(Store, OwnedModelFolder.FilesOf(Model)[0]));
        Assert.AreEqual(0, Run("msiexec", ["/i", _msi, "/qn", "/l*v", Path.Combine(_logs, "install.log")]).Exit, "the service's MSI installed");
        Assert.IsTrue(Until(() => Run("sc", ["query", Service]).Output.Contains("RUNNING"), TimeSpan.FromMinutes(2)), "the service runs once installed");

        // What the service costs the machine with no app using it — Nexaflow is started after this: its supervisor alone, no worker
        // holding a runtime.
        RecordIdleFootprint();
        Check("with no app using it, the service runs no worker", () => Process.GetProcessesByName("Nexaflow.AiHost").Length == 1);

        var version = FileVersionInfo.GetVersionInfo(BuiltOutput.App).FileVersion ?? "1.0.0.0";
        var abilities = Path.Combine(configDir, "Contexts", "Default", "ai-abilities");
        Directory.CreateDirectory(abilities);
        File.WriteAllText(Path.Combine(abilities, $"config_{version}.json"), $$"""
            {
              "ConfigName": "ai-abilities",
              "FriendlyName": "AI",
              "Columns": [
                { "Id": "chat", "ProviderName": "Nexaflow", "Model": "{{Model}}", "Kind": "Language", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": { "GeneralIntelligence": [ "chat" ], "Disambiguation": [ "chat" ] }
            }
            """);
    }

    [TestCleanup]
    public void Uninstall()
    {
        if (_msi.Length > 0 && File.Exists(_msi) && Run("sc", ["query", Service]).Exit == 0) Run("msiexec", ["/x", _msi, "/qn"]);
    }

    [TestMethod]
    public void TheAiHostService_ServesNexaflowAndAnotherApp_OutlivesItsWorker_SaysWhenItCannotRun_AndGivesTheStoreBack()
    {
        // Installed: with the machine, as its own account, keeping the store that the earlier models were moved into.
        string config = Run("sc", ["qc", Service]).Output;
        Check("the service starts with the machine, whoever is signed in", () => config.Contains("AUTO_START"));
        Check("as its own account", () => config.Contains($@"NT SERVICE\{Service}"));
        Check("every user reads the store, and none writes it", () => (UsersRights() & ~FileSystemRights.Synchronize) == FileSystemRights.ReadAndExecute);
        string model = OwnedModelFolder.FilesOf(Model)[0];
        // Moved where the store lacked it; where an earlier run of the sandbox left it there, the earlier copy is left alone.
        Check("the earlier model is in the service's store, moved in where it lacked it", () =>
            File.Exists(Path.Combine(Store, model)) && (_storeHadModel || !File.Exists(Path.Combine(_earlier, model))));

        // Nexaflow, on the service.
        Check("Nexaflow's request is typed and sent", () => InFront() && AskTheAiBar(Question));
        Check("the service answers it, on the model moved in", () => Answered(Affordable(600)));
        var sections = OpenOptions();
        SelectSection(sections, "Local models", "cfg_MemoryForModelsPercent");
        Check("the share of memory the service's models may hold is the service's, not the user's to change here",
              () => WaitForId("cfg_MemoryForModelsPercent", 10) is { IsEnabled: false });
      SelectSection(sections, "About", "About_ComponentsToggle");
      Expand("About_ComponentsToggle");
      Check("About says the service's models run on the CUDA pack it took up, as the service itself found it", () =>
          Until(() => ComponentStatus("CUDA runtime pack") is { } said && said.StartsWith("Installed", StringComparison.Ordinal) && said.Contains("cuda-"),
                TimeSpan.FromSeconds(60)));
      CancelOptions();

        // Another app, on the same service.
        Check("another app, given no host of its own, answers from the same service, fetching nothing", () =>
            Run(Sample, ["--model", Model], "sample-joined.log") is { Exit: 0 } ran && ran.Output.Trim().Length > 0 && !ran.Errors.Contains("fetching"));

        // The worker dies; the supervisor starts another, and the apps join it.
        int supervisor = int.Parse(Run("sc", ["queryex", Service]).Output.Split('\n').Single(l => l.TrimStart().StartsWith("PID", StringComparison.Ordinal))
                                       .Split(':')[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var worker = Process.GetProcessesByName("Nexaflow.AiHost").Single(p => p.Id != supervisor);
        Process.EnterDebugMode();   // the worker runs as the service's account
        worker.Kill();
        Check("the app that comes next is served by another worker, started for it", () =>
            Run(Sample, ["--model", Model], "sample-after-the-worker.log").Exit == 0
            && Process.GetProcessesByName("Nexaflow.AiHost").Any(p => p.Id != supervisor && p.Id != worker.Id));

        // Stopped: nothing in its place, and said.
        Check("the service stops", () => Run("sc", ["stop", Service]).Exit == 0);
        Check("the AI's dot warns that what is typed for it will not reach it, and why", () =>
            Until(() => DotWarning()?.Contains("not running", StringComparison.OrdinalIgnoreCase) == true, TimeSpan.FromSeconds(30)));
        Check("and the AI's line says so, with the reply it showed put away", () =>
        {
            if (WaitForId("AiBar_ReplyDismiss", 2) is { } dismiss) dismiss.Click();
            return Until(() => WaitForId("AiBar_LocalAiNotice", 1)?.Name?.Contains("not running", StringComparison.OrdinalIgnoreCase) == true, TimeSpan.FromSeconds(10));
        });
        Check("the other app has no host — none of its own in the service's place — and says why", () =>
            Run(Sample, ["--host", BuiltHost, "--model", Model], "sample-service-stopped.log") is { Exit: not 0 } ran && ran.Errors.Contains("not running"));

        // Back: the line clears, and Nexaflow answers again.
        Check("the service starts again", () => Run("sc", ["start", Service]).Exit == 0);
        Check("back, the AI's line clears, and its dot", () => Until(() => WaitForId("AiBar_LocalAiNotice", 1) is null
                                                                      && DotWarning() is not { Length: > 0 },
                                                                TimeSpan.FromSeconds(60)));
        Check("Nexaflow's request is typed and sent again", () => InFront() && AskTheAiBar(Question));
        Check("and the service answers again", () => Answered(Affordable(300)));

        // Uninstalled: the store is every user's again, and an app's own host runs what it holds.
        Check("the service uninstalls", () => Run("msiexec", ["/x", _msi, "/qn", "/l*v", Path.Combine(_logs, "uninstall.log")]).Exit == 0);
        Check("and is gone", () => Run("sc", ["query", Service]).Exit != 0);
        Check("every user has full control of the store again", () => (UsersRights() & FileSystemRights.FullControl) == FileSystemRights.FullControl);
        Check("an app's own host runs the model the store holds, fetching nothing", () =>
            Run(Sample, ["--host", BuiltHost, "--model", Model], "sample-own-host.log") is { Exit: 0 } ran && !ran.Errors.Contains("fetching"));

        AssertJourney();
    }

    /// <summary>Writes what the service holds while no app uses it — each host process's working set and private memory, and the GPU's
    /// compute processes as its driver lists them — to <c>idle-footprint.txt</c> in the journey's results: a reading, not a check.</summary>
    private void RecordIdleFootprint()
    {
        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(15));   // the service settled after starting, a worker it began by mistake there to be seen
        var lines = Process.GetProcessesByName("Nexaflow.AiHost")
                           .Select(p => $"process {p.Id}: working set {p.WorkingSet64 >> 20} MiB, private {p.PrivateMemorySize64 >> 20} MiB")
                           .ToList();
        try
        {
            lines.Add(Run("nvidia-smi", ["--query-compute-apps=pid,used_memory", "--format=csv"]).Output.Trim());
        }
        catch (System.ComponentModel.Win32Exception) { lines.Add("nvidia-smi is not on this machine"); }
        File.WriteAllLines(Path.Combine(_logs, "idle-footprint.txt"), lines);
    }

    /// <summary>A reply naming the three colours, within <paramref name="seconds"/>.</summary>
    private bool Answered(int seconds)
        => Until(() => WaitForId("AiBar_Reply", 1)?.Name is { } reply
                       && new[] { "red", "green", "blue" }.All(colour => reply.Contains(colour, StringComparison.OrdinalIgnoreCase)),
                 TimeSpan.FromSeconds(seconds));

    /// <summary>What the AI's dot warns of — why what is typed for it will not reach it — or null.</summary>
    private string? DotWarning() => WaitForId("AiBar_StatusDot", 1)?.Properties.ItemStatus.ValueOrDefault;

    /// <summary>What every user may do in the store, as its folder's rules say.</summary>
    private static FileSystemRights UsersRights()
        => new DirectoryInfo(Store).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
               .Where(r => r.IdentityReference == Users && r.AccessControlType == AccessControlType.Allow)
               .Aggregate((FileSystemRights)0, (all, r) => all | r.FileSystemRights);

    private sealed record Ran(int Exit, string Output, string Errors);

    /// <summary>Runs <paramref name="file"/>, what it said kept — and written to <paramref name="log"/> in the journey's results where one is named.</summary>
    private Ran Run(string file, IReadOnlyList<string> arguments, string? log = null)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(15))) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        var ran = new Ran(process.ExitCode, output.Result, errors.Result);
        if (log is not null && _logs.Length > 0) File.WriteAllText(Path.Combine(_logs, log), $"exit {ran.Exit}\n--- output\n{ran.Output}\n--- errors\n{ran.Errors}");
        return ran;
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
    }

    private static bool Until(Func<bool> condition, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (!condition())
        {
            if (DateTime.UtcNow > until) return false;
            System.Threading.Thread.Sleep(250);
        }
        return true;
    }
}
