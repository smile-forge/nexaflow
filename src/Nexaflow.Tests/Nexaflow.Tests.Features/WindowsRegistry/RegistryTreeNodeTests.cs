using Nexaflow.Features.WindowsRegistry.Services;
using Nexaflow.Features.WindowsRegistry.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsRegistry;

/// <summary>
/// The left-tree key node's path composition and selection notification. Uses a non-existent HKCU
/// subpath so the lazy-load probe finds nothing and no real keys are enumerated.
/// </summary>
[TestClass]
[CoversNode("registry-key-tree")]
public class RegistryTreeNodeTests
{
    private const string MissingSub = @"Software\__NexaflowNoSuchKey__";

    [TestMethod]
    public void FullPath_ForHiveRoot_IsJustTheToken()
    {
        var node = new RegistryTreeNode(RegistryRoot.CurrentUser, "", "HKCU");
        Assert.AreEqual("HKCU", node.FullPath);
        Assert.AreEqual("", node.SubPath);
    }

    [TestMethod]
    public void FullPath_ForSubKey_JoinsTokenAndSubPath()
    {
        var node = new RegistryTreeNode(RegistryRoot.CurrentUser, MissingSub, "__NexaflowNoSuchKey__");
        Assert.AreEqual(@"HKCU\" + MissingSub, node.FullPath);
        Assert.AreEqual("__NexaflowNoSuchKey__", node.Name);
    }

    [TestMethod]
    public void IsSelected_RaisesPropertyChanged()
    {
        var node = new RegistryTreeNode(RegistryRoot.CurrentUser, MissingSub, "x");
        var raised = new List<string?>();
        node.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        node.IsSelected = true;

        Assert.IsTrue(node.IsSelected);
        CollectionAssert.Contains(raised, nameof(RegistryTreeNode.IsSelected));
    }
}
