using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// The text engine (<c>Nexaflow.Markdown</c>) names no UI toolkit — not in its own code, not through anything it references,
/// however far down (docs/markdown-ast.md, Ownership). It holds everything a page's text surface knows and decides: the
/// content, the caret and selection, find, the view's numbers, the languages and their edits. Whatever draws it — the WPF
/// surface today, another toolkit's tomorrow — paints and maps input over it, so the engine carries over unchanged. Its own
/// project references are named here, so a new one is a decision rather than an accident.
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class EngineAssemblyRulesTests
{
    private const string Engine = "Nexaflow.Markdown";

    private static readonly string[] Toolkit = ["PresentationCore", "PresentationFramework", "WindowsBase", "System.Xaml", "System.Windows.Forms"];

    /// <summary>What the engine itself references of Nexaflow's: the icon names a diagram draws, the user's words, and the
    /// code readings that colour and fold code.</summary>
    private static readonly string[] OwnReferences = ["Nexaflow.Icons", "Nexaflow.Localization", "Nexaflow.Syntax"];

    [TestMethod]
    [TestCategory("Unit")]
    public void The_engine_reaches_no_UI_toolkit()
    {
        var offenders = Reached(Engine).Where(path => Forbidden(path[^1])).Select(path => string.Join(" → ", path)).ToList();

        Assert.AreEqual(0, offenders.Count,
            "the text engine reaches a UI toolkit, or something drawn in one:\n  " + string.Join("\n  ", offenders)
          + "\nWhat only a toolkit can do is the surface's: hand the engine a port (ILayoutMedium, IContentEvents) and fill it there.");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void The_engine_project_targets_no_toolkit_and_references_only_what_it_names()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot.Locate(), "src", Engine, Engine + ".csproj"));
        var properties = project.Descendants().Where(e => e.Parent?.Name.LocalName == "PropertyGroup").ToList();

        var problems = properties
            .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks" && e.Value.Contains("-windows", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"{e.Name.LocalName} {e.Value}")
            .Concat(properties.Where(e => e.Name.LocalName is "UseWPF" or "UseWindowsForms"
                                       && string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                              .Select(e => $"{e.Name.LocalName} true"))
            .Concat(project.Descendants().Where(e => e.Name.LocalName == "ProjectReference")
                           .Select(e => Path.GetFileNameWithoutExtension((string?)e.Attribute("Include") ?? ""))
                           .Where(name => !OwnReferences.Contains(name))
                           .Select(name => $"ProjectReference {name}"))
            .ToList();

        Assert.AreEqual(0, problems.Count,
            "the text engine's project targets a toolkit, or references a project it does not name:\n  " + string.Join("\n  ", problems)
          + $"\nIt targets plain .NET and references {string.Join(", ", OwnReferences)}; a new reference is added here with why.");
    }

    private static bool Forbidden(string name)
        => Toolkit.Contains(name)
        || name is "Nexaflow" or "Nexaflow.Core" or "Nexaflow.Interaction"
        || name.StartsWith("Nexaflow.Visuals.", StringComparison.Ordinal)
        || name.StartsWith("Nexaflow.Features.", StringComparison.Ordinal);

    /// <summary>Every assembly <paramref name="root"/> reaches by reference, each with the way it is reached; Nexaflow's own
    /// followed further, anything else seen and left.</summary>
    private static IEnumerable<string[]> Reached(string root)
    {
        var seen    = new HashSet<string>(StringComparer.Ordinal) { root };
        var pending = new Queue<string[]>([[root]]);
        while (pending.TryDequeue(out var path))
        {
            var name = path[^1];
            if (path.Length > 1) yield return path;
            if (name != root && !name.StartsWith("Nexaflow.", StringComparison.Ordinal)) continue;

            foreach (var reference in Assembly.Load(name).GetReferencedAssemblies())
                if (reference.Name is { } next && seen.Add(next))
                    pending.Enqueue([.. path, next]);
        }
    }
}
