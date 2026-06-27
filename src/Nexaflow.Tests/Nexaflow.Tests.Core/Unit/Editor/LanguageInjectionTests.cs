using System.Linq;
using Nexaflow.Syntax;

namespace Nexaflow.Tests.Core.Unit.Editor;

/// <summary>
/// Embedded-language injection: a host file's <see cref="CodeHighlighter"/> should also colour sub-ranges
/// written in another language (JS/CSS in HTML, Ruby/HTML in ERB, HTML in PHP, HTML in a Ruby heredoc),
/// with offsets that still index the original document. A target language we don't ship (SQL) is a no-op.
/// </summary>
[TestClass]
public class LanguageInjectionTests
{
    private static (string cap, string text)[] Pairs(string grammar, string src)
    {
        using var hl = CodeHighlighter.TryCreate(grammar);
        Assert.IsNotNull(hl, grammar);
        return hl!.Highlight(src)
            .Select(s =>
            {
                // every span must stay in bounds of the original document
                Assert.IsTrue(s.Start >= 0 && s.Length > 0 && s.Start + s.Length <= src.Length,
                    $"span out of range: {s.Start}+{s.Length} in {src.Length}");
                return (s.Capture, src.Substring(s.Start, s.Length));
            })
            .ToArray();
    }

    [TestMethod]
    public void Html_InjectsJavaScript_IntoScript()
    {
        const string src = "<html><body>\n<script>let x = 1;</script>\n</body></html>";
        var pairs = Pairs("html", src);

        CollectionAssert.Contains(pairs, ("tag", "script"));   // outer html still colours the tag
        CollectionAssert.Contains(pairs, ("keyword", "let"));  // injected javascript colours the body
    }

    [TestMethod]
    public void Html_InjectsCss_IntoStyle()
    {
        const string src = "<html><head>\n<style>.a { color: red; }</style>\n</head></html>";
        var pairs = Pairs("html", src);

        CollectionAssert.Contains(pairs, ("tag", "style"));
        CollectionAssert.Contains(pairs, ("attribute", "color"));   // injected css property
    }

    [TestMethod]
    public void Ruby_Heredoc_InjectsHtml_ByDelimiter()
    {
        const string src = "html = <<~HTML\n  <p>hi</p>\nHTML\n";
        var pairs = Pairs("ruby", src);

        CollectionAssert.Contains(pairs, ("tag", "p"));   // <<~HTML body parsed as html
    }

    [TestMethod]
    public void Ruby_Heredoc_Erb_InjectsNestedTemplate()
    {
        // <<~ERB embeds an ERB template, itself ruby-in-html → nested injection ruby → erb → html.
        const string src = "t = <<~ERB\n  <h1><%= title %></h1>\nERB\n";
        using var hl = CodeHighlighter.TryCreate("ruby");

        Assert.IsTrue(hl!.FindInjections(src).Any(r => r.TargetGrammarId == "embedded-template"));
        var pairs = hl.Highlight(src).Select(s => (s.Capture, Text: src.Substring(s.Start, s.Length))).ToArray();
        Assert.IsTrue(pairs.Any(p => p is { Capture: "tag", Text: "h1" }), "html tag reached through ruby→erb→html");
    }

    [TestMethod]
    public void Ruby_Heredoc_UnknownLanguage_IsNoOp()
    {
        // SQL has no grammar ⇒ the body keeps whatever the outer ruby gives it (nothing — ruby doesn't
        // capture heredoc bodies), and certainly no foreign tag/keyword/attribute spans appear in it.
        const string src = "q = <<~SQL\n  SELECT * FROM t\nSQL\n";
        var pairs = Pairs("ruby", src);

        int bodyStart = src.IndexOf("SELECT");
        int bodyEnd = src.IndexOf("\nSQL");
        using var hl = CodeHighlighter.TryCreate("ruby");
        var inBody = hl!.Highlight(src).Where(s => s.Start >= bodyStart && s.Start < bodyEnd).ToList();
        Assert.AreEqual(0, inBody.Count, "no spans should be produced inside an un-injected SQL heredoc body");
    }

    [TestMethod]
    public void Erb_InjectsRubyAndHtml()
    {
        const string src = "<h1><%= @title %></h1>\n<% items.each do |i| %>x<% end %>\n";
        var pairs = Pairs("embedded-template", src);

        Assert.IsTrue(pairs.Any(p => p.cap == "tag" && p.text == "h1"), "html tag from content");
        Assert.IsTrue(pairs.Any(p => p.cap == "variable" && p.text == "@title"), "ruby instance var from code");
        Assert.IsTrue(pairs.Any(p => p.cap == "keyword" && p.text == "do"), "ruby keyword from code");
    }

    [TestMethod]
    public void Php_InjectsHtml_IntoText()
    {
        const string src = "<html>\n<body>\n<?php echo 1; ?>\n</body>\n</html>\n";
        var pairs = Pairs("php", src);

        CollectionAssert.Contains(pairs, ("tag", "html"));   // raw html text injected as html
        CollectionAssert.Contains(pairs, ("tag", "body"));
    }

    [TestMethod]
    public void Php_CombinedHtml_ColorsTrailingCloseTags()
    {
        // The HTML before <?php and after ?> is combined into one parse, so the trailing close-tag-only
        // fragment (</body></html>) is no longer an orphan the html grammar rejects.
        const string src = "<html>\n<body>\n<?php echo 1; ?>\n</body>\n</html>\n";
        using var hl = CodeHighlighter.TryCreate("php");
        var spans = hl!.Highlight(src);

        int afterPhp = src.IndexOf("?>") + 2;
        Assert.IsTrue(spans.Any(s => s.Capture == "tag" && s.Start > afterPhp
                && src.Substring(s.Start, s.Length) == "body"),
            "the trailing </body> close tag (after ?>) is coloured via combined injection");
        Assert.IsTrue(spans.Any(s => s.Capture == "tag" && s.Start > afterPhp
                && src.Substring(s.Start, s.Length) == "html"),
            "the trailing </html> close tag is coloured");
    }

    [TestMethod]
    public void Ruby_Heredoc_SelfLanguage_DoesNotRecurseForever()
    {
        // <<~RUBY would inject ruby into ruby — the self-injection guard must stop it (and not hang).
        const string src = "code = <<~RUBY\n  y = 1\nRUBY\n";
        using var hl = CodeHighlighter.TryCreate("ruby");
        var spans = hl!.Highlight(src);   // completes ⇒ guard works
        Assert.IsTrue(spans.Count > 0);   // the outer `code` identifier is still coloured
    }

    [TestMethod]
    public void FindInjections_ReportsTargetsAndRanges()
    {
        const string src = "<html><script>let x=1;</script><style>.a{color:red}</style></html>";
        using var hl = CodeHighlighter.TryCreate("html");
        var inj = hl!.FindInjections(src);

        Assert.AreEqual(2, inj.Count);
        var js = inj.Single(i => i.TargetGrammarId == "javascript");
        var css = inj.Single(i => i.TargetGrammarId == "css");
        Assert.AreEqual("let x=1;", src.Substring(js.Start, js.Length));
        Assert.AreEqual(".a{color:red}", src.Substring(css.Start, css.Length));
    }

    [TestMethod]
    public void Jinja_InjectsPython_IntoStatements()
    {
        const string src = "<ul>\n{% for item in items %}\n<li>{{ item }}</li>\n{% endfor %}\n</ul>\n";
        var pairs = Pairs("jinja", src);

        CollectionAssert.Contains(pairs, ("tag", "ul"));        // markup still parsed as html
        CollectionAssert.Contains(pairs, ("keyword", "for"));   // {% … %} scanned + injected as python
        CollectionAssert.Contains(pairs, ("keyword", "in"));
    }

    [TestMethod]
    public void JsGql_DetectsGraphqlSite_NoOpUntilGrammarAdded()
    {
        const string src = "const q = gql`{ user { id } }`;\n";
        using var hl = CodeHighlighter.TryCreate("javascript");

        // the site is detected today …
        var gql = hl!.FindInjections(src).Single(r => r.TargetGrammarId == "graphql");
        Assert.AreEqual("{ user { id } }", src.Substring(gql.Start, gql.Length));

        // … but with no graphql grammar shipped it adds no spans and certainly doesn't throw/hang
        Assert.IsTrue(hl.Highlight(src).Count > 0);
    }

    [TestMethod]
    public void Ipynb_InjectsKernelLanguage_IntoCodeCells()
    {
        // A code cell's source array carries python; the markdown cell does not get injected.
        const string src =
            "{\n \"cells\": [\n" +
            "  {\"cell_type\": \"code\", \"source\": [\"import os\\n\", \"x = 1\\n\"]},\n" +
            "  {\"cell_type\": \"markdown\", \"source\": [\"# heading\\n\"]}\n" +
            " ],\n \"metadata\": {\"kernelspec\": {\"language\": \"python\"}}\n}\n";
        var pairs = Pairs("ipynb", src);

        CollectionAssert.Contains(pairs, ("keyword", "import"));   // python injected into the code cell
        CollectionAssert.Contains(pairs, ("number", "1"));
    }

    [TestMethod]
    public void Html_NestedScript_FoldsAndParseTreeIncludeEmbedded()
    {
        const string src = "<html>\n<script>\nfunction f() {\n  return 1;\n}\n</script>\n</html>";
        using var hl = CodeHighlighter.TryCreate("html");

        // the JS function body inside <script> should contribute a fold (offset into the html document)
        var folds = hl!.GetFolds(src);
        Assert.IsTrue(folds.Any(f => src.Substring(f.Start, f.End - f.Start).Contains("return 1")),
            "a fold from the embedded JS function body");

        // the spliced parse tree should mention the injected language + a JS node
        var tree = hl.GetParseTree(src);
        StringAssert.Contains(tree, "injection language: \"javascript\"");
        StringAssert.Contains(tree, "function_declaration");
    }
}
