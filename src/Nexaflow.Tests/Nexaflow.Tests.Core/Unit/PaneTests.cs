using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
public class PaneTests
{
    private static Page MakePage(string title = "Page") => new() { Title = title };

    [TestMethod]
    public void Add_InsertsAtFrontAndActivates()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        pane.Add(a);
        pane.Add(b);

        Assert.AreSame(b, pane.Pages[0]);
        Assert.AreSame(b, pane.ActivePage);
    }

    [TestMethod]
    public void Add_SetsIsActiveFlagOnNewPageAndClearsPrevious()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        pane.Add(a);
        pane.Add(b);

        Assert.IsTrue(b.IsActive);
        Assert.IsFalse(a.IsActive);
    }

    [TestMethod]
    public void Remove_WhenActive_ActivatesAdjacentPage()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        pane.Add(a);
        pane.Add(b); // b is at [0], active

        pane.Remove(b);

        Assert.AreSame(a, pane.ActivePage);
        Assert.IsTrue(a.IsActive);
    }

    [TestMethod]
    public void Remove_LastPage_SetsActiveNull()
    {
        var pane = new Pane();
        var a = MakePage();
        pane.Add(a);

        pane.Remove(a);

        Assert.IsNull(pane.ActivePage);
        Assert.AreEqual(0, pane.Pages.Count);
    }

    [TestMethod]
    public void Remove_UnknownPage_IsNoOp()
    {
        var pane = new Pane();
        var a = MakePage();
        pane.Add(a);

        pane.Remove(MakePage("stranger"));

        Assert.AreEqual(1, pane.Pages.Count);
    }

    [TestMethod]
    public void Remove_NonActive_DoesNotChangeActivePage()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        pane.Add(a);
        pane.Add(b); // b active, b at [0], a at [1]

        pane.Remove(a); // remove the non-active one

        Assert.AreSame(b, pane.ActivePage);
        Assert.AreEqual(1, pane.Pages.Count);
    }

    [TestMethod]
    public void BringToFront_MovesToIndex0WithoutChangingActive()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        var c = MakePage("C");
        pane.Add(a);
        pane.Add(b);
        pane.Add(c); // c at [0], b at [1], a at [2]; c is active

        pane.BringToFront(a);

        Assert.AreSame(a, pane.Pages[0]);
        Assert.AreSame(c, pane.ActivePage);
    }

    [TestMethod]
    public void BringToFront_AlreadyAtFront_IsNoOp()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        pane.Add(a);
        pane.Add(b); // b at [0]

        pane.BringToFront(b);

        Assert.AreSame(b, pane.Pages[0]);
        Assert.AreEqual(2, pane.Pages.Count);
    }

    [TestMethod]
    public void Move_ReordersPage()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        var c = MakePage("C");
        pane.Add(a);
        pane.Add(b);
        pane.Add(c); // c[0], b[1], a[2]

        pane.Move(a, 0);

        Assert.AreSame(a, pane.Pages[0]);
    }

    [TestMethod]
    public void Move_ClampsNegativeIndexToZero()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        pane.Add(a);
        pane.Add(b); // b[0], a[1]

        pane.Move(a, -99);

        Assert.AreSame(a, pane.Pages[0]);
    }

    [TestMethod]
    public void Move_ClampsTooLargeIndexToLast()
    {
        var pane = new Pane();
        var a = MakePage("A");
        var b = MakePage("B");
        pane.Add(a);
        pane.Add(b); // b[0], a[1]

        pane.Move(b, 99);

        Assert.AreSame(b, pane.Pages[pane.Pages.Count - 1]);
    }
}
