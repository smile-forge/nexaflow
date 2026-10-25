using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Styles as <c>Themes/Styles.xaml</c> ships them, read from the source tree with a plain brush for each palette key
/// they name. The markup under test is the shipped markup; the colours don't matter, only which token a state reaches
/// for — each stub is its own brush, so a test can ask which one a state picked.
/// Read as source rather than through <c>pack://</c> for the reason <see cref="ThemeLayerContractTests"/> gives: off
/// that path a layer's StaticResources only reach the palette through an Application, and one created here would
/// leave a dead dispatcher behind for every other test in the process.
/// </summary>
internal static partial class ShippedStyles
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The named resources in one dictionary beside the palette stubs. Name them in the order Styles.xaml declares
    /// them — each after anything it names — since a StaticResource only reaches back.
    /// </summary>
    public static ResourceDictionary Load(params string[] keys)
    {
        var root    = Document();
        var shipped = keys.Select(key => root.Elements().Single(e => (string?)e.Attribute(Xaml + "Key") == key)).ToList();
        var defined = shipped.SelectMany(s => s.DescendantsAndSelf())
                             .Select(e => (string?)e.Attribute(Xaml + "Key"))
                             .OfType<string>()
                             .ToHashSet();
        var palette = shipped.SelectMany(s => StaticResourceKey().Matches(s.ToString()).Select(m => m.Groups[1].Value))
                             .Where(key => !defined.Contains(key))
                             .Distinct()
                             .Select(key => new XElement(root.Name.Namespace + "SolidColorBrush",
                                                         new XAttribute(Xaml + "Key", key), new XAttribute("Color", "Gray")));

        var dictionary = new XElement(root.Name, root.Attributes().Where(a => a.IsNamespaceDeclaration), palette, shipped);
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    /// <summary>The keyless style Styles.xaml gives every <paramref name="targetType"/>, or null when it gives none.</summary>
    public static XElement? Implicit(string targetType)
    {
        var root = Document();
        return root.Elements(root.Name.Namespace + "Style")
                   .SingleOrDefault(e => (string?)e.Attribute("TargetType") == targetType && e.Attribute(Xaml + "Key") is null);
    }

    /// <summary>The element measured and arranged in an off-screen host, so its template — and its parts' — are applied.</summary>
    public static T Laid<T>(T element, double width = 300, double height = 40) where T : UIElement
    {
        var host = new System.Windows.Controls.Border { Child = element };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        return element;
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }

    private static XElement Document() =>
        XDocument.Load(Path.Combine(RepoRoot(), "src", "Nexaflow.Core", "Themes", "Styles.xaml")).Root!;

    [GeneratedRegex(@"\{StaticResource\s+([\w.]+)\s*\}")]
    private static partial Regex StaticResourceKey();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return dir.FullName;

        throw new InvalidOperationException($"Could not locate the repo root (no Nexaflow.slnx above '{AppContext.BaseDirectory}').");
    }
}
