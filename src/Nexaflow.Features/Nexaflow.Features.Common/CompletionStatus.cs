namespace Nexaflow.Features.Common;

/// <summary>
/// Shared four-state status vocabulary for "should this hold?" checklists — the Product tree's concern
/// status and the Projects "completion criteria" list use the same set. Colours resolve from the shared
/// app-theme <c>Status.*</c> tokens (Tokens.xaml). Serialized by member name.
/// </summary>
public enum CompletionStatus
{
    /// <summary>Intended but not yet satisfied — aspirational.</summary>
    Should,

    /// <summary>Deliberately excluded / out of scope.</summary>
    Shouldnt,

    /// <summary>Complete.</summary>
    Done,

    /// <summary>Broken / regressed.</summary>
    Faulted,
}
