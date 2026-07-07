using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UserControl = System.Windows.Controls.UserControl;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
public class ShellServicesTests
{
    private static ShellServices CreateSvc() => new(new WorkspaceRuntime());

    /// <summary>
    /// Seeds a page directly into ShellServices' private tab registry.
    /// OpenTab cannot be used in tests because it marshals through the captured UI dispatcher.
    /// </summary>
    private static void SeedTab(ShellServices svc, Page page, FakeWindowHost host)
    {
        var field = typeof(ShellServices)
            .GetField("_tabToWindow", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (Dictionary<Page, IWindowHost>)field.GetValue(svc)!;
        dict[page] = host;
    }

    [TestMethod]
    public void FindTab_TypeIdentity_MatchesRequestWithParams()
    {
        var svc = CreateSvc();
        var host = new FakeWindowHost();
        svc.RegisterWindow(host);
        var tab = new Page { PageKind = "Files", PageParams = null };
        SeedTab(svc, tab, host);

        var found = svc.FindTab("Files", new() { { "path", @"C:\foo" } });

        Assert.AreSame(tab, found);
    }

    [TestMethod]
    public void FindTab_LocationIdentity_WrongParams_ReturnsNull()
    {
        var svc = CreateSvc();
        var host = new FakeWindowHost();
        svc.RegisterWindow(host);
        var tab = new Page { PageKind = "Files", PageParams = new() { { "path", @"C:\foo" } } };
        SeedTab(svc, tab, host);

        var found = svc.FindTab("Files", new() { { "path", @"C:\bar" } });

        Assert.IsNull(found);
    }

    [TestMethod]
    public void FindTab_LocationIdentity_CorrectParams_ReturnsTab()
    {
        var svc = CreateSvc();
        var host = new FakeWindowHost();
        svc.RegisterWindow(host);
        var tab = new Page { PageKind = "Files", PageParams = new() { { "path", @"C:\foo" } } };
        SeedTab(svc, tab, host);

        var found = svc.FindTab("Files", new() { { "path", @"C:\foo" } });

        Assert.AreSame(tab, found);
    }

    [TestMethod]
    public void FindTab_LocationIdentity_NullRequest_ReturnsNull()
    {
        var svc = CreateSvc();
        var host = new FakeWindowHost();
        svc.RegisterWindow(host);
        var tab = new Page { PageKind = "Files", PageParams = new() { { "path", @"C:\foo" } } };
        SeedTab(svc, tab, host);

        var found = svc.FindTab("Files", null);

        Assert.IsNull(found);
    }

    [TestMethod]
    public void FindTab_UnknownKind_ReturnsNull()
    {
        var svc = CreateSvc();
        Assert.IsNull(svc.FindTab("DoesNotExist"));
    }

    [TestMethod]
    public void CloseTab_RemovesFromRegistry()
    {
        var svc = CreateSvc();
        var host = new FakeWindowHost();
        svc.RegisterWindow(host);
        var tab = new Page { PageKind = "Files" };
        SeedTab(svc, tab, host);

        svc.CloseTab(tab);

        Assert.IsNull(svc.FindTab("Files"));
    }

    [TestMethod]
    public void CloseTab_UnknownTab_IsNoOp()
    {
        var svc = CreateSvc();
        var host = new FakeWindowHost();
        svc.RegisterWindow(host);

        // Should not throw
        svc.CloseTab(new Page { PageKind = "Files" });
    }

    [TestMethod]
    public void CloseWindowTabs_RaisesClosedForEachTab_AndClearsRegistry()
    {
        var svc = CreateSvc();
        var host = new FakeWindowHost();
        svc.RegisterWindow(host);

        var a = new Page { PageKind = "Console" };
        var b = new Page { PageKind = "Html" };
        host.AddTab(a);
        host.AddTab(b);
        SeedTab(svc, a, host);
        SeedTab(svc, b, host);

        int closedA = 0, closedB = 0;
        a.Closed += (_, _) => closedA++;
        b.Closed += (_, _) => closedB++;

        // Simulates a window closing for good (OS close / app shutdown). This is the path that
        // previously dropped tabs without firing Closed, orphaning their view-models' processes.
        svc.CloseWindowTabs(host);

        Assert.AreEqual(1, closedA, "tab A should have Closed raised exactly once");
        Assert.AreEqual(1, closedB, "tab B should have Closed raised exactly once");
        Assert.IsNull(svc.FindTab("Console"), "tab A should be gone from the registry");
        Assert.IsNull(svc.FindTab("Html"),    "tab B should be gone from the registry");
    }

    // ── Central content disposal (safety net for features that forget page.Closed += vm.Dispose()) ──

    [TestMethod]
    public void CloseTab_DisposesContentViewModel_WhenDisposable()
    {
        RunSta(() =>
        {
            var svc  = CreateSvc();
            var host = new FakeWindowHost();
            svc.RegisterWindow(host);

            var vm   = new DisposableVm();
            var page = new Page { PageKind = "Files", ContentFactory = () => new FakePageView(vm) };
            page.GetOrCreateContent();            // simulate the tab having been activated
            host.AddTab(page);
            SeedTab(svc, page, host);

            svc.CloseTab(page);

            Assert.AreEqual(1, vm.DisposeCount, "closing an activated tab must dispose its IDisposable view-model");
        });
    }

    [TestMethod]
    public void CloseWindowTabs_DisposesContentViewModels()
    {
        RunSta(() =>
        {
            var svc  = CreateSvc();
            var host = new FakeWindowHost();
            svc.RegisterWindow(host);

            var vm   = new DisposableVm();
            var page = new Page { PageKind = "Console", ContentFactory = () => new FakePageView(vm) };
            page.GetOrCreateContent();
            host.AddTab(page);
            SeedTab(svc, page, host);

            svc.CloseWindowTabs(host);

            Assert.AreEqual(1, vm.DisposeCount, "a closing window must dispose its tabs' IDisposable view-models");
        });
    }

    [TestMethod]
    public void CloseTab_NeverActivatedTab_DisposesNothing()
    {
        var svc  = CreateSvc();
        var host = new FakeWindowHost();
        svc.RegisterWindow(host);

        var vm   = new DisposableVm();
        // Content is never created (tab never activated), so the factory never runs and there is nothing
        // to dispose — and crucially closing must NOT materialise content just to tear it down.
        var page = new Page { PageKind = "Files", ContentFactory = () => new FakePageView(vm) };
        SeedTab(svc, page, host);

        svc.CloseTab(page);

        Assert.AreEqual(0, vm.DisposeCount);
    }

    // A page view-model that records how many times it was disposed (to prove idempotent, single teardown).
    private sealed class DisposableVm : IPageViewModel, IDisposable
    {
        public int DisposeCount { get; private set; }
        public string GetContext() => string.Empty;
        public void Dispose() => DisposeCount++;
    }

    // Minimal content view exposing the VM through IPageView (a real UserControl → needs an STA thread).
    private sealed class FakePageView : UserControl, IPageView
    {
        public FakePageView(IPageViewModel vm) => ViewModel = vm;
        public IPageViewModel? ViewModel { get; }
    }

    // Runs an action on an STA thread and rethrows — WPF UserControl construction requires STA.
    private static void RunSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { caught = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null) throw caught;
    }
}
