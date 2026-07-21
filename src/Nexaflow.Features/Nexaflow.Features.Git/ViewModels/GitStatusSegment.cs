namespace Nexaflow.Features.Git.ViewModels;

/// <summary>
/// The semantic meaning of one segment of the inline status line. The view-model decides the <em>meaning</em>
/// (this count is a problem / this state is good) and the view resolves the tone to a theme brush at render
/// time — so the wording and severity are unit-testable without WPF, and no colour is decided in the VM.
/// </summary>
public enum GitTone
{
    /// <summary>Ordinary text — the default foreground.</summary>
    Normal,
    /// <summary>De-emphasised: separators, ahead/behind arrows, "pushed".</summary>
    Muted,
    /// <summary>A good state — clean tree, staged work, merged branch.</summary>
    Good,
    /// <summary>Needs attention but isn't broken — modified files, unmerged, unpushed.</summary>
    Caution,
    /// <summary>An error state — a broken worktree remnant, a failed action.</summary>
    Bad,
}

/// <summary>One run of the inline status line: its text and what that text <em>means</em>.</summary>
/// <seealso cref="GitTone"/>
public sealed record GitStatusSegment(string Text, GitTone Tone);
