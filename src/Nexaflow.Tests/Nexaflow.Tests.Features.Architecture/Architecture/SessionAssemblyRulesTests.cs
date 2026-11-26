using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// The session, the assistant and the composition root over them name no UI toolkit — not in their own code, and not through
/// anything they reference, however far down (docs/Architecture.md, Ownership &amp; Lifetime). What only a toolkit can do reaches
/// them through ports the app fills (<c>ShellPlatform</c>), so another toolkit's shell is a second adapter over the same
/// session rather than a rewrite of it. And they stack one way: the assistant consumes the session, the composition root wires
/// both, and the session knows neither. The app uses them through their public surface alone, as another toolkit's shell would.
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class SessionAssemblyRulesTests
{
    private static readonly string[] Toolkit = ["PresentationCore", "PresentationFramework", "WindowsBase", "System.Xaml", "System.Windows.Forms"];

    private static readonly string[] Roots = ["Nexaflow.Session", "Nexaflow.Assistant", "Nexaflow.Host", "Nexaflow.Localization"];

    [TestMethod]
    [TestCategory("Unit")]
    public void The_session_the_assistant_and_the_host_reach_no_UI_toolkit()
    {
        var offenders = Roots
            .SelectMany(root => Reached(root).Where(path => Forbidden(path[^1])).Select(path => string.Join(" → ", path)))
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            "the session's assemblies reach a UI toolkit, or the WPF app and its visuals:\n  " + string.Join("\n  ", offenders)
          + "\nHand what only a toolkit can do to the session as a port in ShellPlatform, filled by the app.");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void The_session_knows_neither_the_assistant_nor_the_host_and_the_assistant_not_the_host()
    {
        var offenders = Reached("Nexaflow.Session").Where(path => path[^1] is "Nexaflow.Assistant" or "Nexaflow.Host")
            .Concat(Reached("Nexaflow.Assistant").Where(path => path[^1] is "Nexaflow.Host"))
            .Select(path => string.Join(" → ", path))
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            "the session's assemblies stack the wrong way:\n  " + string.Join("\n  ", offenders)
          + "\nThe session holds a runtime's assistant as IWorkspaceAssistant, and the composition root hands it the factory.");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void The_app_uses_the_session_through_its_public_surface_alone()
    {
        var granted = new[] { "Nexaflow.Session", "Nexaflow.Assistant", "Nexaflow.Host" }
            .Where(name => Assembly.Load(name).GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
                                              .Any(a => a.AssemblyName.Split(',')[0].Trim() == "Nexaflow"))
            .ToList();

        Assert.AreEqual(0, granted.Count,
            "these grant the WPF app their internals: " + string.Join(", ", granted)
          + "\nWhat a shell's views and windows use of the session is its public API — another toolkit's shell has only that. Make it "
          + "public, or move the caller where it belongs.");
    }

    private static bool Forbidden(string name)
        => Toolkit.Contains(name) || name == "Nexaflow" || name.StartsWith("Nexaflow.Visuals.", StringComparison.Ordinal);

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
