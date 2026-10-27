using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Markdown.Scores;

/// <summary>
/// What a score is engraved into, and what measures what it engraves: the pieces of the tree it is laid as, the few kinds of ink
/// a score is drawn in, and how wide a glyph or a word comes out — everything the engraver needs of whoever draws the score, so
/// the engraving is the same whoever that is.
/// </summary>
/// <remarks>Every point and rectangle is in the frame of the piece open when it is said; <see cref="Anchor"/> is where that is.</remarks>
internal interface IScoreLayer
{
    // ── Pieces ──────────────────────────────────────────────────────────────

    /// <summary>Opens a piece at <paramref name="at"/> inside whatever is open, and gives back where it went.</summary>
    int Open(string kind, ISourcePart? part, LayoutPoint at);

    void Close();

    /// <summary>Where the piece being built is anchored on the page.</summary>
    LayoutVector Anchor { get; }

    /// <summary>How far the piece being built has reached so far, in its own frame.</summary>
    LayoutRect Reached { get; }

    /// <summary>Says the piece being built reserves exactly this much of its line, vertically.</summary>
    void Reserves(double top, double height);

    /// <summary>Says the piece being built reaches at least this far.</summary>
    void Covers(LayoutRect what);

    /// <summary>Declares that these pieces read together, in this order.</summary>
    void Runs(IReadOnlyList<int> members, bool vertical);

    /// <summary>Finishes the score, and says how far its root reaches.</summary>
    LayoutRect Seal();

    // ── Ink ─────────────────────────────────────────────────────────────────

    /// <summary>A Bravura glyph with its baseline at <paramref name="baseline"/> — or false, drawing nothing, where there is no font.</summary>
    bool Glyph(int codepoint, LayoutPoint baseline, double staffSpace, double scale);

    /// <summary>A filled rectangle: a staff line, a ledger, a bar line, a beam of graces.</summary>
    void Rule(LayoutRect box);

    /// <summary>A filled box drawn as an outline — a stem.</summary>
    void Box(LayoutRect box);

    /// <summary>A filled box in the score's own ink rather than whatever the theme hands it — the line under a held word.</summary>
    void Inked(LayoutRect box);

    /// <summary>A filled round dot in a box — a repeat's.</summary>
    void Dot(LayoutRect box);

    /// <summary>A hollow ring, stroked — what stands for a glyph where there is no font to draw it.</summary>
    void Ring(LayoutPoint centre, double radiusX, double radiusY, double thickness);

    /// <summary>A filled four-cornered shape — a beam, which leans.</summary>
    void Polygon(LayoutPoint first, LayoutPoint second, LayoutPoint third, LayoutPoint fourth);

    /// <summary>A tie's or a slur's crescent: out to <paramref name="to"/> bowing through <paramref name="bow"/>, and back bowing through <paramref name="back"/>.</summary>
    void Crescent(LayoutPoint from, LayoutPoint bow, LayoutPoint to, LayoutPoint back);

    /// <summary>A stroked line — an acciaccatura's slash.</summary>
    void Stroke(LayoutPoint from, LayoutPoint to, double thickness);

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>Words set in the face a score sets them in.</summary>
    IScoreWords Words(string text, double size, bool semibold = false, bool italic = false);

    /// <summary>A chord symbol: its letters in the chord face, its accidentals as the signs they mean.</summary>
    IScoreWords Chord(string text, double size);

    /// <summary>Words already set, drawn with their top-left at <paramref name="at"/>.</summary>
    void Text(IScoreWords words, LayoutPoint at);

    /// <summary>
    /// A line of prose as a piece of the score — set in its room and aligned in it, a piece per letter standing for what was
    /// written, so it can be selected through.
    /// </summary>
    void Prose(IScoreWords words, LayoutPoint at, double room, LayoutAlign align, ISourcePart? part, string kind,
               IReadOnlyList<ISourcePart>? letters);

    // ── Measures ────────────────────────────────────────────────────────────

    /// <summary>How far along a glyph moves what follows it.</summary>
    double Advance(int codepoint, double staffSpace, double scale = 1.0);

    /// <summary>How wide words are set, measured rather than drawn.</summary>
    double Width(string text, double size);
}

/// <summary>Words a layer set for a score: how big they came out, and where their letters sit in that.</summary>
internal interface IScoreWords
{
    double Width { get; }

    double Height { get; }

    /// <summary>Where the letters sit inside the words' box — top of a capital to the bottom of a descender, leading left out.</summary>
    (double Top, double Height) Letters { get; }
}
