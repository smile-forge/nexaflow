using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.SystemInfo.ViewModels;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

/// <summary>
/// AI integration for the System Information feature: the read/query client tools the Services and
/// Environment-Variables pages surface, plus the honesty of their pinned context and the stable security
/// scope those tools act within. Both pages gather off the UI thread via <see cref="IShellServices.QueueBackgroundTask"/>,
/// so the fake shell here runs that gather inline (against live OS state — WMI services + the registry env
/// scopes), which is deterministic enough to assert well-known entries (a live service, the machine PATH).
/// <para>
/// Only the SAFE / read tools are exercised. The machine-mutating tools are asserted to be
/// <see cref="ToolSafety.RequiresApproval"/> but never invoked: <c>start_service</c> / <c>stop_service</c> /
/// <c>restart_service</c> / <c>set_service_start_mode</c> would change real Windows services, and
/// <c>set_environment_variable</c> / <c>delete_environment_variable</c> write the persistent User (HKCU) or
/// Machine (HKLM) registry — none are safe to drive from a unit test.
/// </para>
/// </summary>
[TestClass]
public class SystemInfoAiToolsTests
{
    // A shell whose QueueBackgroundTask runs the task inline on the caller's thread and then reports it
    // complete — so the VM's ObservableCollections populate (on this thread) against live OS state before
    // the constructor returns, exactly as the real shell would after its off-thread gather.
    private static IShellServices InlineGatherShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci =>
             {
                 var task = ci.Arg<IBackgroundTask>();
                 var onComplete = ci.Arg<Action<bool>>();
                 var ct = ci.Arg<CancellationToken>();
                 bool ok;
                 try { task.RunAsync(ct).GetAwaiter().GetResult(); ok = true; }
                 catch { ok = false; }
                 onComplete?.Invoke(ok);
             });
        return shell;
    }

    [TestMethod]
    [CoversNode("sysinfo-ai-act")]
    [CoversNode("sysinfo-ai-context")]
    public async Task ReadTools_ThroughToolSurface_QueryLiveServicesAndEnv()
    {
        using var services = new ServicesViewModel(InlineGatherShell());
        using var env      = new EnvironmentVariablesViewModel(InlineGatherShell());

        // ── Context is ready + honest, and each page names a stable (non-null) security scope ──────────
        Assert.IsTrue(services.IsContextReady, "the inline gather should have released the Services context.");
        Assert.IsTrue(services.Services.Count > 0, "live WMI returned no services — the gather failed.");
        StringAssert.Contains(services.GetContext(), Environment.MachineName);
        StringAssert.Contains(services.GetContext(), $"{services.Services.Count} services",
            "the Services context must report the real number of services it loaded.");
        Assert.AreEqual("system-services", services.GetSecurityContext());

        Assert.IsTrue(env.IsContextReady, "the inline gather should have released the Env-vars context.");
        StringAssert.Contains(env.GetContext(), Environment.MachineName);
        StringAssert.Contains(env.GetContext(), "Environment variables on");
        Assert.AreEqual("system-env-vars", env.GetSecurityContext());

        // ── Services tool surface: read tools auto-run; mutating tools are approval-gated ──────────────
        var svcTools = services.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "get_services", "find_service", "get_service",
                    "start_service", "stop_service", "restart_service", "set_service_start_mode" },
            svcTools.Select(t => t.Name).ToArray(),
            "the SystemInfo services tool surface changed — update the tree's sysinfo-ai-act leaves to match");

        foreach (var read in new[] { "get_services", "find_service", "get_service" })
            Assert.AreEqual(ToolSafety.SafeOperation, svcTools.Single(t => t.Name == read).Safety, read);
        foreach (var act in new[] { "start_service", "stop_service", "restart_service", "set_service_start_mode" })
            Assert.AreEqual(ToolSafety.RequiresApproval, svcTools.Single(t => t.Name == act).Safety, act);

        // get_services lists live services
        var all = await svcTools.Single(t => t.Name == "get_services")
                                .InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(all.IsError);
        StringAssert.Contains(all.ModelText, "service");

        // a real service from live state resolves through both find_service and get_service
        var known = services.Services.First().Name;

        var found = await svcTools.Single(t => t.Name == "find_service")
                                  .InvokeAsync(new JsonObject { ["query"] = known }, CancellationToken.None);
        Assert.IsFalse(found.IsError);
        StringAssert.Contains(found.ModelText, known);

        var one = await svcTools.Single(t => t.Name == "get_service")
                                .InvokeAsync(new JsonObject { ["name"] = known }, CancellationToken.None);
        Assert.IsFalse(one.IsError);
        StringAssert.Contains(one.ModelText, known);

        // an unknown service is reported as an error the model can react to (not thrown)
        var noSvc = await svcTools.Single(t => t.Name == "get_service")
                                  .InvokeAsync(new JsonObject { ["name"] = "NoSuchService_" + Guid.NewGuid().ToString("N") },
                                               CancellationToken.None);
        Assert.IsTrue(noSvc.IsError);

        // ── Env-var tool surface: read tools auto-run; set/delete are approval-gated ───────────────────
        var envTools = env.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "get_environment_variables", "get_environment_variable",
                    "set_environment_variable", "delete_environment_variable" },
            envTools.Select(t => t.Name).ToArray(),
            "the SystemInfo env-var tool surface changed — update the tree's sysinfo-ai-act leaves to match");

        foreach (var read in new[] { "get_environment_variables", "get_environment_variable" })
            Assert.AreEqual(ToolSafety.SafeOperation, envTools.Single(t => t.Name == read).Safety, read);
        foreach (var act in new[] { "set_environment_variable", "delete_environment_variable" })
            Assert.AreEqual(ToolSafety.RequiresApproval, envTools.Single(t => t.Name == act).Safety, act);

        // get_environment_variables lists both scopes — machine PATH is always present
        var list = await envTools.Single(t => t.Name == "get_environment_variables")
                                 .InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(list.IsError);
        StringAssert.Contains(list.ModelText, "Path");

        // get_environment_variable returns the full PATH value
        var path = await envTools.Single(t => t.Name == "get_environment_variable")
                                 .InvokeAsync(new JsonObject { ["name"] = "Path" }, CancellationToken.None);
        Assert.IsFalse(path.IsError);
        StringAssert.Contains(path.ModelText, "Path");

        // an unknown variable is reported as an error
        var noVar = await envTools.Single(t => t.Name == "get_environment_variable")
                                  .InvokeAsync(new JsonObject { ["name"] = "NEXAFLOW_NO_SUCH_VAR_" + Guid.NewGuid().ToString("N") },
                                               CancellationToken.None);
        Assert.IsTrue(noVar.IsError);
    }
}
