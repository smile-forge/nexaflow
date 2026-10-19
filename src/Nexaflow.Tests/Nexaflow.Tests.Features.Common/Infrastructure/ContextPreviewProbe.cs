using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Infrastructure;

/// <summary>
/// Reads what a page's context preview shows, off-screen: lays it out at about the context panel's width, then
/// gathers the text of every line left visible. On an STA thread — see <see cref="UiThread"/>.
/// </summary>
public static class ContextPreviewProbe
{
    /// <summary>About the width of the conversation's context panel.</summary>
    private const double PanelWidth = 360;

    public static IReadOnlyList<string> VisibleTexts(UserControl preview)
    {
        preview.Measure(new Size(PanelWidth, double.PositiveInfinity));
        preview.Arrange(new Rect(preview.DesiredSize));
        // Bindings deferred until the dispatcher runs: let it reach everything above Background, as a shown panel would.
        preview.Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);

        var texts = new List<string>();
        Gather(preview, texts);
        return texts;
    }

    private static void Gather(DependencyObject node, List<string> texts)
    {
        if (node is UIElement { Visibility: not Visibility.Visible }) return;
        if (node is TextBlock { Text.Length: > 0 } line) texts.Add(line.Text);
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            Gather(child, texts);
    }
}
