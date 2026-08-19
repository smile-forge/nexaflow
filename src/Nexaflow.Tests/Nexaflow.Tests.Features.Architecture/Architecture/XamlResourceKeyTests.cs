using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Every <c>{StaticResource Key}</c> in a view has to resolve to a key that actually exists — declared
/// somewhere in that view's own project (its own resources, a sibling dictionary it merges, a theme
/// contribution it ships), or app-level in Core's merged theme dictionaries.
/// <para>
/// This guards the one failure mode nothing else catches. An unresolvable key compiles perfectly happily
/// and only throws when WPF parses the view, so the first person to find out is a user opening the tab —
/// greeted by <c>XamlParseException: 'Provide value on StaticResourceExtension' threw an exception</c>
/// with a line number and no key name. It is especially easy to hit by copying a <c>{StaticResource X}</c>
/// from another view whose <c>X</c> turns out to be a style local to <em>that</em> feature: the reference
/// looks exactly like a reference to a shared one.
/// </para>
/// <para>
/// Project scope, not file scope, is the right granularity: a feature legitimately splits its resources
/// across sibling dictionaries and merges them. Crossing a project boundary is the part that can't work.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Cross-cutting XAML guard: maps to no single product node.")]
public class XamlResourceKeyTests
{
    private static readonly string Root = RepoRoot.Locate();

    /// <summary>Core's app-merged dictionaries — the resources any view may reference by key.</summary>
    private static readonly string ThemesDir = Path.Combine(Root, "src", "Nexaflow.Core", "Themes");

    private static readonly Regex UsageRe = new(@"\{\s*StaticResource\s+([A-Za-z_][A-Za-z0-9_.]*)\s*\}",
                                                RegexOptions.Compiled);
    private static readonly Regex KeyRe   = new(@"x:Key\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    private static IEnumerable<string> XamlUnder(string dir)
        => Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories)
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                             && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static HashSet<string> KeysIn(IEnumerable<string> files)
    {
        var keys = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var file in files)
            foreach (Match m in KeyRe.Matches(File.ReadAllText(file)))
                keys.Add(m.Groups[1].Value);
        return keys;
    }

    /// <summary>The directory of the .csproj owning this file — the scope a merged dictionary can reach.</summary>
    private static string? OwningProjectDir(string file)
    {
        for (var dir = Path.GetDirectoryName(file); dir is not null && dir.StartsWith(Root); dir = Path.GetDirectoryName(dir))
            if (Directory.EnumerateFiles(dir, "*.csproj").Any()) return dir;
        return null;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Every_StaticResource_reference_resolves_to_a_key_that_exists()
    {
        var appKeys = KeysIn(XamlUnder(ThemesDir));
        Assert.IsTrue(appKeys.Count > 50, $"Only found {appKeys.Count} app-level keys — is {ThemesDir} right?");

        var srcDir = Path.Combine(Root, "src");
        var projectKeys = new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);
        var misses = new List<string>();

        foreach (var file in XamlUnder(srcDir))
        {
            if (file.StartsWith(ThemesDir, System.StringComparison.OrdinalIgnoreCase)) continue;

            var text = File.ReadAllText(file);
            if (OwningProjectDir(file) is not { } projectDir) continue;

            if (!projectKeys.TryGetValue(projectDir, out var local))
                projectKeys[projectDir] = local = KeysIn(XamlUnder(projectDir));

            foreach (Match use in UsageRe.Matches(text))
            {
                var key = use.Groups[1].Value;
                if (local.Contains(key) || appKeys.Contains(key)) continue;

                var line = text.Take(use.Index).Count(c => c == '\n') + 1;
                misses.Add($"{Path.GetRelativePath(Root, file)}:{line}  {{StaticResource {key}}}");
            }
        }

        Assert.AreEqual(0, misses.Count,
            "These StaticResource keys are declared neither in their own project nor app-level in "
            + "src/Nexaflow.Core/Themes. Each one throws XamlParseException when the view is opened — "
            + "move the style into Themes/Styles.xaml if it is genuinely shared.\n  - "
            + string.Join("\n  - ", misses));
    }
}
