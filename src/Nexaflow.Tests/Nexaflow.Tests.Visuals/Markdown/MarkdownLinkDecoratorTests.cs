using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The host's say in how a link looks (<see cref="MarkdownRenderContext.DecorateLink"/>). It is offered every rendered
/// link with the URL as written, once the link's text is in place — which is what lets the help pane mark a
/// <c>locate:</c> link, whose text it must not disturb. A hook that throws costs that link its decoration and nothing
/// else. UI category: FlowDocuments are built on an STA thread; no window opens.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("md-link-decorator")]
public class MarkdownLinkDecoratorTests
{
    private const string Doc =
        "[the Help button](locate:Chrome_HelpButton), [the web](https://example.com/x) and <https://example.com/auto>\n";

    [TestMethod]
    public void EveryLinkIsOffered_WithTheUrlAsWritten_AndItsTextAlreadyIn() => UiThread.Run(() =>
    {
        var offered = new List<(string Url, string Text)>();

        Build((link, url) => offered.Add((url, new TextRange(link.ContentStart, link.ContentEnd).Text)));

        CollectionAssert.AreEqual(new[] { "locate:Chrome_HelpButton", "https://example.com/x", "https://example.com/auto" },
                                  offered.Select(o => o.Url).ToArray());
        CollectionAssert.AreEqual(new[] { "the Help button", "the web", "https://example.com/auto" },
                                  offered.Select(o => o.Text).ToArray(),
                                  "offered after the text, so a decoration can sit beside it");
    });

    [TestMethod]
    public void WhatTheHookDoesToALink_IsWhatRenders() => UiThread.Run(() =>
    {
        var doc = Build((link, url) =>
        {
            if (url.StartsWith("locate:", StringComparison.Ordinal))
                link.Inlines.InsertBefore(link.Inlines.FirstInline, new Run("* "));
        });

        CollectionAssert.AreEqual(new[] { "* the Help button", "the web", "https://example.com/auto" },
                                  Links(doc).Select(l => new TextRange(l.ContentStart, l.ContentEnd).Text).ToArray());
    });

    [TestMethod]
    public void AHookThatThrows_CostsThatLinkItsDecoration_NotTheDocument() => UiThread.Run(() =>
    {
        var doc = Build((_, _) => throw new InvalidOperationException("no"));

        CollectionAssert.AreEqual(new[] { "the Help button", "the web", "https://example.com/auto" },
                                  Links(doc).Select(l => new TextRange(l.ContentStart, l.ContentEnd).Text).ToArray());
    });

    [TestMethod]
    public void NoHook_LeavesEveryLinkAsItWasWritten() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build(Doc, MarkdownPalette.Dark);

        Assert.AreEqual(3, Links(doc).Count);
        Assert.IsTrue(Links(doc).All(l => l.ToolTip is null));
    });

    [TestMethod]
    public void TheViewHandsItsHookOn() => UiThread.Run(() =>
    {
        var offered = new List<string>();
        var view = new SelectableMarkdownView { LinkDecorator = (_, url) => offered.Add(url) };

        view.Markdown = Doc;

        CollectionAssert.Contains(offered, "locate:Chrome_HelpButton");
        Assert.AreEqual(3, Links(((RichTextBox)view.Content).Document).Count);
    });

    private static FlowDocument Build(Action<Hyperlink, string> decorate)
        => MarkdownFlowDocument.Build(Doc, new MarkdownRenderContext
        {
            Palette      = MarkdownPalette.Dark,
            DecorateLink = decorate,
        });

    private static List<Hyperlink> Links(FlowDocument doc)
    {
        var found = new List<Hyperlink>();
        void Walk(DependencyObject node)
        {
            if (node is Hyperlink link) found.Add(link);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        Walk(doc);
        return found;
    }
}
