using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.WindowsSearch;

/// <summary>One row returned from the Windows Search index.</summary>
public sealed partial class SearchResultEntry : ObservableObject
{
    public required string    FilePath  { get; init; }
    public required string    FileName  { get; init; }
    /// <summary>Directory relative to the search root.</summary>
    public required string    Directory { get; init; }
    public          long?     SizeBytes { get; init; }
    public          DateTime? Modified  { get; init; }
    public          string    Kind      { get; init; } = string.Empty;

    /// <summary>
    /// Whether this row is proven. Observable because a content-regex search shows rows the moment the
    /// index returns them and settles each one afterwards — the row's overlay flips from "?" to a tick (or
    /// to struck-through) in place, rather than the list rebuilding under the user.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnverified))]
    [NotifyPropertyChangedFor(nameof(IsRejected))]
    [NotifyPropertyChangedFor(nameof(IsUnreadable))]
    [NotifyPropertyChangedFor(nameof(IsUncertain))]
    private SearchHitState _state = SearchHitState.Verified;

    /// <summary>Queued for checking — the badge shows it hasn't been looked at yet.</summary>
    public bool IsUnverified => State == SearchHitState.Candidate;

    /// <summary>Drives the struck-through styling before the row is removed at the end of the pass.</summary>
    public bool IsRejected => State == SearchHitState.Rejected;

    /// <summary>Couldn't be settled either way — the badge says so rather than implying a miss.</summary>
    public bool IsUnreadable => State == SearchHitState.Unreadable;

    /// <summary>Found, but in a file type we can't read properly — a real hit worth eyeballing.</summary>
    public bool IsUncertain => State == SearchHitState.Uncertain;

    public bool IsFolder => Kind.Contains("folder", StringComparison.OrdinalIgnoreCase);

    public string SizeDisplay => SizeBytes is > 0 ? SizeFormatter.FormatBytes(SizeBytes.Value) : string.Empty;

    public string ModifiedDisplay => Modified?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
}
