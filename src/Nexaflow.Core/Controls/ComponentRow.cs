using System.Windows.Media;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Controls;

/// <summary>
/// One "System components" row on the About page: an <see cref="ExternalDependencyReport"/> flattened into
/// exactly the strings and the brush the template binds.
/// <para>
/// The presentation choices live here rather than in the report so the contract assembly stays free of WPF,
/// and so the status brush is resolved from the active theme at build time — a feature-owned literal colour
/// is never acceptable, and a converter would have to reach for the same resources anyway.
/// </para>
/// </summary>
public sealed class ComponentRow
{
    public ComponentRow(ExternalDependencyReport report, Func<string, Brush?> resolveBrush)
    {
        DisplayName = report.DisplayName;
        Description = report.Description;
        InstallUri  = report.InstallUrl;

        (Glyph, StatusLabel, var brushKey) = report.Status.State switch
        {
            ExternalDependencyState.Present => ("●", Installed(report.Status.DetectedVersion), "SuccessBrush"),
            ExternalDependencyState.Missing => ("●", report.Kind == ExternalDependencyKind.Required
            ? Str.Get("Shell.Components.Missing")
            : Str.Get("Shell.Components.NotInstalled"),
                                                report.Kind == ExternalDependencyKind.Required
                                                        ? "DangerBrush"
                                                        : "WarningBrush"),
            _                               => ("○", Str.Get("Shell.Components.Unknown"), "TextMutedBrush"),
        };

        Brush = resolveBrush(brushKey) ?? resolveBrush("TextMutedBrush") ?? Brushes.Gray;

        Provenance = BuildProvenance(report);
    }

    public string  DisplayName { get; }
    public string  Description { get; }
    public string  Glyph       { get; }
    public string  StatusLabel { get; }
    public Brush   Brush       { get; }
    public string  Provenance  { get; }
    public string? InstallUri  { get; }

    public bool   HasInstallLink  => !string.IsNullOrWhiteSpace(InstallUri);
    public string InstallLinkText => Str.Format("Shell.Components.GetFormat", DisplayName);

    private static string Installed(string? version)
        => string.IsNullOrWhiteSpace(version)
            ? Str.Get("Shell.Components.Installed")
            : Str.Format("Shell.Components.InstalledFormat", version);

    /// <summary>
    /// "Required by PDF, Web" plus whatever the probe wanted to add (a path, or why it couldn't decide).
    /// The requiring features matter more than they look: they are how a user maps an unfamiliar component
    /// name onto the part of the app that stopped working.
    /// </summary>
    private static string BuildProvenance(ExternalDependencyReport report)
    {
        var parts = new List<string>();

        if (report.RequiredBy.Count > 0)
            parts.Add(report.Kind == ExternalDependencyKind.Required
                ? Str.Format("Shell.Components.RequiredByFormat", report.RequiredByLabel)
                : Str.Format("Shell.Components.UsedByFormat", report.RequiredByLabel));
        if (!string.IsNullOrWhiteSpace(report.Status.Detail)) parts.Add(report.Status.Detail!);

        return string.Join(" · ", parts);
    }
}
