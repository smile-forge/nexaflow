using System;
using Nexaflow.Features.WindowsFileSystem.Services;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>Command-template → executable parsing used when importing Windows "Open with" handlers
/// into External Apps. Pure string logic (no registry), so it runs anywhere.</summary>
[TestClass]
public class RegistryHandlerImportTests
{
    [TestMethod]
    public void QuotedPath_ReturnsExe()
        => Assert.AreEqual(@"C:\Apps\foo.exe",
            RegistryHandlerImport.ExtractExecutable("\"C:\\Apps\\foo.exe\" \"%1\""));

    [TestMethod]
    public void UnquotedPath_StopsAtFirstSpace()
        => Assert.AreEqual(@"C:\Windows\notepad.exe",
            RegistryHandlerImport.ExtractExecutable(@"C:\Windows\notepad.exe %1"));

    [TestMethod]
    public void ExpandsEnvironmentVariables()
    {
        var exe = RegistryHandlerImport.ExtractExecutable("\"%SystemRoot%\\notepad.exe\" \"%1\"");
        Assert.IsNotNull(exe);
        Assert.IsFalse(exe!.Contains('%'), "environment variable should be expanded");
        Assert.IsTrue(exe.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Rundll32Shim_ReturnsNull()
        => Assert.IsNull(RegistryHandlerImport.ExtractExecutable(
            @"rundll32.exe C:\Windows\System32\shell32.dll,OpenAs_RunDLL %1"));

    [TestMethod]
    public void NonExeHandler_ReturnsNull()
        => Assert.IsNull(RegistryHandlerImport.ExtractExecutable("\"C:\\Apps\\run.bat\" \"%1\""));

    [TestMethod]
    public void UnterminatedQuote_ReturnsNull()
        => Assert.IsNull(RegistryHandlerImport.ExtractExecutable("\"C:\\Apps\\foo.exe"));

    [TestMethod]
    public void Whitespace_ReturnsNull()
        => Assert.IsNull(RegistryHandlerImport.ExtractExecutable("   "));
}
