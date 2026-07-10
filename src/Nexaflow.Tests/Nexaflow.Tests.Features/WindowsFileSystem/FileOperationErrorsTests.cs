using System;
using System.IO;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Tests.Fixtures;

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

    [TestMethod]
    public void SharingViolation_SaysFileIsOpenElsewhere()
    {
        var msg = Describe("move", Win32(32 /*ERROR_SHARING_VIOLATION*/));
        StringAssert.Contains(msg, "report.docx");
        StringAssert.Contains(msg, "open in another program");
    }

    [TestMethod]
    public void LockViolation_SaysLocked()
        => StringAssert.Contains(Describe("copy", Win32(33 /*ERROR_LOCK_VIOLATION*/)), "locked");

    [TestMethod]
    public void AccessDenied_FromUnauthorizedAccess_MentionsPermission()
    {
        var msg = Describe("move", new UnauthorizedAccessException());
        StringAssert.Contains(msg, "access denied");
        StringAssert.Contains(msg, "permission");
    }

    [TestMethod]
    public void AccessDenied_FromWin32Code_MentionsPermission()
        => StringAssert.Contains(Describe("copy", Win32(5 /*ERROR_ACCESS_DENIED*/)), "access denied");

    [TestMethod]
    public void DiskFull_SaysNotEnoughSpace()
    {
        var msg = Describe("copy", Win32(112 /*ERROR_DISK_FULL*/));
        StringAssert.Contains(msg, "enough free space");
        StringAssert.Contains(msg, "backup");   // names the destination folder
    }

    [TestMethod]
    public void HandleDiskFull_SaysNotEnoughSpace()
        => StringAssert.Contains(Describe("copy", Win32(39 /*ERROR_HANDLE_DISK_FULL*/)), "enough free space");

    [TestMethod]
    public void AlreadyExists_SaysAlreadyExists()
        => StringAssert.Contains(Describe("copy", Win32(183 /*ERROR_ALREADY_EXISTS*/)), "already exists");

    [TestMethod]
    public void FileExists_SaysAlreadyExists()
        => StringAssert.Contains(Describe("copy", Win32(80 /*ERROR_FILE_EXISTS*/)), "already exists");

    [TestMethod]
    public void NotSameDevice_SaysDifferentDrive()
        => StringAssert.Contains(Describe("move", Win32(17 /*ERROR_NOT_SAME_DEVICE*/)), "different drive");

    [TestMethod]
    public void WriteProtected_SaysWriteProtected()
        => StringAssert.Contains(Describe("copy", Win32(19 /*ERROR_WRITE_PROTECT*/)), "write-protected");

    [TestMethod]
    public void PathTooLong_FromTypeRegardlessOfHResult_SaysTooLong()
        => StringAssert.Contains(Describe("copy", new PathTooLongException()), "too long");

    [TestMethod]
    public void FilenameExcedRange_FromWin32Code_SaysTooLong()
        => StringAssert.Contains(Describe("copy", Win32(206 /*ERROR_FILENAME_EXCED_RANGE*/)), "too long");

    [TestMethod]
    public void FileNotFound_SaysNoLongerExists()
        => StringAssert.Contains(Describe("move", new FileNotFoundException()), "no longer exists");

    [TestMethod]
    public void DirectoryNotFound_SaysNoLongerExists()
        => StringAssert.Contains(Describe("move", new DirectoryNotFoundException()), "no longer exists");

    [TestMethod]
    public void InvalidName_SaysNameRejected()
        => StringAssert.Contains(Describe("copy", Win32(123 /*ERROR_INVALID_NAME*/)), "name");

    [TestMethod]
    public void UnknownFault_FallsBackToClrMessage()
    {
        var msg = Describe("copy", new InvalidOperationException("weird native thing"));
        StringAssert.Contains(msg, "report.docx");
        StringAssert.Contains(msg, "weird native thing");
    }
}
