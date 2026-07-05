using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>One selectable double-click target for an extension in the Default Actions tab.</summary>
public sealed record DefaultActionCandidate(
    DefaultActionKind Kind, string TargetId, string Label, string Group);

/// <summary>
/// Builds the list of actions currently registered for an extension — built-in viewers, user external
/// apps, and Windows shell verbs — for the Default Actions tab. Read-only; uses process singletons, so it
/// needs no shell except the set of viewer experience-ids (which the caller discovers via
/// <c>IShellServices.DiscoverImplementations</c>).
/// </summary>
internal static class DefaultActionResolver
{
    private static IReadOnlyCollection<string>? _viewerIds;

    /// <summary>
    /// Experience-ids backed by a real built-in <see cref="IFileAction"/> — the set a candidate viewer
    /// must be in. Discovered by reflecting the loaded <c>Nexaflow.*</c> assemblies (all feature DLLs are
    /// loaded by the time Options opens) and reading each action's static <c>StaticExperienceId</c>,
    /// excluding the universal "/" (Open-With) and the dynamic actions whose id is null. Cached.
    /// </summary>
    public static IReadOnlyCollection<string> ViewerExperienceIds()
    {
        if (_viewerIds is not null) return _viewerIds;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name is not { } n || !n.StartsWith("Nexaflow", StringComparison.Ordinal)) continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t is null || t.IsAbstract || t.IsInterface || !typeof(IFileAction).IsAssignableFrom(t)) continue;
                var id = t.GetProperty("StaticExperienceId",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(id) && id != "/") set.Add(id);
            }
        }
        return _viewerIds = set;
    }

    public static IReadOnlyList<DefaultActionCandidate> CandidatesFor(string extension)
        => CandidatesFor(extension, ViewerExperienceIds());

    public static IReadOnlyList<DefaultActionCandidate> CandidatesFor(
        string extension, IReadOnlyCollection<string> viewerExperienceIds)
    {
        var ext = NormalizeDotExt(extension);
        var list = new List<DefaultActionCandidate>();
        if (string.IsNullOrEmpty(ext)) return list;

        // ── Internal viewers: experiences that apply to the extension and are backed by a real viewer.
        var applicable = FileMapManager.Instance.GetExperiencesForFile(new FileInfo("dummy" + ext));
        foreach (var expId in applicable.Where(id => id != "/" && viewerExperienceIds.Contains(id))
                                        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            list.Add(new DefaultActionCandidate(DefaultActionKind.InternalViewer, expId, Pretty(expId), "Internal viewer"));

        // ── External apps that match the extension.
        var entry = new FileSystemEntry { Name = "dummy" + ext, FullPath = @"C:\dummy" + ext, IsDirectory = false };
        foreach (var ca in ExternalAppRegistry.Instance.Resolve([entry]))
            if (!string.IsNullOrEmpty(ca.Definition.Id))
                list.Add(new DefaultActionCandidate(DefaultActionKind.ExternalApp, ca.Definition.Id, ca.DisplayName, "External app"));

        // ── Windows shell verbs registered for the type.
        var info = ShellTypeResolver.Resolve(ext);
        if (info is not null)
            foreach (var v in info.Verbs)
                list.Add(new DefaultActionCandidate(DefaultActionKind.WindowsVerb, v.Verb, "Windows: " + v.FriendlyName, "Windows"));

        return list;
    }

    /// <summary>"pdf" / ".pdf" / "*.pdf" → ".pdf"; "" when nothing usable.</summary>
    private static string NormalizeDotExt(string ext)
    {
        var e = ext.Trim();
        if (e.StartsWith("*.")) e = e[1..];          // "*.pdf" → ".pdf"
        else if (!e.StartsWith('.')) e = "." + e;    // "pdf"   → ".pdf"
        return e is "." ? string.Empty : e.ToLowerInvariant();
    }

    /// <summary>"/text/code" → "Text › Code"; a readable label without needing action instances.</summary>
    private static string Pretty(string experienceId) =>
        string.Join(" › ", experienceId.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]));
}
