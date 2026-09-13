using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Where a name is spelt — the candidate set the compiler then narrows. A whole word only, credited to the declaration
/// holding the line, and read from the files it is handed rather than the graph's list, which lags a file just created.
/// </summary>
[TestClass]
[CoversNode("graph-edit-impact")]
public class GraphMentionsTests
{
    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["src/A.cs"]      = "class A\n{\n    void Run() => Plan();\n    void Planner() { }\n}\n",
        ["src/View.xaml"] = "<StackPanel>\n  <TextBlock Text=\"Plan\" />\n  <Button Click=\"Plan\" Content=\"Plan\" />\n</StackPanel>\n",
        ["src/B.cs"]      = "class B { }\n",
        ["notes.md"]      = "Plan the Plan.\n",
    };

    private static readonly KnowledgeGraph Graph = new()
    {
        Nodes =
        [
            new GraphNode { Id = "code:src/A.cs#T:A", Type = NodeType.Type, Label = "A", FilePath = "src/A.cs",
                            Metadata = new Dictionary<string, string> { ["line"] = "1", ["endLine"] = "5" } },
            new GraphNode { Id = "code:src/A.cs#T:A/M:Run", Type = NodeType.Member, Label = "Run", FilePath = "src/A.cs",
                            Metadata = new Dictionary<string, string> { ["line"] = "3", ["endLine"] = "3" } },
        ],
    };

    [TestMethod]
    public void AWholeWord_InCodeAndViews_CreditedToTheTightestDeclaration()
    {
        var found = GraphMentions.Of(Graph, ["Plan"], Files.Keys, rel => Files.GetValueOrDefault(rel));

        CollectionAssert.AreEqual(new[] { "src/A.cs:3", "src/View.xaml:3" },
                                  found.Select(m => $"{m.RelativePath}:{m.Line}").ToArray(),
                                  "Planner is another word, a caption is not a use, and a markdown file is not code");
        Assert.AreEqual("code:src/A.cs#T:A/M:Run", found[0].Owner?.Id);
        Assert.IsNull(found[1].Owner, "the graph records nothing inside a view's line, and nothing is guessed");
    }
}
