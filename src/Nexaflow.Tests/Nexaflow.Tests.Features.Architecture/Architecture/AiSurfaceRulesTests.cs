using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Aspect 4 of AI-readiness ("two pinned pages in one conversation won't be confused"): a page that
/// exposes client tools MUST also return a distinct <see cref="IPageViewModel.GetSecurityContext"/>. The
/// conversation hub (<c>MultiContextClientTool</c>) groups pinned tools by name and disambiguates them by
/// each page's security context — so a tool-bearing page with a null scope collapses first-wins against
/// another instance, and the second page's tools become silently unreachable.
/// <para>
/// Detection is reflection-only (no ViewModel is instantiated): <see cref="IPageViewModel"/>'s optional
/// members are default interface methods, so a type that overrides one exposes a matching public method
/// and a type that inherits the default does not — <see cref="Provides"/> tells them apart.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class AiSurfaceRulesTests
{
    /// <summary>
    /// Tool-bearing pages that currently return <c>GetSecurityContext() == null</c> — a real aspect-4 gap
    /// (two pinned instances collapse first-wins). This is <b>debt to burn down</b>: remove a name here the
    /// moment its page returns a scope; a page reaching a terminal state can add nothing — a NEW offender
    /// that isn't listed fails the build. See docs/features.md → AI client tools.
    /// </summary>
    private static readonly HashSet<string> KnownNullScope = new(StringComparer.Ordinal)
    {
        // Exposes tools but returns GetSecurityContext() == null — pinning two of the same kind collapses
        // their tools first-wins. The conversation hub aggregates every pinned page's tools, so it has no
        // single scope of its own; it is the one legitimate terminal entry here (not debt to burn down).
        "Nexaflow.Features.AIChat.ViewModels.ConversationViewModel",   // the hub itself; aggregates pinned tools
    };

    /// <summary>
    /// Pages with a security context that show nothing from the disk, so put no file or folder in front of the assistant
    /// (<see cref="IPageViewModel.GetContextGrant"/> left at none): processes, services, the environment, the registry,
    /// the scratchpad, the solver, a web page, the installed apps, a serial port and the USB tree. Every other page with a scope says what of the disk
    /// it shows — a page that leaves it out would let no tool reach its own file, or, worse, be forgotten when it should.
    /// </summary>
    private static readonly HashSet<string> ShowsNothingFromDisk = new(StringComparer.Ordinal)
    {
        "Nexaflow.Features.Processes.ViewModels.ProcessDetailViewModel",
        "Nexaflow.Features.Processes.ViewModels.ProcessesViewModel",
        "Nexaflow.Features.SystemInfo.ViewModels.EnvironmentVariablesViewModel",
        "Nexaflow.Features.SystemInfo.ViewModels.ServicesViewModel",
        "Nexaflow.Features.WindowsRegistry.ViewModels.RegistryViewModel",
        "Nexaflow.Features.Scratchpad.ViewModels.ScratchpadViewModel",
        "Nexaflow.Features.Solver.ViewModels.SolverViewModel",
        "Nexaflow.Features.Web.ViewModels.HtmlViewModel",
        "Nexaflow.Features.WindowsApps.ViewModels.WindowsAppsViewModel",
        "Nexaflow.Features.Serial.ViewModels.SerialViewModel",
        "Nexaflow.Features.UsbProbe.ViewModels.UsbProbeViewModel",
    };

    private static List<string> ScopedPagesWithoutAGrant()
        => PageViewModels()
            .Where(t => Provides(t, nameof(IPageViewModel.GetSecurityContext)) && !Provides(t, nameof(IPageViewModel.GetContextGrant)))
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [TestMethod]
    public void Pages_with_a_scope_say_what_of_the_disk_they_show()
    {
        var offenders = ScopedPagesWithoutAGrant().Where(n => !ShowsNothingFromDisk.Contains(n)).ToList();

        Assert.AreEqual(0, offenders.Count,
            "A page with a security context must say what of the disk it puts in front of the assistant — GetContextGrant: " +
            "the files it shows, the folders it shows whole — or, if it shows nothing from the disk, be listed in " +
            "ShowsNothingFromDisk. Missing: " + string.Join(", ", offenders));
    }

    [TestMethod]
    public void ShowsNothingFromDisk_has_no_stale_entries()
    {
        var actual = ScopedPagesWithoutAGrant().ToHashSet(StringComparer.Ordinal);
        var stale = ShowsNothingFromDisk.Where(n => !actual.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.AreEqual(0, stale.Count, "Remove entries whose page now grants, or was renamed or removed: " + string.Join(", ", stale));
    }

    private static IEnumerable<Type> PageViewModels()
        => Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Features.*.dll")
            .SelectMany(p => Assembly.Load(Path.GetFileNameWithoutExtension(p)).GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPageViewModel).IsAssignableFrom(t));

    /// <summary>True when <paramref name="type"/> (or a non-interface base) supplies its own parameterless
    /// <paramref name="method"/> — i.e. it overrides the <see cref="IPageViewModel"/> default rather than
    /// inheriting it. Default interface methods live on the interface, so <see cref="Type.GetMethod(string, Type[])"/>
    /// returns them as null here and a real override as non-null.</summary>
    private static bool Provides(Type type, string method)
        => type.GetMethod(method, BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null) is not null;

    private static List<string> ToolBearingPagesWithNullScope()
        => PageViewModels()
            .Where(t => Provides(t, nameof(IPageViewModel.GetClientTools)) && !Provides(t, nameof(IPageViewModel.GetSecurityContext)))
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [TestMethod]
    public void Tool_bearing_pages_declare_a_security_context()
    {
        var offenders = ToolBearingPagesWithNullScope()
            .Where(n => !KnownNullScope.Contains(n))
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            "A page exposing client tools must also return a distinct GetSecurityContext() so two pinned " +
            "instances don't collapse first-wins in MultiContextClientTool. Either add a scope or, if this " +
            "is deliberate debt, list it in KnownNullScope. New offenders: " + string.Join(", ", offenders));
    }

    [TestMethod]
    public void KnownNullScope_has_no_stale_entries()
    {
        var actual = ToolBearingPagesWithNullScope().ToHashSet(StringComparer.Ordinal);
        var stale = KnownNullScope.Where(n => !actual.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.AreEqual(0, stale.Count,
            "KnownNullScope must only ever shrink — remove entries whose page now returns a scope (or was " +
            "renamed/removed): " + string.Join(", ", stale));
    }
}
