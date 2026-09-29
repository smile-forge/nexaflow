using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The closed <c>DarkComboBox</c> shows what the open list shows. Its template draws the selection box itself, and
/// a presenter given only the selection-box item and template drops <c>DisplayMemberPath</c> — WPF carries that as
/// the combo's <c>ItemTemplateSelector</c> — so a record item read as <c>Choice { Value = …, Label = … }</c> once
/// chosen. An <c>ItemTemplate</c> must still win over it.
/// <para>Builds the combo off-screen on an STA thread; opens no window. Not parallel: BAML
/// loading shares WPF's process-wide schema cache.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("opt-editor-listsource")]
public partial class DarkComboBoxTests
{
    public sealed record Choice(string Value, string Label);

    private static readonly Choice[] Choices = [new("a", "Alpha"), new("b", "Beta")];

    [TestMethod]
    public void TheClosedBoxShowsTheDisplayMember() => UiThread.Run(() =>
    {
        var shown = ClosedText(combo => combo.DisplayMemberPath = nameof(Choice.Label));

        CollectionAssert.Contains(shown, "Beta");
        Assert.IsFalse(shown.Any(t => t.Contains(nameof(Choice))), $"the item's ToString() showed: {string.Join(" | ", shown)}");
    });

    [TestMethod]
    public void AnItemTemplateStillWinsOverTheDisplayMemberSelector() => UiThread.Run(() =>
    {
        var shown = ClosedText(combo => combo.ItemTemplate = ValueTemplate());

        CollectionAssert.Contains(shown, "b");
        CollectionAssert.DoesNotContain(shown, "Beta");
    });

    [TestMethod]
    public void TheClosedBoxHonoursTheItemStringFormat() => UiThread.Run(() =>
    {
        var shown = ClosedText(combo =>
        {
            combo.ItemsSource  = new[] { 1.5, 2.25 };
            combo.ItemStringFormat = "{0:0.00} MB";
        });

        CollectionAssert.Contains(shown, "2.25 MB");
    });

    /// <summary>Every text the closed combo draws, with its second item chosen.</summary>
    private static List<string> ClosedText(Action<ComboBox> configure)
    {
        var host  = new Border();
        var combo = new ComboBox { Style = ShippedDarkComboBox(), ItemsSource = Choices };
        configure(combo);
        combo.SelectedIndex = 1;
        host.Child = combo;

        host.Measure(new Size(300, 40));
        host.Arrange(new Rect(0, 0, 300, 40));
        host.UpdateLayout();

        return Descendants(combo).OfType<TextBlock>().Select(t => t.Text).ToList();
    }

    /// <summary>
    /// The shipped <c>DarkComboBox</c>, read from <c>Themes/Styles.xaml</c> in the source tree with its item style, and a
    /// plain brush for each palette key they name. The markup under test is the shipped markup; the colours don't matter.
    /// Read as source rather than through <c>pack://</c> for the reason <see cref="ThemeLayerContractTests"/> gives: off
    /// that path a layer's StaticResources only reach the palette through an Application, and one created here would
    /// leave a dead dispatcher behind for every other test in the process.
    /// </summary>
    private static Style ShippedDarkComboBox()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "Nexaflow.Core", "Themes", "Styles.xaml"));
        var root = doc.Root!;
        string[] shipped = ["DarkComboBoxItem", "DarkComboBox"];
        var styles = shipped.Select(key => root.Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == key)).ToList();
        var palette = styles.SelectMany(s => StaticResourceKey().Matches(s.ToString()).Select(m => m.Groups[1].Value))
                            .Except(shipped)
                            .Distinct()
                            .Select(key => new XElement(root.Name.Namespace + "SolidColorBrush",
                                                        new XAttribute(Xaml + "Key", key), new XAttribute("Color", "Gray")));

        var dictionary = new XElement(root.Name, root.Attributes().Where(a => a.IsNamespaceDeclaration), palette, styles);
        return (Style)((ResourceDictionary)XamlReader.Parse(dictionary.ToString()))["DarkComboBox"];
    }

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [GeneratedRegex(@"\{StaticResource\s+([\w.]+)\s*\}")]
    private static partial Regex StaticResourceKey();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return dir.FullName;

        throw new InvalidOperationException($"Could not locate the repo root (no Nexaflow.slnx above '{AppContext.BaseDirectory}').");
    }

    private static DataTemplate ValueTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Choice.Value)));
        var template = new DataTemplate { VisualTree = text };
        template.Seal();
        return template;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }
}
