using System.Collections.Generic;
using System.Reflection;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
public class ShellServicesTests
{
    private static ShellServices CreateSvc() => new(new WorkContext());

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
    public void TransferWindowTo_MovesTabToTargetRegistry()
    {
        var src = CreateSvc();
        var dst = CreateSvc();
        var host = new FakeWindowHost();
        src.RegisterWindow(host);
        var tab = new Page { PageKind = "Files" };
        host.AddTab(tab);
        SeedTab(src, tab, host);

        src.TransferWindowTo(host, dst);

        Assert.IsNull(src.FindTab("Files"));
        Assert.IsNotNull(dst.FindTab("Files"));
    }
}
