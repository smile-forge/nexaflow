using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Syntax;

/// <summary>
/// Which declarations an edit changed from outside — the ones whose uses elsewhere it can have broken. A body is not
/// the outside of anything, and treating a body edit as a contract change would send every edit off to search the
/// repository for the names in it.
/// </summary>
[TestClass]
[CoversNode("graph-edit-impact")]
public class ChangedDeclarationsTests
{
    private const string Before =
        "namespace N;\n\npublic class Greeter\n{\n    public string Greet(string name)\n    {\n        return \"hi \" + name;\n    }\n\n"
      + "    public int Count => 1;\n}\n";

    private static IReadOnlyList<string> Changed(string after) =>
        [.. StructuralEdit.ChangedDeclarations("c-sharp", Before, after).Select(d => d.Name)];

    [TestMethod]
    public void ABodyEdit_ChangesNothingFromOutside() =>
        Assert.AreEqual(0, Changed(Before.Replace("\"hi \"", "\"hello \"")).Count);

    [TestMethod]
    public void AParameterAdded_IsAContractChange() =>
        CollectionAssert.AreEqual(new[] { "Greet" }, Changed(Before.Replace("(string name)", "(string name, int times)")).ToArray());

    [TestMethod]
    public void ARename_IsTheOldNameGone() =>
        CollectionAssert.AreEqual(new[] { "Greet" }, Changed(Before.Replace("Greet(", "Welcome(")).ToArray());

    [TestMethod]
    public void AnExpressionBodiedMember_IsJudgedWhole() =>
        CollectionAssert.AreEqual(new[] { "Count" }, Changed(Before.Replace("public int Count", "public long Count")).ToArray());

    [TestMethod]
    public void TheNameIsWhereTheCompilerIsAskedAbout()
    {
        var start = StructuralEdit.NameStartOf("c-sharp", Before, "T:Greeter/M:Greet", "Greet");

        Assert.AreEqual(Before.IndexOf("Greet(", StringComparison.Ordinal), start);
    }
}
