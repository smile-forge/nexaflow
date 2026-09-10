using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsFileSystem.FileActions;

/// <summary>
/// What "Copy path" puts on the clipboard. The clipboard write itself is one line with no rule in it, and
/// a test that takes the clipboard interferes with whatever else the machine is doing — so what is asserted
/// is the text a selection produces, and the refusal that keeps an empty one from reaching the clipboard at
/// all (writing nothing REPLACES whatever the user had copied; see <see cref="CopyPathsConformance"/>).
/// </summary>
[TestClass]
[CoversNode("winfs-act-copy-path")]
public class CopyPathsTests
{
    [TestMethod]
    public void OnePath_CopiesItVerbatim()
        => Assert.AreEqual(@"C:\docs\notes.txt", CopyPaths.ClipboardText([@"C:\docs\notes.txt"]));

    [TestMethod]
    public void ManyPaths_CopyOnePerLine()
        => Assert.AreEqual($@"C:\a.txt{Environment.NewLine}C:\b.txt",
                           CopyPaths.ClipboardText([@"C:\a.txt", @"C:\b.txt"]));

    [TestMethod]
    public void ANonExistentPathIsStillCopied()
        // The action names a location, it does not open one — a broken link's target is exactly what the
        // user is trying to paste somewhere to find out about.
        => Assert.AreEqual(@"C:\gone.txt", CopyPaths.ClipboardText([@"C:\gone.txt"]));

    [TestMethod]
    public void AVirtualPathIsCopiedAsShown()
        // Inside an archive the path on screen is the virtual one, which is what Nexaflow itself reopens.
        // Materialising to a temp copy would name a location the user never asked about.
        => Assert.AreEqual(@"C:\bundle.zip\inner\x.txt", CopyPaths.ClipboardText([@"C:\bundle.zip\inner\x.txt"]));

    [TestMethod]
    public void NothingToCopy_ProducesNoText()
    {
        // Null rather than "" — the difference between "copy this empty string over their clipboard" and
        // "there is nothing to copy, leave it alone".
        Assert.IsNull(CopyPaths.ClipboardText([]));
        Assert.IsNull(CopyPaths.ClipboardText([" ", ""]));
    }

    [TestMethod]
    public void ABlankEntryIsDroppedRatherThanCopiedAsABlankLine()
        => Assert.AreEqual(@"C:\a.txt", CopyPaths.ClipboardText([@"C:\a.txt", "  "]));

    [TestMethod]
    public void ItIsOfferedForDrivesButNotForAnEmptySelection()
    {
        // Drives so the This PC list and the tree's drive nodes both carry it; not the open folder, whose
        // path the breadcrumb bar already offers — the action stands for the item you clicked.
        var action = (IFolderAction)new CopyPaths();

        Assert.IsTrue(action.AppliesToDrives);
        Assert.IsFalse(action.AppliesToRoot);
    }

    [TestMethod]
    public void ItReachesTheMenuForATreeFolderAndForADrive()
    {
        // End of the wiring, not just the action's own flags: the registry discovers it as a folder action
        // (it is ICacheable, which the folder registry requires) and the view-model offers it for the entry
        // the directory tree builds for the node under the cursor — a plain folder, or a drive.
        var shell = Substitute.For<IShellServices>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns([typeof(CopyPaths)]);
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());

        var vm = FileSystemViewModel.CreateThisPc(
            shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>());

        CollectionAssert.Contains(
            Offered(vm, new FileSystemEntry { Name = "docs", FullPath = @"C:\docs", IsDirectory = true }),
            "Copy path", "a folder in the tree");

        CollectionAssert.Contains(
            Offered(vm, new FileSystemEntry { Name = @"C:\", FullPath = @"C:\", IsDirectory = true, IsThisPcItem = true }),
            "Copy path", "a drive");
    }

    private static List<string> Offered(FileSystemViewModel vm, FileSystemEntry entry)
        => vm.BuildContextActions([entry]).Select(a => a.DisplayName).ToList();
}
