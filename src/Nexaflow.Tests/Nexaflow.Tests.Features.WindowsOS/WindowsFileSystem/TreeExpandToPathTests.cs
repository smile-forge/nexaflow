using System;
using System.IO;
using System.Linq;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Expand-to-path descends only into genuine ancestors. The descend test is a path-segment test, not a
/// string prefix: "C:\Data" is not an ancestor of "C:\Datasets", and treating it as one expanded every
/// sibling whose name the target merely starts with.
/// </summary>
[TestClass]
[CoversNode("winfs-tree")]
public class TreeExpandToPathTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexa-tree-" + Guid.NewGuid().ToString("N"));
        foreach (var name in new[] { "Data", "Datasets", "Dat" })
            Directory.CreateDirectory(Path.Combine(_root, name, "child"));
    }

    [TestCleanup]
    public void RemoveTree() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private FileSystemViewModel Vm()
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        return new FileSystemViewModel(_root, shell, ai, new Dictionary<Type, IFeatureConfig>());
    }

    private static FileSystemTreeNode Node(FileSystemViewModel vm, string name)
        => vm.TreeRoots[0].Children.Single(c => c.Name == name);

    [TestMethod]
    public void ExpandTo_Target_IsExpandedAndSelected()
    {
        var vm = Vm();

        vm.SelectAndExpandPath(Path.Combine(_root, "Datasets"));

        var target = Node(vm, "Datasets");
        Assert.IsTrue(target.IsSelected, "the target node should be selected");
        Assert.IsTrue(target.IsExpanded, "the target node should be expanded");
    }

    [TestMethod]
    [DataRow("Data")]   // a strict string prefix of the target
    [DataRow("Dat")]    // …and a prefix of that prefix
    public void ExpandTo_NamePrefixSibling_StaysCollapsed(string sibling)
    {
        var vm = Vm();

        vm.SelectAndExpandPath(Path.Combine(_root, "Datasets"));

        Assert.IsFalse(Node(vm, sibling).IsExpanded,
            $"'{sibling}' only shares a name prefix with 'Datasets' — it is not an ancestor of it");
    }

    [TestMethod]
    public void ExpandTo_NamePrefixSibling_StaysUnselected()
    {
        var vm = Vm();

        vm.SelectAndExpandPath(Path.Combine(_root, "Datasets"));

        Assert.IsFalse(Node(vm, "Data").IsSelected);
    }

    [TestMethod]
    public void ExpandTo_NestedTarget_ExpandsRealAncestorOnly()
    {
        var vm = Vm();

        vm.SelectAndExpandPath(Path.Combine(_root, "Datasets", "child"));

        Assert.IsTrue(Node(vm, "Datasets").IsExpanded, "a real ancestor expands");
        Assert.IsFalse(Node(vm, "Data").IsExpanded, "a name-prefix sibling does not");
    }
}
