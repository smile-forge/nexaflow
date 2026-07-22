using System.IO;
using System.Windows.Input;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Shift+Enter opens the selected entry — the keyboard equivalent of double-clicking its row. Plain Enter
/// deliberately does nothing here: the AI input normally holds focus and owns that key, so the binding is
/// Shift-modified. Open acts on exactly one entry, mirroring a double-click.
/// </summary>
[TestClass]
[CoversNode("winfs-keyboard-open")]
public class FileSystemKeyboardHandlerTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"fskeys_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    private FileSystemViewModel AtScratch()
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());

        return new FileSystemViewModel(_scratch, shell, ai, new Dictionary<Type, IFeatureConfig>());
    }

    /// <summary>A directory entry is the deterministic case: OpenEntry navigates rather than dispatching to
    /// the default-open resolver, so the effect is observable without the shell actually launching a viewer.</summary>
    private FileSystemEntry Subfolder(string name)
    {
        var path = Path.Combine(_scratch, name);
        Directory.CreateDirectory(path);
        return new FileSystemEntry { Name = name, FullPath = path, IsDirectory = true };
    }

    [TestMethod]
    public void ShiftEnter_isOffered_forASingleSelection()
    {
        var vm = AtScratch();
        vm.OnSelectionChanged([Subfolder("one")]);

        Assert.IsTrue(new FileSystemKeyboardHandler(vm).CanProcessKey(Key.Enter, ModifierKeys.Shift));
    }

    [TestMethod]
    public void ShiftEnter_isNotOffered_withNoSelection()
    {
        var vm = AtScratch();
        vm.OnSelectionChanged([]);

        Assert.IsFalse(new FileSystemKeyboardHandler(vm).CanProcessKey(Key.Enter, ModifierKeys.Shift));
    }

    [TestMethod]
    public void ShiftEnter_isNotOffered_forAMultiSelection()
    {
        var vm = AtScratch();
        vm.OnSelectionChanged([Subfolder("one"), Subfolder("two")]);

        Assert.IsFalse(new FileSystemKeyboardHandler(vm).CanProcessKey(Key.Enter, ModifierKeys.Shift),
            "Open mirrors a double-click, which acts on exactly one row.");
    }

    [TestMethod]
    public void PlainEnter_isNeverClaimed_soTheAiInputKeepsIt()
    {
        var vm = AtScratch();
        vm.OnSelectionChanged([Subfolder("one")]);

        Assert.IsFalse(new FileSystemKeyboardHandler(vm).CanProcessKey(Key.Enter, ModifierKeys.None));
    }

    [TestMethod]
    public void ShiftEnter_opensTheSelectedFolder_byNavigatingToIt()
    {
        var vm = AtScratch();
        var target = Subfolder("target");
        vm.OnSelectionChanged([target]);

        Assert.IsTrue(new FileSystemKeyboardHandler(vm).ProcessKey(Key.Enter, ModifierKeys.Shift));
        Assert.AreEqual(target.FullPath, vm.CurrentPath,
            "Shift+Enter must route through OpenEntryCommand, which navigates into a selected folder.");
    }
}
