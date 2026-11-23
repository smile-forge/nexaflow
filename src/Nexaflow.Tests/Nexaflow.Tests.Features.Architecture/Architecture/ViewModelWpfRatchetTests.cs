using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// A view-model that names WPF is one that has to be rewritten, not reused, when the views move to another UI toolkit
/// (docs/Architecture.md, Views and view-models). The view draws; what it draws — the state, the commands, the timers that
/// drive them, the clipboard — belongs to the view-model, in terms any toolkit's view can bind. This is the count of what is
/// left to move, and what stops it growing.
///
/// <para>
/// <b>A ratchet, like <see cref="HandRolledModalRatchetTests"/>.</b> The view-models that name WPF today are listed in
/// <see cref="BaselineFile"/>, one repo-relative path per line: a new one fails, and a listed one that no longer names WPF
/// fails until its line is deleted, so the list can only shrink.
/// </para>
/// <para>
/// What counts as a view-model: a C# file under <c>src/Nexaflow.Features</c> or a <c>src/Nexaflow.Visuals.*</c> library
/// that sits in a <c>ViewModels</c> folder or whose name says <c>ViewModel</c>. What counts as naming WPF:
/// <c>System.Windows</c> anywhere in its code once comments are taken out — a type written out in full counts as much as a
/// <c>using</c>.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class ViewModelWpfRatchetTests
{
    private static readonly string Root = RepoRoot.Locate();

    /// <summary>The ratchet. One repo-relative path per line; <c>#</c> starts a comment.</summary>
    private static string BaselineFile => Path.Combine(
        Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.Features.Architecture", "Architecture", "view-models-on-wpf.txt");

    /// <summary>A comment, or a literal read past whole so a <c>//</c> or <c>/*</c> inside one is not taken for a comment.</summary>
    private static readonly Regex CommentsAndLiterals = new(
        @"@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+'|/\*.*?\*/|//[^\n]*",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex Wpf = new(@"\bSystem\.Windows\b", RegexOptions.Compiled);

    [TestMethod]
    [TestCategory("Unit")]
    public void No_new_view_model_names_WPF()
    {
        var found = ViewModelsOnWpf();
        var baseline = Baseline();

        var fresh = found.Keys
            .Where(path => !baseline.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(0, fresh.Count,
            $"{fresh.Count} view-model(s) name WPF:\n"
          + string.Join("\n", fresh.Select(path => $"  {path}    (line {found[path]})"))
          + "\nA view-model is drawn by a view any UI toolkit can provide: reach the UI thread, a timer or the clipboard\n"
          + "through IShellServices, and leave brushes, bitmaps and controls to the view (docs/Architecture.md).");
    }

    /// <summary>
    /// The other half of the ratchet. A listed view-model that no longer names WPF has to leave the file — otherwise the
    /// count stops being the work left, and a view-model that starts naming WPF again would pass unseen.
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void The_baseline_has_no_stale_entries()
    {
        var found = ViewModelsOnWpf();

        var gone = Baseline()
            .Where(path => !found.ContainsKey(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(0, gone.Count,
            $"{Path.GetFileName(BaselineFile)} is out of date — these no longer name WPF; delete the lines:\n"
          + string.Join("\n", gone.Select(path => $"  {path}")));
    }

    /// <summary>Every view-model that names WPF, by repo-relative path, with the line it first does so on.</summary>
    private static Dictionary<string, int> ViewModelsOnWpf()
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        var src = Path.Combine(Root, "src");
        var roots = Directory.EnumerateDirectories(src, "Nexaflow.Visuals.*").Prepend(Path.Combine(src, "Nexaflow.Features"));

        foreach (var file in roots.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)))
        {
            if (IsBuildOutput(file) || !IsViewModel(file)) continue;

            var code = CommentsAndLiterals.Replace(File.ReadAllText(file), WithoutComment);
            var wpf = Wpf.Match(code);
            if (!wpf.Success) continue;

            found.Add(Path.GetRelativePath(Root, file).Replace('\\', '/'), 1 + code.AsSpan(0, wpf.Index).Count('\n'));
        }
        return found;
    }

    /// <summary>A comment becomes the line breaks it spanned, so a line number found after it still counts true; a literal stays.</summary>
    private static string WithoutComment(Match match) =>
        match.Value.StartsWith('/') ? new string('\n', match.Value.Count(c => c == '\n')) : match.Value;

    private static bool IsViewModel(string file) =>
        Path.GetFileName(file).Contains("ViewModel", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}ViewModels{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static HashSet<string> Baseline()
    {
        if (!File.Exists(BaselineFile)) return new HashSet<string>(StringComparer.Ordinal);
        return File.ReadAllLines(BaselineFile)
                   .Select(l => l.Trim())
                   .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                   .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
}
