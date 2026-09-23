using System;
using System.IO;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Verifies that <see cref="FileOperationErrors.Describe"/> turns the common Win32/CLR copy/move
/// faults into a specific, user-facing sentence instead of a generic "something went wrong".
/// </summary>
[TestClass]
[CoversNode("winfs-act-paste")]
public class FileOperationErrorsTests
{
    private const string Source = @"C:\work\report.docx";
    private const string Dest   = @"D:\backup";

    private static IOException Win32(int code) =>
        new("native fault") { HResult = unchecked((int)(0x80070000 | (uint)code)) };

    private static string Describe(string verb, Exception ex) =>
        FileOperationErrors.Describe(verb, Source, Dest, ex);

    /// <summary>The sentence <paramref name="key"/> makes of this fault: every one is handed the verb, the item's
    /// name, the quoted destination folder and the CLR message, and uses the ones its grammar needs.</summary>
    private static string Sentence(string key, string verb, Exception ex) =>
        Str.Format(key, verb, "report.docx", "\"backup\"", ex.Message);

    [TestMethod]
    public void SharingViolation_SaysFileIsOpenElsewhere()
    {
        var ex = Win32(32 /*ERROR_SHARING_VIOLATION*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.SharingViolationFormat", "move", ex), Describe("move", ex));
    }

    [TestMethod]
    public void LockViolation_SaysLocked()
    {
        var ex = Win32(33 /*ERROR_LOCK_VIOLATION*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.LockViolationFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void AccessDenied_FromUnauthorizedAccess_MentionsPermission()
    {
        var ex = new UnauthorizedAccessException();
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.AccessDeniedFormat", "move", ex), Describe("move", ex));
    }

    [TestMethod]
    public void AccessDenied_FromWin32Code_MentionsPermission()
    {
        var ex = Win32(5 /*ERROR_ACCESS_DENIED*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.AccessDeniedFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void DiskFull_SaysNotEnoughSpace()
    {
        var ex = Win32(112 /*ERROR_DISK_FULL*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.DiskFullFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void HandleDiskFull_SaysNotEnoughSpace()
    {
        var ex = Win32(39 /*ERROR_HANDLE_DISK_FULL*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.DiskFullFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void AlreadyExists_SaysAlreadyExists()
    {
        var ex = Win32(183 /*ERROR_ALREADY_EXISTS*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.AlreadyExistsFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void FileExists_SaysAlreadyExists()
    {
        var ex = Win32(80 /*ERROR_FILE_EXISTS*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.AlreadyExistsFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void NotSameDevice_SaysDifferentDrive()
    {
        var ex = Win32(17 /*ERROR_NOT_SAME_DEVICE*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.NotSameDeviceFormat", "move", ex), Describe("move", ex));
    }

    [TestMethod]
    public void WriteProtected_SaysWriteProtected()
    {
        var ex = Win32(19 /*ERROR_WRITE_PROTECT*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.WriteProtectedFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void PathTooLong_FromTypeRegardlessOfHResult_SaysTooLong()
    {
        var ex = new PathTooLongException();
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.PathTooLongFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void FilenameExcedRange_FromWin32Code_SaysTooLong()
    {
        var ex = Win32(206 /*ERROR_FILENAME_EXCED_RANGE*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.PathTooLongFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void FileNotFound_SaysNoLongerExists()
    {
        var ex = new FileNotFoundException();
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.NotFoundFormat", "move", ex), Describe("move", ex));
    }

    [TestMethod]
    public void DirectoryNotFound_SaysNoLongerExists()
    {
        var ex = new DirectoryNotFoundException();
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.NotFoundFormat", "move", ex), Describe("move", ex));
    }

    [TestMethod]
    public void InvalidName_SaysNameRejected()
    {
        var ex = Win32(123 /*ERROR_INVALID_NAME*/);
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.InvalidNameFormat", "copy", ex), Describe("copy", ex));
    }

    [TestMethod]
    public void UnknownFault_FallsBackToClrMessage()
    {
        var ex = new InvalidOperationException("weird native thing");
        Assert.AreEqual(Sentence("WindowsFileSystem.Errors.UnknownFormat", "copy", ex), Describe("copy", ex));
    }
}
