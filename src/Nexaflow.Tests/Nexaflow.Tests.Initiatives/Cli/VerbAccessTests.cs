using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// What each command waits for in the resident process, as its verb declares it. Wrong one way, a command that changes
/// a tree runs beside another that does; wrong the other, questions queue behind each other again. An alias is the verb
/// it stands for, and a verb nobody classified writes.
/// </summary>
[TestClass]
[CoversNode("initiatives-daemon")]
public class VerbAccessTests
{
    [TestMethod]
    public void Questions_Read()
    {
        foreach (var args in new string[][]
                 {
                     ["ask", "search X | ids"], ["find", "x"], ["describe", "x"], ["tree"], ["graph", "search", "x"],
                     ["graph", "grep", "x"], ["graph", "code", "file:a.cs"], ["test", "code:a.cs#T:A", "--list"],
                 })
            Assert.AreEqual(VerbAccess.Reads, Program.AccessOf(args), string.Join(' ', args));
    }

    [TestMethod]
    public void AnAlias_WaitsForWhatItsVerbWaitsFor()
    {
        Assert.AreEqual(VerbAccess.Reads, Program.AccessOf(["graph", "cat", "file:a.cs"]));
        Assert.AreEqual(VerbAccess.Reads, Program.AccessOf(["graph", "ctx", "code:a.cs#T:A"]));
        Assert.AreEqual(VerbAccess.Reads, Program.AccessOf(["graph", "path", "a", "b"]));
    }

    [TestMethod]
    public void EditsAndBuilds_WriteTheirTree()
    {
        foreach (var args in new string[][]
                 {
                     ["graph", "edit", "replace", "code:a.cs#T:A"], ["graph", "edit", "script", "--stdin"],
                     ["graph", "edit", "undo"], ["graph", "build"], ["graph"],
                 })
            Assert.AreEqual(VerbAccess.Writes, Program.AccessOf(args), string.Join(' ', args));
    }

    [TestMethod]
    public void ProductTreeEdits_WriteTheProduct()
    {
        foreach (var args in new string[][]
                 {
                     ["set-status", "x", "done"], ["add-snaplink", "x", "--type", "code"], ["batch", "s.txt"],
                     ["promote"], ["remap", "a", "b"], ["validate", "--save"],
                 })
            Assert.AreEqual(VerbAccess.WritesProduct, Program.AccessOf(args), string.Join(' ', args));
    }

    [TestMethod]
    public void AVerbNobodyClassified_Writes()
    {
        Assert.AreEqual(VerbAccess.Writes, Program.AccessOf(["something-new", "x"]));
    }
}
