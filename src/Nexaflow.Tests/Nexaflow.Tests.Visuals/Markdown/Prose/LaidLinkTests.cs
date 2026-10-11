using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// The links of a document as UI Automation reads them: one Hyperlink per link, named by its words, which does on
/// Invoke exactly what a click does. The engine says what the links are and what following one means; the surface only
/// puts that where a screen reader or a UI journey can reach it.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-surface")]
public class LaidLinkTests
{
    [TestMethod]
    public void EveryLinkIsOneHyperlink_NamedByTheWordsAReaderSees() => UiThread.Run(() =>
    {
        var view = Shown("see [the **bold** page](https://example.org/a) and [two](https://example.org/b)\n");

        var links = Hyperlinks(view);

        CollectionAssert.AreEqual(new[] { "the bold page", "two" }, links.Select(link => link.GetName()).ToArray(),
            "a link whose words change face is still one link, and its name is all of its words");
        CollectionAssert.AreEqual(new[] { "https://example.org/a", "https://example.org/b" },
            links.Select(link => ((IValueProvider)link.GetPattern(PatternInterface.Value)!).Value).ToArray(),
            "where it goes is its value, as a hyperlink's is");
    });

    [TestMethod]
    public void ALinkThatWraps_IsStillOneLink() => UiThread.Run(() =>
    {
        var view = Shown("[a link long enough that a narrow page has to carry it over onto another line](https://example.org/w)\n",
                         width: 140);

        Assert.AreEqual(1, Hyperlinks(view).Count);
    });

    [TestMethod]
    public void AMarkTheHostSetsBesideALink_IsNotPartOfItsName() => UiThread.Run(() =>
    {
        // The help pane's locate: links carry a pin in front, which is the host's and not the writer's.
        var view = Shown("[the Help button](locate:Chrome_HelpButton)\n",
                         decorate: (where, _) => where.StartsWith("locate:")
                             ? new LinkLook(Brushes.Orange, Before: "\U0001F4CD", Says: "Shows where it is")
                             : null);

        var link = Hyperlinks(view).Single();

        Assert.AreEqual("the Help button", link.GetName());
        Assert.AreEqual("Shows where it is", link.GetHelpText(), "what the host says of it is its help text");
    });

    [TestMethod]
    public void InvokingALink_RaisesWhatAClickRaises() => UiThread.Run(() =>
    {
        var view = Shown("[one](https://example.org/1) and [two](https://example.org/2)\n");
        var followed = new List<string>();
        view.LinkNavigate += (_, e) => followed.Add(e.Url);

        Invoke(Hyperlinks(view).Single(link => link.GetName() == "two"));

        CollectionAssert.AreEqual(new[] { "https://example.org/2" }, followed);
    });

    [TestMethod]
    public void AnInPageLink_IsAnsweredByTheDocument_AndNeverHandedToTheHost() => UiThread.Run(() =>
    {
        var view = Shown("## Notes\n\nwords\n\n[back up](#notes)\n");
        var followed = new List<string>();
        view.LinkNavigate += (_, e) => followed.Add(e.Url);

        Invoke(Hyperlinks(view).Single());

        CollectionAssert.AreEqual(System.Array.Empty<string>(), followed, "the same as a click on it");
    });

    [TestMethod]
    public void ALinkFromALayoutSinceReplaced_IsGone() => UiThread.Run(() =>
    {
        var view = Shown("[one](https://example.org/1)\n");
        var stale = Hyperlinks(view).Single();

        view.Markdown = "[other](https://example.org/2)\n";
        Settle(view);

        Assert.ThrowsExactly<ElementNotAvailableException>(() => Invoke(stale));
        Assert.AreEqual("other", Hyperlinks(view).Single().GetName(), "and the document's peer offers the new one");
    });

    // ── Driving ─────────────────────────────────────────────────────────────

    private static MarkdownSurface Shown(string markdown, double width = 640,
                                         System.Func<string, string, LinkLook?>? decorate = null)
    {
        var view = new MarkdownSurface { LinkDecorator = decorate, Markdown = markdown };
        Settle(view, width);
        return view;
    }

    private static void Settle(MarkdownSurface view, double width = 640)
    {
        view.Measure(new Size(width, 2000));
        view.Arrange(new Rect(0, 0, width, 2000));
        view.UpdateLayout();
    }

    /// <summary>Every Hyperlink in the surface's automation tree, in the order UI Automation lists them.</summary>
    private static List<AutomationPeer> Hyperlinks(MarkdownSurface view)
    {
        var found = new List<AutomationPeer>();
        Walk(UIElementAutomationPeer.CreatePeerForElement(view));
        return found;

        void Walk(AutomationPeer? peer)
        {
            if (peer is null) return;
            if (peer.GetAutomationControlType() == AutomationControlType.Hyperlink) found.Add(peer);
            foreach (var child in peer.GetChildren() ?? []) Walk(child);
        }
    }

    private static void Invoke(AutomationPeer link) =>
        ((IInvokeProvider)link.GetPattern(PatternInterface.Invoke)!).Invoke();
}
