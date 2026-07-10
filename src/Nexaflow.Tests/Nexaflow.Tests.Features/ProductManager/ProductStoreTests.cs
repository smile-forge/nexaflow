using System.IO;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// Validates the .product/ schema by implementing it: the node-status model, concern-links, snaplinks
/// (markdown title-path + code class/method), round-trip fidelity, and minimal diffs on a status flip.
/// </summary>
[TestClass]
[CoversNode("data-model")]
public class ProductStoreTests
{
    private string _root = string.Empty;
    private ProductStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _root  = Path.Combine(Path.GetTempPath(), $"product_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _store = new ProductStore(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Dictionary<string, ProductNode> Mermaid() => new()
    {
        ["mermaid"] = new ProductNode
        {
            Title = "Mermaid rendering", Status = Status.Should, Parent = null,
            Children = ["flowchart", "pie", "sequence", "state"]
        },
        ["flowchart"] = new ProductNode
        {
            Title = "Flowcharts", Status = Status.Done, Parent = "mermaid",
            Concerns = [new ConcernLink { Tag = "tests", Status = Status.Done }]
        },
        ["pie"] = new ProductNode { Title = "Pie charts", Status = Status.Should, Parent = "mermaid" },
        ["sequence"] = new ProductNode
        {
            Title = "Sequence diagrams", Status = Status.Should, Parent = "mermaid",
            Note = "lifeline + activation clashes with the WpfMath measuring pass"
        },
        ["state"] = new ProductNode
        {
            Title = "State diagrams", Status = Status.Faulted, Parent = "mermaid",
            Note = "Repro: open docs/samples/state-nested.md",
            Concerns = [new ConcernLink { Tag = "tests", Status = Status.Done }],
            Snaplinks =
            [
                new Snaplink { Type = "markdown", Doc = "docs/mermaid.md", TitlePath = ["Mermaid", "State"], Status = Status.Should },
                new Snaplink { Type = "code", Class = "StateDiagram", Method = "Render" }
            ]
        }
    };

    [TestMethod]
    public void Initialize_CreatesSeedFiles()
    {
        _store.Initialize("Nexaflow");
        var dot = Path.Combine(_root, ".product");
        Assert.IsTrue(File.Exists(Path.Combine(dot, "product.json")));
        Assert.IsTrue(File.Exists(Path.Combine(dot, "tree.json")));
        Assert.IsTrue(ProductStore.Exists(_root));
    }

    [TestMethod]
    public void Load_Fresh_HasDefaultConcernVocabAndEmptyTree()
    {
        _store.Initialize("Nexaflow");
        var s = _store.Load();
        Assert.AreEqual("Nexaflow", s.Product.Product);
        Assert.AreEqual(0, s.Nodes.Count);
        Assert.IsTrue(s.Product.Concerns.Any(c => c.Name == "tests"));
        Assert.IsTrue(s.Product.Concerns.Any(c => c.Name == "accessibility"));
    }

    [TestMethod]
    public void SaveTree_ThenLoad_RoundTripsStatusConcernsSnaplinks()
    {
        _store.Initialize("Nexaflow");
        _store.SaveTree(Mermaid());

        var s = _store.Load();
        Assert.AreEqual(5, s.Nodes.Count);

        var state = s.Nodes["state"];
        Assert.AreEqual(Status.Faulted, state.Status);
        StringAssert.Contains(state.Note, "state-nested.md");
        Assert.AreEqual(Status.Done, state.Concerns!.Single(c => c.Tag == "tests").Status);

        var md = state.Snaplinks!.Single(l => l.Type == "markdown");
        Assert.AreEqual("docs/mermaid.md", md.Doc);
        CollectionAssert.AreEqual(new[] { "Mermaid", "State" }, md.TitlePath!);

        var code = state.Snaplinks!.Single(l => l.Type == "code");
        Assert.AreEqual("StateDiagram", code.Class);
        Assert.AreEqual("Render", code.Method);
        Assert.AreEqual("StateDiagram.Render", code.Display);
    }

    [TestMethod]
    public void SaveTree_IsIdempotent_ReloadResaveByteIdentical()
    {
        _store.Initialize("Nexaflow");
        _store.SaveTree(Mermaid());
        var first = File.ReadAllText(_store.TreeFilePath);
        _store.SaveTree(_store.Load().Nodes);
        Assert.AreEqual(first, File.ReadAllText(_store.TreeFilePath));
    }

    [TestMethod]
    public void RoundTrip_ChangingOneStatus_ProducesMinimalDiff()
    {
        _store.Initialize("Nexaflow");
        _store.SaveTree(Mermaid());
        var before = File.ReadAllText(_store.TreeFilePath);

        var tree = _store.Load().Nodes;
        tree["pie"].Status = Status.Done;       // should → done, no new fields
        _store.SaveTree(tree);

        Assert.IsTrue(ChangedLineCount(before, File.ReadAllText(_store.TreeFilePath)) <= 2);
    }

    [TestMethod]
    public void Save_OmitsNullOptionalFields()
    {
        _store.Initialize("Nexaflow");
        _store.SaveTree(new Dictionary<string, ProductNode> { ["bare"] = new ProductNode { Title = "Bare", Status = Status.Should } });

        var text = File.ReadAllText(_store.TreeFilePath);
        StringAssert.Contains(text, "\"children\"");
        StringAssert.Contains(text, "\"status\"");
        Assert.IsFalse(text.Contains("\"description\""));
        Assert.IsFalse(text.Contains("\"note\""));
        Assert.IsFalse(text.Contains("\"concerns\""));
        Assert.IsFalse(text.Contains("\"snaplinks\""));
        Assert.IsFalse(text.Contains("\"parent\""));
    }

    [TestMethod]
    public void Status_SerializesSnakeTokens_IncludingShouldnt()
    {
        _store.Initialize("Nexaflow");
        _store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["a"] = new ProductNode { Title = "A", Status = Status.Shouldnt },
            ["b"] = new ProductNode { Title = "B", Status = Status.Faulted }
        });
        var text = File.ReadAllText(_store.TreeFilePath);
        StringAssert.Contains(text, "\"shouldnt\"");
        StringAssert.Contains(text, "\"faulted\"");

        var loaded = _store.Load().Nodes;
        Assert.AreEqual(Status.Shouldnt, loaded["a"].Status);
        Assert.AreEqual(Status.Faulted, loaded["b"].Status);
    }

    [TestMethod]
    public void Snaplink_MarkdownTitlePath_UsesSnakeCaseKey()
    {
        _store.Initialize("Nexaflow");
        _store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["n"] = new ProductNode { Title = "N", Status = Status.Should,
                Snaplinks = [new Snaplink { Type = "markdown", Doc = "d.md", TitlePath = ["A"] }] }
        });
        StringAssert.Contains(File.ReadAllText(_store.TreeFilePath), "\"title_path\"");
    }

    [TestMethod]
    public void Snapshot_WriteListLoadDelete_RoundTrips()
    {
        _store.Initialize("Nexaflow");
        var snap = new ProductSnapshot
        {
            Version = "v0.4.0", Date = "2026-06-01", Tag = "v0.4.0",
            Product = new ProductDocument { Product = "Nexaflow" },
            Nodes = new Dictionary<string, ProductNode> { ["n"] = new ProductNode { Title = "N", Status = Status.Done } }
        };
        var path = _store.WriteSnapshot("docs/product", snap);
        Assert.IsTrue(File.Exists(path));

        var listed = _store.ListSnapshots("docs/product");
        Assert.AreEqual(1, listed.Count);
        Assert.AreEqual("v0.4.0", listed[0].Version);

        var loaded = _store.LoadSnapshot("docs/product", "v0.4.0");
        Assert.IsNotNull(loaded);
        Assert.AreEqual(Status.Done, loaded!.Nodes["n"].Status);
        Assert.AreEqual("v0.4.0", loaded.Tag);

        _store.DeleteSnapshot("docs/product", "v0.4.0");
        Assert.AreEqual(0, _store.ListSnapshots("docs/product").Count);
    }

    [TestMethod]
    public void Initialize_GitignoresDotProduct()
    {
        _store.Initialize("Nexaflow");
        var gitignore = Path.Combine(_root, ".gitignore");
        Assert.IsTrue(File.Exists(gitignore));
        StringAssert.Contains(File.ReadAllText(gitignore), ".product/");

        // idempotent — a second init doesn't add a duplicate entry
        _store.EnsureGitignored();
        Assert.AreEqual(1, File.ReadAllLines(gitignore).Count(l => l.Trim().TrimEnd('/') == ".product"));
    }

    [TestMethod]
    public void ConcernDef_And_ConcernSnaplinks_RoundTrip()
    {
        _store.Initialize("Nexaflow");
        _store.SaveProduct(new ProductDocument
        {
            Product = "Nexaflow",
            Concerns = [new() { Name = "tests", IsDefault = true }, new() { Name = "a11y" }]
        });
        _store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["n"] = new ProductNode
            {
                Title = "N", Status = Status.Should,
                Concerns = [new ConcernLink { Tag = "tests", Status = Status.Done,
                    Snaplinks = [new Snaplink { Type = "code", Class = "Foo", Method = "Bar" }] }]
            }
        });

        var s = _store.Load();
        Assert.IsTrue(s.Product.Concerns.Single(c => c.Name == "tests").IsDefault);
        Assert.IsFalse(s.Product.Concerns.Single(c => c.Name == "a11y").IsDefault);
        var link = s.Nodes["n"].Concerns!.Single();
        Assert.AreEqual(Status.Done, link.Status);
        Assert.AreEqual("Foo.Bar", link.Snaplinks!.Single().Display);
    }

    [TestMethod]
    public void Load_MissingTreeFile_DoesNotThrow()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".product"));
        File.WriteAllText(Path.Combine(_root, ".product", "product.json"), "{\"product\":\"X\"}");
        var s = _store.Load();
        Assert.AreEqual("X", s.Product.Product);
        Assert.AreEqual(0, s.Nodes.Count);
    }

    private static int ChangedLineCount(string before, string after)
    {
        var a = before.Replace("\r\n", "\n").Split('\n');
        var b = after.Replace("\r\n", "\n").Split('\n');
        var changed = Math.Abs(a.Length - b.Length);
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) changed++;
        return changed;
    }
}
