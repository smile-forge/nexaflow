using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Locate;

namespace Nexaflow.Tests.Visuals.Locate;

/// <summary>
/// Finding the control a <c>locate:</c> link names (<see cref="AutomationIdLookup"/>). What matters is that it only ever
/// answers with something a reader could actually see — a lasso round a collapsed panel would point at nothing — and that
/// the surface doing the pointing (help, which has a search box of its own) does not win over the page beside it.
/// <para>UI category: the elements are built and laid out on an STA thread; no window opens.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("help-locate")]
public class AutomationIdLookupTests
{
    [TestMethod]
    public void ItAnswersWithTheControlCarryingTheId() => UiThread.Run(() =>
    {
        var page = Scene();

        Assert.AreEqual("pageBox", AutomationIdLookup.Find(page, "Both")?.Name);
        Assert.IsNull(AutomationIdLookup.Find(page, "Nothing_Like_It"), "an id no view declares is simply not there");
    });

    [TestMethod]
    public void AControlNobodyCanSee_IsNotFound() => UiThread.Run(() =>
    {
        var page = Scene();

        Assert.IsNull(AutomationIdLookup.Find(page, "Collapsed"), "a collapsed branch is skipped whole");
        Assert.IsNull(AutomationIdLookup.Find(page, "Hidden"), "…and so is a hidden control");
        Assert.IsNull(AutomationIdLookup.Find(page, "Sizeless"), "one with no size is passed over");
    });

    [TestMethod]
    public void TheSurfaceDoingThePointing_IsSearchedLast() => UiThread.Run(() =>
    {
        var page = Scene();
        var help = (FrameworkElement)LogicalTreeHelper.FindLogicalNode(page, "help")!;

        Assert.AreEqual("pageBox", AutomationIdLookup.Find(page, "Both", help)?.Name, "the page's, not help's own copy");
        Assert.AreEqual("helpOnly", AutomationIdLookup.Find(page, "HelpOnly", help)?.Name,
                        "but help's own beats nothing at all");
        Assert.AreEqual("pageBox", AutomationIdLookup.Find(page, "Both")?.Name, "with nothing to avoid, tree order decides");
    });

    [TestMethod]
    public void WhetherAControlIsStillOnShow_IsAnAnswerableQuestion() => UiThread.Run(() =>
    {
        var page = Scene();
        var shown = AutomationIdLookup.Find(page, "Both")!;

        Assert.IsTrue(AutomationIdLookup.IsShown(shown, page));

        ((UIElement)VisualTreeHelper.GetParent(shown)).Visibility = Visibility.Collapsed;
        Assert.IsFalse(AutomationIdLookup.IsShown(shown, page), "the panel holding it closed");

        Assert.IsFalse(AutomationIdLookup.IsShown(new Button { Width = 10, Height = 10 }, page),
                       "and something that was never in this tree is not on show in it");
    });

    // A page with a control of its own, a closed panel, a hidden control and a sizeless one — and a help pane beside it
    // holding a control with the same id as the page's.
    private static FrameworkElement Scene()
    {
        var page = new StackPanel();
        var help = new StackPanel { Name = "help" };

        page.Children.Add(Control("pageBox", "Both"));
        var closed = new StackPanel { Visibility = Visibility.Collapsed };
        closed.Children.Add(Control("closedBox", "Collapsed"));
        page.Children.Add(closed);
        page.Children.Add(Control("hiddenBox", "Hidden", visibility: Visibility.Hidden));
        page.Children.Add(Control("sizeless", "Sizeless", width: 0, height: 0));
        help.Children.Add(Control("helpCopy", "Both"));
        help.Children.Add(Control("helpOnly", "HelpOnly"));
        page.Children.Add(help);

        page.Measure(new Size(400, 400));
        page.Arrange(new Rect(0, 0, 400, 400));
        page.UpdateLayout();
        return page;

        static Button Control(string name, string automationId, Visibility visibility = Visibility.Visible,
                              double width = 80, double height = 24)
        {
            var button = new Button { Name = name, Content = name, Width = width, Height = height, Visibility = visibility };
            AutomationProperties.SetAutomationId(button, automationId);
            return button;
        }
    }
}
