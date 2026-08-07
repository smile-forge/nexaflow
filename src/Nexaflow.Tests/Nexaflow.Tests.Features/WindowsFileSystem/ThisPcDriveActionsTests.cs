using System.Collections.Generic;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// In "This PC" mode the right-click menu and the action strip used to short-circuit before the drive-action
/// path could run — so right-clicking a drive gave no menu, and selecting a drive gave no action icons. The
/// machinery to resolve a drive's actions (<c>FilterFolderActions</c> honouring <c>AppliesToDrives</c>) was
/// already there; the guards just never let a drive reach it. These cover the right-click path
/// (<see cref="FileSystemViewModel.BuildContextActions"/>), which is synchronous; the action strip shares the
/// same resolution behind a debounce.
/// </summary>
[TestClass]
[CoversNode("winfs-context-menu")]
public class ThisPcDriveActionsTests
{
    // The registry only keeps folder-action types that implement ICacheable (one logical instance per
    // workspace); without it the discovery list drops them.

    /// <summary>A folder action that applies to drives — the kind that should surface for a selected drive.</summary>
    public sealed class DriveApplicableAction : IFolderAction, ICacheable
    {
        public bool   IsDestructive         => false;
        public bool   SupportsMultipleFiles => true;
        public string Icon                  => "💿";
        public string DisplayName           => "Drive Action";
        public bool   RequiresRefresh       => false;
        public bool   CanPerformAction      => true;
        public bool   AppliesToRoot         => false;
        public bool   AppliesToDrives       => true;

        public bool PerformAction(string folderPath) => true;
        public bool PerformAction(IEnumerable<string> folderPaths) => true;
    }

    /// <summary>A folder action that does NOT apply to drives — must be filtered out for a drive selection.</summary>
    public sealed class NonDriveAction : IFolderAction, ICacheable
    {
        public bool   IsDestructive         => false;
        public bool   SupportsMultipleFiles => true;
        public string Icon                  => "📁";
        public string DisplayName           => "Folder Only";
        public bool   RequiresRefresh       => false;
        public bool   CanPerformAction      => true;
        public bool   AppliesToRoot         => false;
        public bool   AppliesToDrives       => false;

        public bool PerformAction(string folderPath) => true;
        public bool PerformAction(IEnumerable<string> folderPaths) => true;
    }

    private static FileSystemViewModel ThisPc()
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();

        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>()
             .Returns([typeof(DriveApplicableAction), typeof(NonDriveAction)]);
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());

        return FileSystemViewModel.CreateThisPc(shell, ai, new Dictionary<Type, IFeatureConfig>());
    }

    private static FileSystemEntry Drive(string letter) =>
        new() { Name = $"{letter}:\\", FullPath = $"{letter}:\\", IsDirectory = true, IsThisPcItem = true };

    [TestMethod]
    public void RightClickingADrive_ProducesTheDriveApplicableActions()
    {
        var vm = ThisPc();

        var actions = vm.BuildContextActions([Drive("C")]);

        var names = actions.Select(a => a.DisplayName).ToList();
        CollectionAssert.Contains(names, "Drive Action");
        CollectionAssert.DoesNotContain(names, "Folder Only",
            "a non-drive folder action must not appear for a drive");
    }

    [TestMethod]
    public void RightClickingEmptySpaceInThisPc_ProducesNoMenu()
    {
        // Correct behaviour: nothing selected at the This PC root offers nothing.
        var vm = ThisPc();

        Assert.AreEqual(0, vm.BuildContextActions([]).Count);
    }

    [TestMethod]
    public void MultipleDrivesSelected_StillResolveTheDriveActions()
    {
        var vm = ThisPc();

        var actions = vm.BuildContextActions([Drive("C"), Drive("D")]);

        CollectionAssert.Contains(actions.Select(a => a.DisplayName).ToList(), "Drive Action");
    }
}
