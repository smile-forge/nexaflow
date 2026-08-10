using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.WindowsApps.Models;

/// <summary>
/// One optional package installed alongside a Store app — what Windows calls "app add-ons &amp;
/// downloadable content". Carries its own <see cref="PackageFullName"/> so a single add-on can be
/// removed without touching the app it extends.
/// </summary>
public sealed record AppAddOn(
    string Name,
    string? Publisher,
    string? Version,
    string PackageFullName,
    long? SizeBytes)
{
    public string SizeText => SizeBytes is { } b && b > 0 ? SizeFormatter.FormatBytes(b) : "—";

    /// <summary>Publisher + version on one muted line under the add-on name; blank when neither is known.</summary>
    public string Subtitle => string.Join(" · ",
        new[] { Publisher, Version }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
