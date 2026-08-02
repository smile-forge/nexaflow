using Nexaflow.Search;

namespace Nexaflow.Features.Common.Search;

/// <summary>
/// How sure we are that a hit really matches. Most backends match exactly and only ever produce
/// <see cref="Verified"/> — this exists for the file index, which can narrow by name but cannot evaluate a
/// regex against file contents, so it returns fast "might match" rows that a background pass then settles.
/// </summary>
public enum SearchHitState
{
    /// <summary>Proven to match. The default: a backend that evaluates the query itself never needs the
    /// other states, so existing pages keep showing plain results with no verification affordance.</summary>
    Verified,

    /// <summary>Might match — the index returned it, but the query couldn't be fully evaluated against it
    /// yet (typically a regex that must be checked against file contents).</summary>
    Candidate,

    /// <summary>Checked and does not match. Kept rather than dropped so the list settles visibly instead of
    /// rows silently vanishing under the user's cursor.</summary>
    Rejected,

    /// <summary>
    /// The pattern <em>was</em> found, but inside a file whose format we don't understand — the bytes were
    /// scanned as raw text rather than decoded properly. It is a real hit on the file's contents; it just
    /// might be incidental, the way a plain-text scan of a JPEG finds "magic" in a camera tag rather than
    /// in anything the user would call the document's text. Shown as a match the user should eyeball.
    /// </summary>
    Uncertain,

    /// <summary>
    /// Checked, and we genuinely can't tell — the file couldn't be opened, or nothing was found in a
    /// format whose real text is compressed or encoded (a <c>.docx</c> is a ZIP, so its words are simply
    /// not in the raw bytes and their absence proves nothing). Distinct from <see cref="Rejected"/>, which
    /// is a conclusive miss, and from <see cref="Candidate"/>, which re-checking could still settle —
    /// re-reading this file produces the same answer, so it is never re-offered.
    /// </summary>
    Unreadable,
}
