namespace Nexaflow.Features.Common.Dependencies;

/// <summary>How badly the declaring feature needs the component.</summary>
public enum ExternalDependencyKind
{
    /// <summary>The feature cannot do its core job without it.</summary>
    Required,

    /// <summary>The feature works without it, but something is degraded or unavailable.</summary>
    Optional,
}

/// <summary>Whether the component was found on this machine.</summary>
public enum ExternalDependencyState
{
    /// <summary>Found, and usable.</summary>
    Present,

    /// <summary>Definitively absent.</summary>
    Missing,

    /// <summary>The probe could not decide — it threw, or the component can't be detected without using it.</summary>
    Unknown,
}

/// <summary>The result of one <see cref="IExternalDependency.Probe"/>.</summary>
/// <param name="State">Found, absent, or undecidable.</param>
/// <param name="DetectedVersion">Version string when the probe can read one; null otherwise.</param>
/// <param name="Detail">Optional extra line for the user — a path, or why the probe couldn't decide.</param>
public sealed record ExternalDependencyStatus(
    ExternalDependencyState State,
    string? DetectedVersion = null,
    string? Detail = null)
{
    /// <summary>The answer for a component nothing has declared, or a probe that hasn't run yet.</summary>
    public static ExternalDependencyStatus Unknown(string? detail = null)
        => new(ExternalDependencyState.Unknown, null, detail);
}

/// <summary>
/// Optional, reflection-discovered hook that lets a feature declare a THIRD-PARTY COMPONENT it needs
/// present on the machine — an external runtime (the Edge WebView2 runtime), a native library shipped
/// beside the app (libvlc), or a command-line tool on PATH (<c>dotnet</c>). Declared exactly like an
/// <see cref="IThemeContribution"/>: Core finds it by reflection across the feature assemblies, so
/// neither side references the other.
///
/// <para>
/// Two things come out of declaring one. First, Options → About lists every declared component and
/// whether it is actually present, so "why doesn't X work on this PC" is answerable without reading a
/// log. Second, a feature can ask
/// <see cref="Nexaflow.Features.Common.IShellServices.GetDependencyStatus(string)"/> before it starts
/// using the component, and say something precise instead of failing deeper in.
/// </para>
///
/// <para>
/// More than one feature may declare the SAME <see cref="Id"/> — WebView2 is needed by both the PDF
/// reader and the Web tab. The registry keys on the id and merges the declaring features into one row,
/// so About shows a single entry naming everything that wants it.
/// </para>
/// </summary>
public interface IExternalDependency
{
    /// <summary>
    /// Stable, lowercase, hyphenated identity for the component itself (e.g. <c>webview2-runtime</c>) —
    /// NOT for the feature declaring it. Two features naming the same component must use the same id.
    /// </summary>
    string Id { get; }

    /// <summary>Name as the user would recognise it, e.g. "Microsoft Edge WebView2 Runtime".</summary>
    string DisplayName { get; }

    /// <summary>One sentence on what stops working without it. Shown under the name on the About page.</summary>
    string Description { get; }

    /// <summary>Whether the declaring feature is broken or merely degraded without it.</summary>
    ExternalDependencyKind Kind { get; }

    /// <summary>Where the user can get it, when that's a meaningful thing to offer. Null otherwise.</summary>
    string? InstallUrl { get; }

    /// <summary>
    /// Looks for the component. Called off the UI thread and may be slow-ish (a PATH walk, a registry
    /// read); it must not show UI. Throwing is tolerated — the registry reports
    /// <see cref="ExternalDependencyState.Unknown"/> — but returning a status is better than throwing.
    /// Implementations must expose a public parameterless constructor.
    /// </summary>
    ExternalDependencyStatus Probe();
}
