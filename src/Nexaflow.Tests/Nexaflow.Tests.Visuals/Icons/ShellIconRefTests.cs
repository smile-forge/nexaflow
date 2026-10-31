using Nexaflow.Icons;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Icons;

/// <summary>
/// A picture in a file's resources is named the way the registry's <c>DefaultIcon</c> names one, so every form such a
/// value takes reads as the icon it means, one that names no icon reads as none, and a saved reference reads back as
/// itself.
/// </summary>
[TestClass]
[CoversNode("icons-resource-icon")]
public class ShellIconRefTests
{
    [TestMethod]
    public void AFileAndIndex_ReadAsThatIcon() =>
        Assert.AreEqual(new ShellIconRef(@"%SystemRoot%\System32\shell32.dll", 3), ShellIconRef.Parse(@"%SystemRoot%\System32\shell32.dll,3"));

    [TestMethod]
    public void ANegativeNumber_IsKeptAsTheResourceIdItIs() =>
        Assert.AreEqual(new ShellIconRef(@"C:\Windows\System32\imageres.dll", -102), ShellIconRef.Parse(@"C:\Windows\System32\imageres.dll,-102"));

    [TestMethod]
    public void AQuotedFileAlone_IsItsFirstIcon() =>
        Assert.AreEqual(new ShellIconRef(@"C:\Apps\app.exe", 0), ShellIconRef.Parse("\"C:\\Apps\\app.exe\""));

    [TestMethod]
    public void AQuotedPathHoldingAComma_KeepsItBeforeTheIndex() =>
        Assert.AreEqual(new ShellIconRef(@"C:\Odd, Folder\app.exe", 2), ShellIconRef.Parse("\"C:\\Odd, Folder\\app.exe\",2"));

    [TestMethod]
    public void APathHoldingACommaAndNoIndex_IsAllPath() =>
        Assert.AreEqual(new ShellIconRef(@"C:\Odd, Folder\app.exe", 0), ShellIconRef.Parse(@"C:\Odd, Folder\app.exe"));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("%1")]
    [DataRow("\"%1\"")]
    [DataRow(",3")]
    public void ASpecNamingNoIcon_ReadsAsNone(string? spec) => Assert.IsNull(ShellIconRef.Parse(spec));

    [TestMethod]
    public void AReference_ReadsBackAsItself()
    {
        var icon = new ShellIconRef(@"%ProgramFiles%\Tool\tool.exe", -7);
        Assert.AreEqual(icon, ShellIconRef.Parse(icon.ToString()));
    }
}
