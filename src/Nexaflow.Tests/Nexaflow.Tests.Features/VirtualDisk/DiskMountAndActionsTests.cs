using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.FileActions;
using Nexaflow.Features.VirtualDisk.Services;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>Mount policy (which formats, elevation gating, drive-letter parsing) and the disk file/folder
/// actions' contracts. Live mounting needs admin + real hardware, so these cover the routing, not the attach.</summary>
[TestClass]
[CoversNode("virtualdisk")]
public class DiskMountAndActionsTests
{
    [TestMethod]
    public void CanMount_OnlyNativelyMountableFormats()
    {
        Assert.IsTrue(MountSupport.CanMount("x.iso"));
        Assert.IsTrue(MountSupport.CanMount("x.vhd"));
        Assert.IsTrue(MountSupport.CanMount("x.vhdx"));
        Assert.IsFalse(MountSupport.CanMount("x.vmdk"));
        Assert.IsFalse(MountSupport.CanMount("x.zip"));
    }

    [TestMethod]
    public void RequiresElevation_TrueForEverythingButIso()
    {
        Assert.IsFalse(MountSupport.RequiresElevation("x.iso"));
        Assert.IsTrue(MountSupport.RequiresElevation("x.vhd"));
        Assert.IsTrue(MountSupport.RequiresElevation("x.vhdx"));
    }

    [TestMethod]
    public void ParseDrive_ExtractsAssignedLetter()
    {
        Assert.AreEqual("E:", DiskMounter.ParseDrive("DRIVE=E"));
        Assert.AreEqual("F:", DiskMounter.ParseDrive("noise\nDRIVE=f\n"));
        Assert.IsNull(DiskMounter.ParseDrive("no marker here"));
    }

    [TestMethod]
    public void OpenAsDisk_OpensInspectorTabForThePath()
    {
        var shell = Substitute.For<IShellServices>();
        var action = new OpenAsDiskAction(shell);

        Assert.AreEqual("/disk", action.ExperienceId);
        Assert.AreEqual("/disk", OpenAsDiskAction.StaticExperienceId);
        Assert.IsTrue(action.OpensViewer);

        Assert.IsTrue(action.PerformAction(@"C:\images\disk.vhdx"));
        shell.Received(1).OpenTab(
            "VirtualDisk",
            Arg.Is<Dictionary<string, string>>(d => d["path"] == @"C:\images\disk.vhdx"),
            Arg.Any<IPageView?>(),
            Arg.Any<bool>());
    }

    [TestMethod]
    public void Mount_TargetsTheMountableExperience()
    {
        var action = new MountDiskAction(Substitute.For<IShellServices>());
        Assert.AreEqual("/disk/mountable", action.ExperienceId);
        Assert.AreEqual("/disk/mountable", MountDiskAction.StaticExperienceId);
        Assert.AreEqual("Mount", action.DisplayName);
    }

    [TestMethod]
    public void Unmount_IsDriveAction_HiddenForNonDrivePaths()
    {
        var action = new UnmountDiskAction(Substitute.For<IShellServices>());
        Assert.IsTrue(action.AppliesToDrives);
        Assert.IsFalse(action.AppliesToRoot);
        // Non-drive-root paths can't parse a root letter → not applicable (and never touch WMI):
        Assert.IsFalse(action.AppliesToFolder(@"\\server\share"));
        Assert.IsFalse(action.AppliesToFolder("relative-path"));
        Assert.IsFalse(action.AppliesToFolder(@"E:\sub\folder"));   // a subfolder, not the drive root
    }
}
