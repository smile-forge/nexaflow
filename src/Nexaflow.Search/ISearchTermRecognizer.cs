namespace Nexaflow.Search;

/// <summary>
/// Claims a token as a term kind the base syntax doesn't know about. Passed in by whoever is parsing, so a
/// search surface only understands the term kinds it can actually honour.
/// <para>
/// Filename globs are the reason this exists: they mean something to a file browser and to a file-index
/// search, and nothing at all to a page searching its own single body. Baking glob recognition into the
/// parser would hand a text viewer a <c>*.txt</c> term it can only ignore or fail on — so recognition is
/// contributed, not built in.
/// </para>
/// </summary>
public interface ISearchTermRecognizer
{
    /// <summary>
    /// The term this token represents, or null to leave it to the next recognizer (and ultimately to the
    /// default plain-text reading). Never sees a delimited <c>/regex/</c> — those are the base syntax's.
    /// </summary>
    SearchTerm? Recognize(string token);
}


