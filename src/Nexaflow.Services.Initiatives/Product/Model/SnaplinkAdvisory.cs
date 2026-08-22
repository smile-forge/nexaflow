namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>Why a snaplink advisory was raised.</summary>
public enum SnaplinkAdvisoryKind
{
    /// <summary>The link carries an <c>ast</c> that no longer resolves in the file it points at — or never
    /// did, because nothing has ever checked the field.</summary>
    UnresolvedAst,
}

/// <summary>
/// A <em>non-gating</em> problem with a snaplink. Unlike an <see cref="IntegrityIssue"/>, which fails a release
/// build, an advisory is a suggestion: the link still resolves to its file and class, only its finer
/// <see cref="Snaplink.Ast"/> target is wrong.
/// <para>
/// The distinction matters because <see cref="Snaplink.Ast"/> has never been validated by anything, so the
/// field drifted into free text — "Mic button", "ROW 4 — AI INTERACTION BAR". Reporting that as breakage would
/// fail every release build at once for links whose real target is perfectly sound; reporting it as an advisory
/// with a suggested replacement turns it into a burn-down instead.
/// </para></summary>
public sealed class SnaplinkAdvisory
{
    public SnaplinkAdvisoryKind Kind { get; set; }

    public string NodeId { get; set; } = string.Empty;
    public string NodeTitle { get; set; } = string.Empty;

    /// <summary>The concern this link hangs off, or null when it is on the node itself.</summary>
    public string? Concern { get; set; }

    /// <summary>Index within that snaplink list — the handle <c>nfi set-snaplink --index</c> takes.</summary>
    public int Index { get; set; }

    /// <summary>The file the link points at.</summary>
    public string Doc { get; set; } = string.Empty;

    /// <summary>The <c>ast</c> value that does not resolve.</summary>
    public string Current { get; set; } = string.Empty;

    /// <summary>The closest real structure path in that file, when one is close enough to be worth offering.</summary>
    public string? Suggestion { get; set; }

    /// <summary>The fix, ready to run.</summary>
    public string Command =>
        $"nfi set-snaplink {NodeId} --index {Index}"
        + (Concern is { Length: > 0 } c ? $" --concern {c}" : "")
        + (Suggestion is { Length: > 0 } s ? $" --ast \"{s}\"" : " --clear ast");
}
