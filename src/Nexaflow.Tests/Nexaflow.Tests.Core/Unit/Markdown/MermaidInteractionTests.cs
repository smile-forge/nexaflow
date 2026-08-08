using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

namespace Nexaflow.Tests.Core.Unit.Markdown;

/// <summary>
/// Clickable diagram objects. Interaction is a property of the graph model rather than a trick one
/// renderer supports, so the <c>click</c> directive is parsed once, centrally, and lands on the node
/// — where every diagram type that shares the layout can use it.
/// <para>
/// Parser-level and therefore WPF-free; the gesture itself lives in <c>DiagramInteraction</c>.
/// </para>
/// </summary>
[TestClass]
[CoversNode("mermaid")]
public class MermaidInteractionTests
{
    // ── The directive parser ──────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void The_quoted_form_is_parsed()
    {
        Assert.IsTrue(MermaidDirectives.TryParseClick(
            """click nodeA "https://example.com" """, out var id, out var href, out var tip));

        Assert.AreEqual("nodeA", id);
        Assert.AreEqual("https://example.com", href);
        Assert.IsNull(tip);
    }

    [TestMethod, TestCategory("Unit")]
    public void The_href_form_and_its_tooltip_are_parsed()
    {
        Assert.IsTrue(MermaidDirectives.TryParseClick(
            """click nodeB href "C:\lib\thing.dll" "Open this dependency" """,
            out var id, out var href, out var tip));

        Assert.AreEqual("nodeB", id);
        Assert.AreEqual(@"C:\lib\thing.dll", href);
        Assert.AreEqual("Open this dependency", tip);
    }

    [TestMethod, TestCategory("Unit")]
    public void An_unquoted_target_is_accepted()
    {
        Assert.IsTrue(MermaidDirectives.TryParseClick(
            "click n1 https://example.com/x", out _, out var href, out _));
        Assert.AreEqual("https://example.com/x", href);
    }

    [TestMethod, TestCategory("Unit")]
    public void A_callback_binding_is_rejected_rather_than_treated_as_a_link()
    {
        // There is no script host here, so `call` names a function that can never run. Turning it
        // into a link to the text "doSomething()" would be worse than ignoring the line.
        Assert.IsFalse(MermaidDirectives.TryParseClick(
            "click nodeA call doSomething()", out _, out _, out _));
    }

    [TestMethod, TestCategory("Unit")]
    public void Non_click_lines_and_malformed_ones_are_rejected()
    {
        Assert.IsFalse(MermaidDirectives.TryParseClick("style nodeA fill:#f00", out _, out _, out _));
        Assert.IsFalse(MermaidDirectives.TryParseClick("click", out _, out _, out _));
        Assert.IsFalse(MermaidDirectives.TryParseClick("click nodeA", out _, out _, out _));
        Assert.IsFalse(MermaidDirectives.TryParseClick("", out _, out _, out _));
    }

    [TestMethod, TestCategory("Unit")]
    public void Leading_whitespace_is_tolerated()
    {
        Assert.IsTrue(MermaidDirectives.TryParseClick(
            """      click  spaced   "target" """, out var id, out var href, out _));
        Assert.AreEqual("spaced", id);
        Assert.AreEqual("target", href);
    }

    // ── The flowchart parser ──────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_flowchart_node_picks_up_its_click_target()
    {
        var graph = new MermaidParser().Parse("""
            graph LR
              app["app.exe"] --> lib["lib.dll"]
              click app href "C:\app\app.exe" "Inspect app.exe"
              click lib "C:\app\lib.dll"
            """);

        var app = graph.Nodes.Single(n => n.Id == "app");
        Assert.AreEqual(@"C:\app\app.exe", app.Href);
        Assert.AreEqual("Inspect app.exe", app.Tooltip);

        var lib = graph.Nodes.Single(n => n.Id == "lib");
        Assert.AreEqual(@"C:\app\lib.dll", lib.Href);
        Assert.IsNull(lib.Tooltip);
    }

    [TestMethod, TestCategory("Unit")]
    public void A_click_naming_an_unknown_node_is_ignored_without_disturbing_the_graph()
    {
        var graph = new MermaidParser().Parse("""
            graph TD
              a --> b
              click ghost "nowhere"
            """);

        Assert.AreEqual(2, graph.Nodes.Count, "The stray directive must not invent a node.");
        Assert.IsTrue(graph.Nodes.All(n => n.Href is null));
    }

    [TestMethod, TestCategory("Unit")]
    public void A_flowchart_without_click_directives_has_no_links()
    {
        var graph = new MermaidParser().Parse("""
            graph LR
              a["A"] --> b["B"]
            """);

        Assert.AreEqual(2, graph.Nodes.Count);
        Assert.IsTrue(graph.Nodes.All(n => n.Href is null && n.Tooltip is null));
    }

    [TestMethod, TestCategory("Unit")]
    public void Other_directives_are_still_skipped()
    {
        // click parsing replaced one arm of the skip list; the rest must keep being ignored.
        var graph = new MermaidParser().Parse("""
            graph LR
              a --> b
              style a fill:#f9f
              linkStyle 0 stroke:#333
              direction RL
            """);

        Assert.AreEqual(2, graph.Nodes.Count);
        Assert.AreEqual(1, graph.Edges.Count);
    }
}
