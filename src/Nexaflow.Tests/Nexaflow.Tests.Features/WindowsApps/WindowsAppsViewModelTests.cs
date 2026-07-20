using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Features.WindowsApps.Services;
using Nexaflow.Features.WindowsApps.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsApps;

[TestClass]
public class WindowsAppsViewModelTests
{
    // Capture the pass-1 onComplete the VM hands to QueueBackgroundTask so a test can simulate the scan
    // finishing without running it (which would hit the real registry).
    private static (WindowsAppsViewModel Vm, Action<bool> Complete) Build()
    {
        Action<bool>? captured = null;
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci => captured = ci.Arg<Action<bool>>());

        var vm = new WindowsAppsViewModel(shell);
        Assert.IsNotNull(captured, "Constructor should queue a background scan.");
        return (vm, captured!);
    }

    // Builds a VM whose scan is backed by an in-memory app source and runs synchronously, so the Apps list
    // is deterministically populated headless (no live registry/WMI scan) by the time the ctor returns.
    private static WindowsAppsViewModel BuildLoaded(params InstalledApp[] apps)
    {
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(
                Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci =>
             {
                 // Run the queued task on the calling thread, then fire the completion callback (which the
                 // shell would normally marshal to the UI thread) so the VM populates from the scan result.
                 var task = ci.Arg<IBackgroundTask>();
                 task.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                 ci.Arg<Action<bool>>()?.Invoke(true);
             });

        var service = new InstalledAppsService([new FakeSource(apps)]);
        return new WindowsAppsViewModel(shell, service);
    }

    // A stand-in installed-app source returning a fixed set — no registry / package-manager access.
    private sealed class FakeSource(IReadOnlyList<InstalledApp> apps) : IInstalledAppSource
    {
        public AppSource Source => AppSource.Win32;
        public Task<IReadOnlyList<InstalledApp>> EnumerateAsync(CancellationToken ct) => Task.FromResult(apps);
        public Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct) =>
            Task.FromResult(UninstallResult.Ok);
    }

    [TestMethod]
    [CoversNode("windowsapps-ai-context")]
    public void IsContextReady_WhileScanning_False()
    {
        var (vm, _) = Build();

        Assert.IsFalse(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "still scanning");
    }

    [TestMethod]
    [CoversNode("windowsapps-ai-context")]
    public void IsContextReady_AfterScanFinishes_True()
    {
        // Ready once pass 1 finishes — here via the failure path, which also proves a failed scan
        // releases the gate instead of wedging it.
        var (vm, complete) = Build();

        complete(false);

        Assert.IsTrue(vm.IsContextReady);
    }

    [TestMethod]
    [CoversNode("windowsapps-ai-context")]
    public void IsContextReady_Flip_RaisesPropertyChanged()
    {
        var (vm, complete) = Build();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WindowsAppsViewModel.IsContextReady)) raised = true;
        };

        complete(false);

        Assert.IsTrue(raised);
    }

    // ── AI integration: context + scope + the read-only act tools ─────────────
    [TestMethod]
    [CoversNode("windowsapps-ai-act")]
    [CoversNode("windowsapps-ai-context")]
    public async Task AiTools_ListAndDetails_ThroughToolSurface()
    {
        var vm = BuildLoaded(
            new InstalledApp
            {
                Name = "Contoso Editor", Publisher = "Contoso Ltd", Version = "1.2.3",
                InstallDate = new DateTime(2025, 1, 2), SizeBytes = 5_000_000,
                Source = AppSource.Win32, InstallLocation = null,
            },
            new InstalledApp
            {
                Name = "Fabrikam Player", Publisher = "Fabrikam", Version = "9.0",
                SizeBytes = 2_000_000, Source = AppSource.Store,
            });

        // Scope: stable, non-null so two installed-apps tabs don't collapse first-wins.
        Assert.AreEqual("installed-applications", vm.GetSecurityContext());

        // Context reflects the loaded list once the scan finished.
        Assert.IsTrue(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "Contoso Editor");

        var tools = vm.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "list_installed_applications", "get_application_details" },
            tools.Select(t => t.Name).ToArray(),
            "the WindowsApps AI act tool surface changed — update the tree's windowsapps-ai-act leaves to match");

        // list_installed_applications → every installed app name.
        var list = tools.Single(t => t.Name == "list_installed_applications");
        var listed = await list.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsFalse(listed.IsError);
        StringAssert.Contains(listed.ModelText, "Contoso Editor");
        StringAssert.Contains(listed.ModelText, "Fabrikam Player");

        // get_application_details → publisher/version/etc. for one app, matched by (partial) name.
        var details = tools.Single(t => t.Name == "get_application_details");
        var one = await details.InvokeAsync(new JsonObject { ["name"] = "Contoso" }, CancellationToken.None);
        Assert.IsFalse(one.IsError);
        StringAssert.Contains(one.ModelText, "Contoso Editor");
        StringAssert.Contains(one.ModelText, "Contoso Ltd");
        StringAssert.Contains(one.ModelText, "1.2.3");

        // A miss is reported to the model, not thrown.
        var miss = await details.InvokeAsync(new JsonObject { ["name"] = "Nope" }, CancellationToken.None);
        Assert.IsTrue(miss.IsError);
    }
}
