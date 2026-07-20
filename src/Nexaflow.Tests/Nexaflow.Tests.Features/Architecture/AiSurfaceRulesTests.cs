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
        // Each of these exposes tools but returns GetSecurityContext() == null today — pinning two of the
        // same kind collapses their tools first-wins. Give each a stable scope and delete it from here.
        "Nexaflow.Features.AIChat.ViewModels.ConversationViewModel",   // the hub itself; aggregates pinned tools
        "Nexaflow.Features.Console.ViewModels.CmdTerminalViewModel",
        "Nexaflow.Features.Font.ViewModels.FontViewModel",
        "Nexaflow.Features.ProductManager.ViewModels.ProductViewModel",
        "Nexaflow.Features.Scratchpad.ViewModels.ScratchpadViewModel",
        "Nexaflow.Features.Web.ViewModels.HtmlViewModel",
        "Nexaflow.Features.WindowsSearch.ViewModels.SearchViewModel",
    };

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
