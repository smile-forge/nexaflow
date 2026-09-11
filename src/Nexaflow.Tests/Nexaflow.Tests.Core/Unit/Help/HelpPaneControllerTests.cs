using Nexaflow.Core.Help;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The Help button's rules (<see cref="HelpPaneController"/>), over real <see cref="Pane"/>s: help opens beside the
/// page in use (splitting if it must), closes when pressed while showing that page, re-points when showing another,
/// and follows the other pane's page until the reader pins it by moving within help.
/// </summary>
[TestClass]
[CoversNode("help-pane-controller")]
public class HelpPaneControllerTests
{
    private sealed class Window : IHelpPaneHost
    {
        public List<Pane> Panes { get; } = [new Pane()];
        public Pane Focused { get; set; }
        public HashSet<Page> Pinned { get; } = [];

        public Window() => Focused = Panes[0];

        public IReadOnlyList<Pane> LeafPanes => Panes;
        public Pane FocusedPane => Focused;

        public Pane SplitBeside(Pane subject)
        {
            var beside = new Pane();
            Panes.Add(beside);
            return beside;
        }

        public void OpenInPane(Pane pane, string pageKind, Dictionary<string, string> pageParams)
        {
            pane.Add(new Page { PageKind = pageKind, PageParams = pageParams });
            Focused = pane;
        }

        public void Activate(Page page)
        {
            var pane = Panes.First(p => p.Pages.Contains(page));
            pane.ActivePage = page;
            Focused = pane;
        }

        public void Close(Page page)
        {
            var pane = Panes.First(p => p.Pages.Contains(page));
            pane.Remove(page);
            if (pane.Pages.Count == 0 && Panes.Count > 1) Panes.Remove(pane);   // an emptied pane collapses the split
        }

        public Page Open(Pane pane, string kind)
        {
            var page = new Page { PageKind = kind };
            pane.Add(page);
            return page;
        }

        public Page? Help => Panes.SelectMany(p => p.Pages).FirstOrDefault(p => p.PageKind == "Help");

        public HelpPaneController Controller() => new(this, Pinned.Contains);
    }

    private static string? TopicOf(Page? help) => help?.PageParams?.GetValueOrDefault("topic");

    [TestMethod]
    public void NoHelp_SplitsTheWindow_AndOpensHelpBesideThePageInUse()
    {
        var window = new Window();
        var text = window.Open(window.Panes[0], "Text");

        window.Controller().Toggle();

        Assert.AreEqual(2, window.Panes.Count, "the window splits");
        Assert.AreSame(text, window.Panes[0].ActivePage, "the page stays on the left");
        Assert.AreSame(window.Help, window.Panes[1].ActivePage, "its help opens on the right");
        Assert.AreEqual("Text", TopicOf(window.Help));
    }

    [TestMethod]
    public void AlreadySplit_HelpOpensInThePaneBesideThePageInUse()
    {
        var window = new Window();
        window.Panes.Add(new Pane());
        window.Open(window.Panes[0], "Markdown");
        window.Open(window.Panes[1], "Text");
        window.Focused = window.Panes[1];

        window.Controller().Toggle();

        Assert.AreEqual(2, window.Panes.Count, "no second split");
        Assert.AreSame(window.Help, window.Panes[0].ActivePage, "help lands beside the page in use, whichever side that is");
        Assert.AreEqual("Text", TopicOf(window.Help));
    }

    [TestMethod]
    public void PressedWhileShowingThePageInUse_HelpCloses_AndTheSplitWithIt()
    {
        var window = new Window();
        window.Open(window.Panes[0], "Text");
        var controller = window.Controller();
        controller.Toggle();

        controller.Toggle();   // the focus is on help now: the page in use is the one beside it

        Assert.IsNull(window.Help);
        Assert.AreEqual(1, window.Panes.Count);
    }

    [TestMethod]
    public void PressedWhileShowingAnotherPage_HelpTurnsToThePageInUse()
    {
        var window = new Window();
        window.Open(window.Panes[0], "Text");
        var controller = window.Controller();
        controller.Toggle();
        window.Help!.PageParams = new() { ["topic"] = "Markdown" };   // the reader went elsewhere in help
        window.Focused = window.Panes[0];

        controller.Toggle();

        Assert.IsNotNull(window.Help, "re-pointed, not closed");
        Assert.AreEqual("Text", TopicOf(window.Help));
    }

    [TestMethod]
    public void HelpSharingThePagesPane_MovesBesideIt_RatherThanCoveringIt()
    {
        var window = new Window();
        var help = window.Open(window.Panes[0], "Help");
        window.Open(window.Panes[0], "Text");   // shown over the help, in the one pane

        window.Controller().Toggle();

        Assert.AreEqual(2, window.Panes.Count);
        Assert.IsFalse(window.Panes[0].Pages.Contains(help), "the old tab closes");
        Assert.AreSame(window.Help, window.Panes[1].ActivePage, "and help reopens beside the page");
        Assert.AreEqual("Text", TopicOf(window.Help));
    }

    [TestMethod]
    public void NoPageInUse_OpensTheListOfHelpPages()
    {
        var window = new Window();

        window.Controller().Toggle();

        Assert.IsNotNull(window.Help);
        Assert.IsNull(TopicOf(window.Help), "no topic is the index");
    }

    [TestMethod]
    public void Help_FollowsThePageShownInTheOtherPane()
    {
        var window = new Window();
        window.Open(window.Panes[0], "Text");
        var controller = window.Controller();
        controller.Toggle();

        window.Open(window.Panes[0], "Markdown");
        controller.OnActivePageChanged(window.Panes[0]);

        Assert.AreEqual("Markdown", TopicOf(window.Help));
    }

    [TestMethod]
    public void Help_IgnoresItsOwnPane_AndStaysPutOncePinned()
    {
        var window = new Window();
        window.Open(window.Panes[0], "Text");
        var controller = window.Controller();
        controller.Toggle();

        window.Open(window.Panes[1], "Hex");   // a page opened in help's own pane is not what help explains
        controller.OnActivePageChanged(window.Panes[1]);
        Assert.AreEqual("Text", TopicOf(window.Help));

        window.Pinned.Add(window.Help!);
        window.Open(window.Panes[0], "Markdown");
        controller.OnActivePageChanged(window.Panes[0]);
        Assert.AreEqual("Text", TopicOf(window.Help), "the reader took it somewhere; it stays there");
    }
}
