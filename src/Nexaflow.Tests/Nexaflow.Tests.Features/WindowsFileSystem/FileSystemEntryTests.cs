using System.ComponentModel;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[CoversNode("winfs-filelist")]
public class FileSystemEntryTests
{
    // ── TypeLabel ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void TypeLabel_Directory_ReturnsFolder()
    {
        var entry = new FileSystemEntry { Name = "docs", IsDirectory = true };

        Assert.AreEqual("Folder", entry.TypeLabel);
    }

    [TestMethod]
    public void TypeLabel_Drive_ReturnsDriveKindLabel()
    {
        var entry = new FileSystemEntry { IsDrive = true };
        entry.DriveKindLabel = "Local Disk";

        Assert.AreEqual("Local Disk", entry.TypeLabel);
    }

    [TestMethod]
    public void TypeLabel_FileWithExtension_UppercasedExtension()
    {
        var entry = new FileSystemEntry { Name = "Program.cs" };

        Assert.AreEqual("CS", entry.TypeLabel);
    }

    [TestMethod]
    public void TypeLabel_FileNoExtension_Empty()
    {
        var entry = new FileSystemEntry { Name = "Makefile" };

        Assert.AreEqual(string.Empty, entry.TypeLabel);
    }

    // ── SizeLabel ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void SizeLabel_Directory_Empty()
    {
        var entry = new FileSystemEntry { IsDirectory = true };

        Assert.AreEqual(string.Empty, entry.SizeLabel);
    }

    [TestMethod]
    public void SizeLabel_FileBytes_FormattedAsB()
    {
        var entry = new FileSystemEntry { SizeBytes = 800 };

        Assert.AreEqual("800 B", entry.SizeLabel);
    }

    [TestMethod]
    public void SizeLabel_FileKb_FormattedAsKb()
    {
        var entry = new FileSystemEntry { SizeBytes = 2048 };

        Assert.AreEqual("2.0 KB", entry.SizeLabel);
    }

    [TestMethod]
    public void SizeLabel_FileMb_FormattedAsMb()
    {
        var entry = new FileSystemEntry { SizeBytes = 1024L * 1024 * 5 };

        Assert.AreEqual("5.0 MB", entry.SizeLabel);
    }

    [TestMethod]
    public void SizeLabel_Drive_ZeroTotal_Empty()
    {
        var entry = new FileSystemEntry { IsDrive = true };

        Assert.AreEqual(string.Empty, entry.SizeLabel);
    }

    [TestMethod]
    public void SizeLabel_Drive_ShowsUsedPercent()
    {
        var entry = new FileSystemEntry { IsDrive = true };
        entry.DriveTotalBytes = 1024L * 1024 * 1024 * 100; // 100 GB
        entry.DriveUsedBytes  = 1024L * 1024 * 1024 * 50;  // 50 GB

        StringAssert.Contains(entry.SizeLabel, "50%");
        StringAssert.Contains(entry.SizeLabel, "used");
    }

    // ── ModifiedLabel ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ModifiedLabel_Drive_Empty()
    {
        var entry = new FileSystemEntry { IsDrive = true, Modified = DateTime.Now };

        Assert.AreEqual(string.Empty, entry.ModifiedLabel);
    }

    [TestMethod]
    public void ModifiedLabel_MinValue_Empty()
    {
        var entry = new FileSystemEntry { Modified = DateTime.MinValue };

        Assert.AreEqual(string.Empty, entry.ModifiedLabel);
    }

    [TestMethod]
    public void ModifiedLabel_ValidDate_Formatted()
    {
        var entry = new FileSystemEntry { Modified = new DateTime(2024, 6, 15, 14, 30, 0) };

        Assert.AreEqual("2024-06-15 14:30", entry.ModifiedLabel);
    }

    // ── PropertyChanged ───────────────────────────────────────────────────────

    [TestMethod]
    public void DriveUsedBytes_Set_FiresSizeLabelChange()
    {
        var entry  = new FileSystemEntry { IsDrive = true };
        entry.DriveTotalBytes = 1024L * 1024 * 1024;
        var fired  = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        entry.DriveUsedBytes = 512L * 1024 * 1024;

        CollectionAssert.Contains(fired, nameof(FileSystemEntry.SizeLabel));
    }

    [TestMethod]
    public void DriveKindLabel_Set_FiresTypeLabelChange()
    {
        var entry = new FileSystemEntry { IsDrive = true };
        var fired = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        entry.DriveKindLabel = "Network Drive";

        CollectionAssert.Contains(fired, nameof(FileSystemEntry.TypeLabel));
    }
}
