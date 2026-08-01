namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>What the verification banner is currently saying.</summary>
public enum VerifyPhase
{
    /// <summary>No speculative rows — a literal query the index answered outright. Banner hidden.</summary>
    None,

    /// <summary>Candidates remain and need the user's say-so before more files are read.</summary>
    Prompt,

    /// <summary>The index had no answer, and a folder scan is being offered. A separate phase from
    /// <see cref="Prompt"/> because it offers a different action at a very different cost — verifying a
    /// handful of candidates is seconds, reading a directory tree is minutes.</summary>
    OfferScan,

    /// <summary>A folder scan is running and reporting hits as it finds them.</summary>
    Scanning,

    /// <summary>A refinement was typed against an empty result set, and running it as a fresh search in the
    /// same place is being offered instead.</summary>
    OfferNewSearch,

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

    // ── The folder scan ───────────────────────────────────────────────────────

    /// <summary>
    /// The offer made when the index came back with nothing. A scan reads every file in the tree and can
    /// run for minutes, so it is proposed rather than started — but it is proposed, because "no results"
    /// from an index that never covered this folder is not an answer to the user's question.
    /// <para>
    /// The two cases are worded apart on purpose: an index that searched and found nothing is real
    /// information ("probably isn't here"), while an index that couldn't be reached is none at all.
    /// </para>
    /// </summary>
    public static VerifyPlan OfferScan(SearchOrigin origin) =>
        new(VerifyPhase.OfferScan, 0, origin == SearchOrigin.IndexUnavailable
            ? "Windows Search isn't running, so nothing could be looked up. Scan this location manually (slow)?"
            : "The Windows indexer didn't find anything like that in this location — scan it manually (slow)?");

    /// <summary>
    /// The same offer, after a verification sweep has thrown every row away. The index DID return rows, so
    /// the "found nothing" wording above would contradict the count the user just watched appear — but the
    /// end state is identical: an empty list from a location the index may never have covered, and the
    /// scan is still the only thing that can settle it.
    /// </summary>
    public static VerifyPlan OfferScanAfterSweep(string originPrefix = "") =>
        new(VerifyPhase.OfferScan, 0,
            originPrefix + "None of them matched. Scan this location manually (slow)?");

    /// <summary>
    /// Offered when a refinement is typed at an empty result set. Narrowing nothing yields nothing, so
    /// taking the query at face value would answer with the same empty list and look like it ignored them.
    /// The likely intent is a new search in the same place, but that discards their previous query — so it
    /// is asked, not assumed.
    /// </summary>
    public static VerifyPlan OfferNewSearch(string query) =>
        new(VerifyPhase.OfferNewSearch, 0,
            $"There are no results here to search within. Run '{query}' as a new search in the same location?");

    /// <summary>Progress while a scan runs. Counts are what it has found so far, not a total — a scan
    /// can't know how many matches exist until it has finished looking.</summary>
    public static VerifyPlan Scanning(int found) =>
        new(VerifyPhase.Scanning, 0, found == 0
            ? "Scanning… no matches yet."
            : $"Scanning… {found} match{(found == 1 ? "" : "es")} so far.");

    /// <summary>
    /// What a finished scan says. Both qualifiers matter for the same reason: they turn a total into a
    /// floor. A <paramref name="cancelled"/> scan looked at part of the tree, and a
    /// <paramref name="truncated"/> one stopped collecting before the tree ran out — either way there may
    /// be more, and a bare count would say there isn't.
    /// </summary>
    public static VerifyPlan AfterScan(int found, bool cancelled = false, bool truncated = false) => new(
        VerifyPhase.Done, 0,
        cancelled
            ? $"Scan stopped — {found} match(es) found so far."
            : truncated
                ? $"Folder scan stopped at its {found}-row limit — narrow the search to see the rest."
                : found == 0
                    ? "Folder scan finished — no matches in this location."
                    : $"Folder scan found {found} match{(found == 1 ? "" : "es")}.");
}
