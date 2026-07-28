namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>What the verification banner is currently saying.</summary>
public enum VerifyPhase
{
    /// <summary>No speculative rows — a literal query the index answered outright. Banner hidden.</summary>
    None,

    /// <summary>Candidates remain and need the user's say-so before more files are read.</summary>
    Prompt,

    /// <summary>A sweep is in progress.</summary>
    Running,

    /// <summary>Everything that is going to be checked has been.</summary>
    Done,
}

/// <param name="Phase">Banner state.</param>
/// <param name="SweepNow">How many candidates to verify without asking. Zero means ask first.</param>
/// <param name="Banner">The line shown to the user.</param>
public readonly record struct VerifyPlan(VerifyPhase Phase, int SweepNow, string Banner);

/// <summary>
/// The decision behind the verification banner, kept pure so the threshold rule and its wording are
/// testable without an index, a UI thread or a background sweep. The ViewModel does the plumbing; this
/// decides what should happen.
/// </summary>
public static class VerificationPlanner
{
    /// <summary>Candidates verified without asking. Past this the sweep is opt-in — reading thousands of
    /// files off the back of a keystroke is the user's call, not ours.</summary>
    public const int AutoVerifyLimit = 50;

    /// <summary>
    /// How the rows were obtained, as a sentence prefix. The index and a folder scan cover different files
    /// and match on different things, so a count means little without saying which produced it.
    /// </summary>
    public static string OriginPrefix(SearchOrigin origin, int total, bool truncated = false)
    {
        var source = origin == SearchOrigin.FolderScan ? "Folder scan" : "Windows search index";

        // A capped result set is a different claim from a complete one: matches past the cap were never
        // considered, so "N files" without this reads as "that's all there was".
        return truncated
            ? $"{source} hit its {total}-file limit — narrow the search to be sure of the rest. "
            : $"{source} returned {total} file(s). ";
    }

    /// <summary>What to do with a freshly returned result set.</summary>
    public static VerifyPlan ForNewResults(
        int verified, int candidates, string originPrefix = "", int limit = AutoVerifyLimit)
    {
        if (candidates <= 0)
            return new(VerifyPhase.Done, 0, originPrefix + (verified == 0
                ? "No matches."
                : $"{verified} matched by name. Nothing else to check."));

        if (candidates <= limit)
            return new(VerifyPhase.Running, candidates,
                       originPrefix + $"Possible matches found — verifying {candidates}…");

        // Sweep a first slice regardless so the list is immediately useful, and ask about the tail rather
        // than either stalling on thousands of file reads or silently skipping them.
        return new(VerifyPhase.Prompt, limit,
                   originPrefix + $"{verified} matched by name. {candidates} more might match inside the file — check them?");
    }

    /// <summary>
    /// What to say once a sweep finishes. <paramref name="confirmed"/> is the count of rows actually
    /// proven — never the total row count, which includes rows still unsettled and reads as a far stronger
    /// claim than it is.
    /// <para>
    /// <paramref name="unreadable"/> rows were checked and couldn't be read (a .docx or .pdf has no text
    /// extractor yet). They are reported separately and never re-offered: asking to check them again is an
    /// action that cannot change anything, which is what makes a button look broken.
    /// </para>
    /// </summary>
    public static VerifyPlan AfterSweep(
        int confirmed, int stillPending, int unreadable = 0, int uncertain = 0, string originPrefix = "")
    {
        var notes = new List<string> { $"{confirmed} confirmed" };
        if (uncertain > 0)  notes.Add($"{uncertain} probable (found in a file type we can't read properly)");
        if (unreadable > 0) notes.Add($"{unreadable} couldn't be checked (text is compressed or encoded)");

        var summary = originPrefix + string.Join(" · ", notes);

        return stillPending == 0
            ? new(VerifyPhase.Done, 0, summary + ".")
            : new(VerifyPhase.Prompt, 0,
                  $"{summary} · {stillPending} more might match inside the file — check them?");
    }

    /// <summary>What to say when the user declines the rest.</summary>
    public static VerifyPlan AfterSkip(int confirmed, int unchecked_) =>
        new(VerifyPhase.Done, 0, $"{confirmed} confirmed · {unchecked_} unchecked.");
}
