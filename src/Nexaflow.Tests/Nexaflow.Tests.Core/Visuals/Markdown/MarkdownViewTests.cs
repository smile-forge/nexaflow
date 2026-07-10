using Nexaflow.Visuals.Text.Markdown;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using VisMarkdownView = Nexaflow.Visuals.Text.Markdown.MarkdownView;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-rendering")]
public class MarkdownViewTests
{
    [TestMethod]
    public void Markdown_PopulatesBlocksPanel() => UiThread.Run(() =>
    {
        var view = new VisMarkdownView();
        view.Markdown = "# Title\n\nA paragraph.\n\n- one\n- two\n";

        var panel = FindBlocksPanel(view);
        Assert.IsNotNull(panel, "BlocksPanel not found");
        Assert.AreEqual(3, panel.Children.Count); // heading + paragraph + list
    });

    [TestMethod]
    public void EmptyMarkdown_LeavesPanelEmpty() => UiThread.Run(() =>
    {
        var view  = new VisMarkdownView { Markdown = string.Empty };
        var panel = FindBlocksPanel(view);
        Assert.IsNotNull(panel);
        Assert.AreEqual(0, panel.Children.Count);
    });

    private static StackPanel? FindBlocksPanel(VisMarkdownView v)
    {
        if (v.Content is ScrollViewer sv && sv.Content is StackPanel sp) return sp;
        if (v.Content is StackPanel direct) return direct;
        return null;
    }
}

internal static class UiThread
{
    public static void Run(Action action)
    {
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null) throw caught;
    }
}
