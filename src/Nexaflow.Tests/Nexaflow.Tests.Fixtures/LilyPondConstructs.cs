namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// Every construct the LilyPond reader knows, each in a small piece of music — the counterpart to
/// <see cref="AbcConstructs"/>, and asked the same three questions: does it read back as it was written, does
/// every stage leave it alone, and does every piece of the picture name a real part of it.
///
/// <para>
/// Fragments rather than whole files where a fragment is what somebody writes. LilyPond is not line-oriented
/// and needs no header, so <c>{ c4 d e f }</c> is a complete piece of music; the multi-staff and lyric entries
/// are whole scores because that is the only way to write those.
/// </para>
/// </summary>
public static class LilyPondConstructs
{
    public static readonly (string What, string Ly)[] Everything =
    [
        ("the smallest piece there is",
            "{ c4 d e f }"),

        ("plain pitches across every octave",
            "{ c,, c, c c' c'' c''' b a' g'' }"),

        ("relative entry, from a start and from none",
            @"\relative c' { c4 g c g, } \relative { c'4 b a g }"),

        ("fixed entry",
            @"\fixed c' { c4 g c' }"),

        ("Dutch note names, contracted and doubled",
            "{ cis4 ces cisis ceses as es aes ees ases bes fis }"),

        ("durations from a breve to a sixty-fourth, dotted and scaled",
            @"{ c\breve c1 c2 c4 c8 c16 c32 c64 c4. c4.. c2*2 c8*2/3 }"),

        ("rests: plain, whole-bar and invisible",
            @"{ r4 r2. R1 R1*3 s2 s1*2 \skip 4 }"),

        ("chords, tied, repeated and forced",
            "{ <c e g>4 <c e g>~ q <g c' e'>2 <c! e?>4 }"),

        ("meter, key and clef",
            @"{ \time 3/4 \key g \major \clef bass c4 d e \time 2,2,3 7/8 \key es \minor \clef ""treble_8"" f4 }"),

        ("bar checks, bar lines and a pickup",
            @"{ \partial 4 g4 | c1 | c \bar ""||"" c \bar "".|:"" c \bar "":|."" c \bar ""|."" }"),

        ("repeats and alternatives",
            @"\relative c'' { \repeat volta 2 { c4 d e f } \alternative { { g1 } { a1 } } \repeat unfold 2 { c4 d } }"),

        ("endings numbered by volta, as LilyPond writes them since 2.24",
            @"\relative c'' { \repeat volta 3 { c4 d e f } \alternative { \volta 1,2 { g1 } \volta 3 { a1 } } }"),

        ("tuplets both ways round",
            @"\relative c'' { \tuplet 3/2 { c8 d e } \times 2/3 { f g a } \tuplet 3/2 4 { b c d e f g } }"),

        ("ties, slurs and phrasing slurs",
            @"\relative c'' { c4( d e f) | c2~ c | c4\( d( e) f\) }"),

        ("manual beams and the meter's",
            @"{ c8[ d e] f[ g a b c] \autoBeamOff c8 d \autoBeamOn }"),

        ("articulations, spelled and named",
            @"{ c4-. d-> e-- f-^ g-_ a-! b-+ c'^. d'_> | c\staccato d\fermata e\trill f\upbow g\downbow }"),

        ("text and marks put in a direction",
            @"{ c4^""Fine"" d_""dolce"" e-\markup { \italic ""rit."" } f^\fermata }"),

        ("fingerings and dynamics",
            @"{ c4-1 d-3 e\f f\p g\< a b\! c\mf }"),

        ("grace notes of every kind",
            @"\relative c'' { \grace d8 c4 \grace { d16 e } c4 \acciaccatura d8 c4 \appoggiatura d8 c4 \slashedGrace e8 d4 }"),

        ("a transposition",
            @"\transpose c d { c4 e g c' }"),

        ("definitions, plain and quoted, and their use",
            "global = { \\time 4/4 \\key c \\major }\nmelody = \\relative c'' { \\global c4 d e f }\n\"words1V1\" = \\lyricmode { one two three four }\n{ \\melody }\n{ \\\"words1V1\" }"),

        ("a header, with a markup and Scheme",
            "\\header {\n  title = \"Speed the Plough\"\n  composer = \\markup { \\bold \"Trad.\" }\n  tagline = ##f\n}\n{ c1 }"),

        ("staves in a score, named and bracketed",
            "\\score {\n  \\new ChoirStaff <<\n    \\new Staff \\with { instrumentName = \"Soprano\" } \\relative c'' { g4 a b c }\n    \\new Staff = \"low\" { \\clef bass g,4 fis, e, d, }\n  >>\n  \\layout { }\n}"),

        ("lyrics added, and lyrics to a named voice",
            "<<\n  \\new Staff \\new Voice = \"melody\" \\relative c' { c4 d e f | g1 }\n  \\new Lyrics \\lyricsto \"melody\" { Twin -- kle, lit -- tle __ _ star. }\n>>\n\\relative c' { c4 d e f } \\addlyrics { \"1. One\" two Ly4 -- rics4. }"),

        ("chord names",
            "<<\n  \\new ChordNames \\chordmode { c1 | a1:m | d2:m7 g2:7 | s4 bes2.:maj7/f }\n  \\new Staff { c'1 a' d' g' }\n>>"),

        ("two voices on one staff",
            @"\new Staff { << { c'4 d' e' f' } \\ { a4 b c' d' } >> }"),

        ("tempo, marks and section labels",
            @"{ \tempo ""Allegro"" 4 = 120 c4 \mark \default d \sectionLabel ""Chorus"" e \tempo 4 = 100-120 f \break }"),

        ("settings a score carries along",
            @"{ \set Staff.instrumentName = ""Flute"" \override NoteHead.color = #red \once \override Staff.TimeSignature.break-visibility = ##f \omit Staff.TimeSignature \numericTimeSignature \cadenzaOn c4 \cadenzaOff }"),

        ("comments of both kinds, and Scheme",
            "% a line comment with c4 in it\n#(set-global-staff-size 20)\n{ c4 d %{ e f %} g #'left a }"),

        ("a file of definitions with no score",
            "cf = \\relative { \\clef bass c4 c' b a | g a f d }\nupper = \\relative c'' { r4 s4 s2 | s1*2 \\bar \"||\" }"),

        ("tremolos, forced accidentals and quarter-note names left as words",
            "{ c4:32 c!8 c?8 cih4 }"),
    ];
}
