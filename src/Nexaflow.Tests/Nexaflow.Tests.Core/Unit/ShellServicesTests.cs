using System.Collections.Generic;
using System.Reflection;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
public class ShellServicesTests
{
    private static ShellServices CreateSvc() => new(new Workspace());

    /// <summary>
    /// Seeds a page directly into ShellServices' private tab registry.
    /// OpenTab cannot be used in tests because it marshals to Application.Current.Dispatcher.
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
}
