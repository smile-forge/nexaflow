namespace Nexaflow.Markdown.Ast;

/// <summary>
/// What a language says about a body it holds in another language (<see cref="Kinds.Nested"/>) when something is written inside
/// it: whether the body still ends where it did. Only the language holding the body knows what would end it — a closing fence, a
/// quote, a line a container no longer holds — so only it can answer (R9).
///
/// <para>
/// <strong>Where it holds, the holder is not read again.</strong> What was written is the body's, and only the reading of the body
/// changes: the unit holding it keeps its tree, the body standing for a few more or fewer characters, and nothing it holds is read
/// again. Where the language cannot vouch for it — or says nothing at all — the unit is read again, as any change is.
/// </para>
/// </summary>
public interface IBodyBounds
{
    /// <summary>
    /// Whether <paramref name="body"/>, in <paramref name="holder"/>, still ends where it did once <paramref name="written"/> has been
    /// written somewhere inside the characters it holds in its own language.
    /// </summary>
    /// <param name="holder">The piece holding the body: its delimiters, as they stand.</param>
    /// <param name="body">The body, as it stood before the change.</param>
    /// <param name="written">What was written in place of what was there.</param>
    /// <param name="lines">Every line the change touched, whole, as they read afterwards — line breaks between them, none after the last.</param>
    bool Holds(ContentNode holder, ContentNode body, string written, string lines);
}
