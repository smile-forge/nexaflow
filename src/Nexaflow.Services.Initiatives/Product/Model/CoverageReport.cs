namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// The result of reconciling the <see cref="TestCoverageManifest"/> against the tree — a set of non-gating
/// <see cref="CoverageAdvisory"/> suggestions surfaced on the Integrity page. Deliberately separate from
/// <see cref="IntegrityReport"/> so these advisories never fail the installer's snaplink gate.
/// </summary>
public sealed class CoverageReport
{
    public string Generated { get; set; } = string.Empty;

    public List<CoverageAdvisory> Advisories { get; set; } = [];

    public int AddableCount => Advisories.Count(a => a.CanAdd);
    public int TotalCount => Advisories.Count;
    public bool IsClean => Advisories.Count == 0;
}
