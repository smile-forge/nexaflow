using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>
/// A clickable count tile shown on the <em>product root</em>, occupying the slot a real node uses for its
/// PROPERTIES panel. Deliberately open-ended: integrity is the first of these, and another metric is added
/// simply by appending one more widget to <c>ProductViewModel.RootWidgets</c>.
/// </summary>
public partial class CountWidget(string icon, string label, int count, Action open) : ObservableObject
{
    public string Icon { get; } = icon;
    public string Label { get; } = label;

    [ObservableProperty] private int _count = count;

    /// <summary>A non-zero count reads as a problem; zero is the calm, healthy state.</summary>
    public Brush CountBrush => StatusInfo.Res(Count > 0 ? "DangerBrush" : "TextMutedBrush");

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(CountBrush));

    [RelayCommand] private void Open() => open();
}

/// <summary>
/// A target a snaplink could legitimately point at inside the currently-named document: a markdown heading
/// path, or a class / one of its methods. Offered so a link is re-pointed by <em>picking</em> a real target
/// rather than retyping a class name and hoping.
/// </summary>
/// <param name="Label">What the user sees (the heading, or the member signature).</param>
/// <param name="Depth">Nesting level (heading level, or member-under-type), so the list reads as an outline.</param>
public sealed record TargetChoice(
    string Label,
    int Depth,
    string? Class = null,
    string? Method = null,
    IReadOnlyList<string>? TitlePath = null);

/// <summary>
/// A row in the Integrity list — either a broken snaplink (<see cref="IntegrityIssueItem"/>) or a coverage
/// suggestion (<see cref="CoverageAdvisoryItem"/>). One list, one count, one filter; they differ only in the
/// badge colour (<see cref="KindBrush"/>) and what the detail pane offers (repair vs add).
/// </summary>
public interface IIntegrityRow
{
    string NodeId { get; }
    string NodeTitle { get; }
    /// <summary>Badge one — "node" or the concern tag the row pertains to.</summary>
    string Scope { get; }
    /// <summary>Badge two — the short kind ("missing file", "not linked", …).</summary>
    string Kind { get; }
    /// <summary>The sub-line under the title.</summary>
    string Detail { get; }
    /// <summary>True for a coverage suggestion (non-gating), false for a broken snaplink (gating).</summary>
    bool IsAdvisory { get; }
    /// <summary>Colour of the Kind badge — danger for a broken link, accent for a suggestion.</summary>
    Brush KindBrush { get; }
    /// <summary>The text the free-text filter matches against.</summary>
    string FilterText { get; }
}

/// <summary>
/// One broken snaplink in the Integrity page: the diagnosis, plus the link's target as <em>editable</em>
/// fields so it can be re-pointed in place.
/// </summary>
/// <remarks>
/// <see cref="Live"/> is the actual <see cref="Snaplink"/> instance inside the loaded tree — editing writes
/// straight through to it. Rows restored from a saved report that could not be re-bound to a live instance
/// (the tree changed underneath) have a null <see cref="Live"/> and are read-only until the rescan lands,
/// because writing to a detached copy would silently lose the edit.
/// </remarks>
public partial class IntegrityIssueItem : ObservableObject, IIntegrityRow
{
    public bool IsAdvisory => false;
    public Brush KindBrush => StatusInfo.Res("DangerBrush");
    public string FilterText => $"{NodeTitle} {Scope} {Kind} {Detail} {Doc}";

    public IntegrityIssue Issue { get; }
    public Snaplink? Live { get; }

    public IntegrityIssueItem(IntegrityIssue issue, Snaplink? live)
    {
        Issue = issue;
        Live  = live;

        var link   = live ?? issue.Link;
        _doc       = link.Doc ?? string.Empty;
        _class     = link.Class ?? string.Empty;
        _method    = link.Method ?? string.Empty;
        _titlePath = link.TitlePath is { Count: > 0 } tp ? string.Join(TitlePathSeparator, tp) : string.Empty;
        _target    = link.Target ?? string.Empty;

        SetDiagnosis(issue.Kind, issue.Detail);
    }

    /// <summary>Heading paths are edited as a single line; this is what splits/joins the levels.</summary>
    public const string TitlePathSeparator = " > ";

    public string NodeId    => Issue.NodeId;
    public string NodeTitle => Issue.NodeTitle;

    /// <summary>"node" for a node attachment, otherwise the concern tag the link hangs off.</summary>
    public string Scope => Issue.Scope;

    public string LinkType => (Live ?? Issue.Link).Type;
    public bool IsUrl      => LinkType == "url";
    public bool IsMarkdown => LinkType == "markdown";
    public bool IsCode     => !IsUrl && !IsMarkdown;

    /// <summary>False for a row restored from a stale report — actions stay disabled rather than lose an edit.</summary>
    public bool CanEdit => Live is not null;

    // The diagnosis refreshes in place when an edit is applied and the link is re-checked.
    [ObservableProperty] private string _kind = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;

    // Editable target fields (only the ones this link type actually uses are shown).
    [ObservableProperty] private string _doc;
    [ObservableProperty] private string _class;
    [ObservableProperty] private string _method;
    [ObservableProperty] private string _titlePath;
    [ObservableProperty] private string _target;

    /// <summary>Copies the edited fields onto the live link. No-op for a detached row.</summary>
    public void WriteBack()
    {
        if (Live is null) return;
        Live.Doc    = Blank(Doc);
        Live.Class  = Blank(Class);
        Live.Method = Blank(Method);
        Live.Target = Blank(Target);
        Live.TitlePath = string.IsNullOrWhiteSpace(TitlePath)
            ? null
            : [.. TitlePath.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    public void SetDiagnosis(IntegrityKind kind, string detail)
    {
        Kind   = KindLabel(kind);
        Detail = detail;
    }

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string KindLabel(IntegrityKind kind) => kind switch
    {
        IntegrityKind.MissingDoc     => "no path",
        IntegrityKind.MissingFile    => "missing file",
        IntegrityKind.WorktreePath   => "worktree path",
        IntegrityKind.MissingHeading => "missing heading",
        IntegrityKind.MissingClass   => "missing class",
        IntegrityKind.MissingMethod  => "missing method",
        IntegrityKind.EmptyTarget    => "empty target",
        IntegrityKind.InvalidUrl     => "invalid url",
        IntegrityKind.MissingSnaplink => "no snaplink",
        _                            => kind.ToString()
    };
}

/// <summary>
/// A non-gating coverage suggestion in the Integrity list: a test declares it covers a node (via
/// <c>[CoversNode]</c>) but the tree has no matching <c>tests</c> snaplink yet, or the declared id is stale.
/// A <see cref="CanAdd"/> advisory turns into a real snaplink with one click; the tree stays authoritative.
/// </summary>
public sealed class CoverageAdvisoryItem(CoverageAdvisory advisory) : IIntegrityRow
{
    public CoverageAdvisory Advisory { get; } = advisory;

    public bool IsUnknownNode => Advisory.Kind == CoverageAdvisoryKind.UnknownNode;

    public string NodeId    => Advisory.NodeId;
    public string NodeTitle => IsUnknownNode ? $"unknown node: {Advisory.NodeId}" : Advisory.NodeTitle;
    public string Scope     => "tests";
    public string Kind      => IsUnknownNode ? "stale id" : "not linked";

    /// <summary>The declaring test — <c>Class.Method</c>, or just the class for a class-level declaration.</summary>
    public string TestDisplay => string.IsNullOrEmpty(Advisory.Method)
        ? CoverageAdvisory.SimpleName(Advisory.Class)
        : $"{CoverageAdvisory.SimpleName(Advisory.Class)}.{Advisory.Method}";

    public string File => Advisory.File.Length > 0 ? Advisory.File : "(source file unresolved)";

    /// <summary>Sub-line: the declaring test and its file.</summary>
    public string Detail => $"{TestDisplay}  ·  {File}";

    public bool CanAdd => Advisory.CanAdd;

    public bool IsAdvisory => true;
    public Brush KindBrush => StatusInfo.Res(CanAdd ? "AccentBrush" : "WarningBrush");
    public string FilterText => $"{NodeTitle} {Scope} {Kind} {TestDisplay} {File}";
}
