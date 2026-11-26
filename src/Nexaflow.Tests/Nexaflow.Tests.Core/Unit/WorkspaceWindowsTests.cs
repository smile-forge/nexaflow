using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Which workspace each window shows: a window moved joins the other workspace's runtime and leaves its own; the last workspace
/// can't be deleted; deleting the one a window shows moves that window first and ends the workspace's runtime; and a workspace
/// dropped from the list takes its windows to the first one left.
/// </summary>
[TestClass]
[CoversNode("ws-lifetimes")]
public class WorkspaceWindowsTests
{
    private WorkspaceRuntimes _runtimes = null!;
    private WorkspaceWindows _windows = null!;
    private WorkspaceManager _saved = null!;

    [TestCleanup]
    public void Done()
    {
        _windows?.Dispose();
        _runtimes?.Dispose();
    }

    private (Workspace First, Workspace Second) TwoWorkspaces()
    {
        var first  = new Workspace { Name = "First-"  + Guid.NewGuid().ToString("N"), DefaultTabs = [] };
        var second = new Workspace { Name = "Second-" + Guid.NewGuid().ToString("N"), DefaultTabs = [] };
        Session(first, second);
        return (first, second);
    }

    private void Session(params Workspace[] workspaces)
    {
        _saved    = TestRuntimes.SavedListOf(workspaces);
        _runtimes = new WorkspaceRuntimes(TestRuntimes.FactoryWith(tasks: null, notify: _ => { }, _saved), TestRuntimes.CurrentUi(),
                                          () => TimeSpan.FromMinutes(5), _saved.Workspaces.Contains, TimeProvider.System,
                                          TestRuntimes.LoggerFor<WorkspaceRuntimes>());
        _windows  = new WorkspaceWindows(_saved, _runtimes, TestRuntimes.CurrentUi());
    }

    private FakeWindowHost WindowOn(WorkspaceRuntime runtime)
    {
        var window = new FakeWindowHost();
        runtime.ShellServices.RegisterWindow(window);
        return window;
    }

    [TestMethod]
    public void AWindowMoved_JoinsTheOtherWorkspace_AndLeavesItsOwn()
    {
        var (first, second) = TwoWorkspaces();
        var from   = _runtimes.Open(first);
        var window = WindowOn(from);

        var joined = _windows.Move(window, from, second);

        Assert.AreSame(second, joined.Workspace);
        CollectionAssert.Contains(joined.ShellServices.Windows.ToList(), window);
        CollectionAssert.DoesNotContain(from.ShellServices.Windows.ToList(), window);
        CollectionAssert.AreEqual(new[] { joined }, window.Rejoined);
        Assert.AreEqual(WorkspaceRuntimeState.Dormant, from.State, "left by its last window");
    }

    [TestMethod]
    public void TheLastWorkspace_IsNotDeleted()
    {
        var only = new Workspace { Name = "Only-" + Guid.NewGuid().ToString("N"), DefaultTabs = [] };
        Session(only);
        var window = WindowOn(_runtimes.Open(only));

        Assert.IsFalse(_windows.CanDelete(only));
        Assert.IsFalse(_windows.Delete(only, window));
        CollectionAssert.Contains(_saved.Workspaces, only);
    }

    [TestMethod]
    public void DeletingTheWorkspaceTheAskingWindowShows_MovesItFirst_AndEndsTheRuntime()
    {
        var (first, second) = TwoWorkspaces();
        var window = WindowOn(_runtimes.Open(first));

        Assert.IsTrue(_windows.Delete(first, window));

        Assert.AreSame(second, window.Rejoined.Single().Workspace);
        Assert.IsNull(_runtimes.Find(first));
        CollectionAssert.DoesNotContain(_saved.Workspaces, first);
    }

    [TestMethod]
    public void AWorkspaceDroppedFromTheList_TakesItsWindowsToTheFirstLeft()
    {
        var (first, second) = TwoWorkspaces();
        var runtime = _runtimes.Open(second);
        var window  = WindowOn(runtime);

        _saved.DeleteWorkspace(second);   // as Options does: the list alone, not the windows

        Assert.AreSame(first, window.Rejoined.Single().Workspace);
        CollectionAssert.DoesNotContain(runtime.ShellServices.Windows.ToList(), window);
    }
}
