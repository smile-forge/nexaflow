using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Syntax;

/// <summary>
/// A declaration as it reads from outside — what a listing prints beside each id so the body need not be read. The
/// shapes are the ones that make "up to the body" mean different things: an attribute above, parameters over two lines,
/// an arrow body, accessors, an initialiser that is a table.
/// </summary>
[TestClass]
[CoversNode("graph-ask-signatures")]
public class DeclarationSignaturesTests
{
    private const string Widget = """
        namespace N;

        public sealed class Widget : IDisposable
        {
            private const int Limit = 40;

            [Obsolete("old")]
            public int Add(int a,
                           int b)
            {
                return a + b;
            }

            public int Twice(int n) => n * 2;

            public int Count { get; set; }

            private static readonly int[] Table =
            [
                1, 2, 3,
            ];

            public void Dispose() { }
        }
        """;

    private static DeclarationSignature Named(string name) =>
        DeclarationSignatures.Of("c-sharp", Widget).First(s => s.Name == name);

    [TestMethod]
    public void AMethod_IsItsFirstWordToItsBody_PastItsAttributes_OnOneLine() =>
        Assert.AreEqual("public int Add(int a, int b)", Named("Add").Text);

    [TestMethod]
    public void AnArrowBody_IsLeftOut() => Assert.AreEqual("public int Twice(int n)", Named("Twice").Text);

    [TestMethod]
    public void ADelegate_IsItsWholeDeclaration() =>
        Assert.AreEqual("public delegate int Measure(string s, int n)",
                        DeclarationSignatures.Of("c-sharp", "class Host\n{\n    public delegate int Measure(string s,\n                                int n);\n}\n")
                                             .First(s => s.Name == "Measure").Text.TrimEnd(';'));

    [TestMethod]
    public void AProperty_StopsAtItsAccessors() => Assert.AreEqual("public int Count", Named("Count").Text);

    [TestMethod]
    public void AType_IsItsHeader() => Assert.AreEqual("public sealed class Widget : IDisposable", Named("Widget").Text);

    [TestMethod]
    public void AShortInitialiser_IsKept_AndATableOfOne_IsCutAtItsFirstLine()
    {
        Assert.AreEqual("private const int Limit = 40", Named("Limit").Text);
        Assert.AreEqual("private static readonly int[] Table = …", Named("Table").Text);
    }

    [TestMethod]
    public void ADeclaration_IsFound_OnTheLineItStarts_OrTheLineItsNameIsOn()
    {
        var all = DeclarationSignatures.Of("c-sharp", Widget);

        Assert.AreEqual("Add", DeclarationSignatures.Find(all, "Add", 7)?.Name, "where its attribute is");
        Assert.AreEqual("Add", DeclarationSignatures.Find(all, "Add", 8)?.Name, "where its name is");
        Assert.IsNull(DeclarationSignatures.Find(all, "Add", 20), "not anywhere a declaration of that name is not");
    }

    [TestMethod]
    public void AnElement_IsItsStartTag_WithThePathThatAddressesIt_AndTheDeepestOneHoldsALine()
    {
        const string view = "<UserControl x:Class=\"App.View\">\n  <Grid>\n    <Button Content=\"a\"/>\n    <Button\n      Content=\"b\"/>\n  </Grid>\n</UserControl>\n";
        var all = DeclarationSignatures.Of("xaml", view);

        var second = DeclarationSignatures.Innermost(all, 5)!;
        Assert.AreEqual("/UserControl/Grid/Button[2]", second.XmlPath);
        Assert.AreEqual("<Button Content=\"b\"/>", second.Text);
        Assert.AreEqual("/UserControl/Grid", DeclarationSignatures.Innermost(all, 6)!.XmlPath);
    }
}
