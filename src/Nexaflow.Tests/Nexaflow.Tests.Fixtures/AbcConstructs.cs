namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// Every construct the ABC reader knows, gathered into a handful of tunes — the standing, cheap version
/// of the ten-thousand-tune corpus sweep, and the record of what is supported.
///
/// <para>
/// Shared rather than kept beside whichever suite wrote it down first, because three things ask
/// questions of the same list and they must be asking about the same constructs: the parse-tree tests
/// (does reading it and writing it back give exactly what was read?), the pipeline tests (does every
/// stage leave the source alone?) and the layout tests (does every piece of the picture name a real part
/// of the source?). A construct added to one and not the others would look covered and not be.
/// </para>
/// <para>
/// Each entry is a whole tune rather than a fragment, because ABC is line-oriented: what a note means
/// depends on the <c>K:</c> and <c>L:</c> above it, and a fragment with no header is not something
/// anybody writes.
/// </para>
/// </summary>
public static class AbcConstructs
{
    public static readonly (string What, string Abc)[] Everything =
    [
        ("the smallest tune there is",
            "X:1\nK:C\nCDEF|\n"),

        ("pitches across every octave",
            "X:1\nK:C\nC,,C,C c c' c''|\nA,,B,,C,D,E,F,G,|\n"),

        ("note lengths, dotted and not",
            "X:1\nL:1/8\nK:C\nA/4 A/2 A/ A A2 A3 A4 A6 A7 A8 A12 A16|\nA3/2 A5/4 A// |\n"),

        ("accidentals, single and double",
            "X:1\nK:C\n__A _A =A ^A ^^A|\n"),

        ("bar lines, repeats and brackets",
            "X:1\nK:G\nABc|def||gab[|c'd'e'|]\n|:ABc:|\n|:def::gab:|\n|1 ABc:|2 def|]\n[1 ABc|[2 def|\n"),

        ("beams grouped by whitespace",
            "X:1\nL:1/8\nK:D\nABcd ABcd|A2B2 c2d2|ABAB cdcd|\n"),

        ("broken rhythm both ways",
            "X:1\nL:1/8\nK:C\nA>A B<B|C>>C D>>>D|\n"),

        ("tuplets, plain and counted",
            "X:1\nL:1/8\nK:C\n(3ABc (3:2ABc (3:2:3ABc|(2AB (4ABcd|\n"),

        ("ties and nested slurs",
            "X:1\nK:C\nA-A (AB) ((AA)A)|c-c|\n"),

        ("chords, with and without a length",
            "X:1\nK:C\n[CEG] [CEG]2 [A4d4] [^C_EG]/2|\n"),

        ("grace notes, beamed and slashed",
            "X:1\nK:G\n{g}A {gAGAG}B {/g}c {^fg}d|\n"),

        ("rests: visible, invisible and whole-bar",
            "X:1\nM:4/4\nL:1/8\nK:C\nz2 x2 A2 B2|Z|Z2|z/2 x/|\n"),

        ("chord symbols and placed annotations",
            "X:1\nK:Gm\n\"Gm7\"D \"^Fine\"E \"_x\"F \"<y\"G \"> z\"A|\n"),

        ("decorations, shorthand and named",
            "X:1\nK:C\n.A ~B HC LD ME OF PG Sa Tb uc vd|!trill!A !fermata!B !upbow!c|\n"),

        ("keys and modes, spaced and glued",
            "X:1\nK:C\nA|\nK:Cm\nA|\nK:C Lydian\nA|\nK:Bb\nA|\nK:F# clef=bass\nA|\nK:Ador\nA|\n"),

        ("meters, including the symbols",
            "X:1\nM:4/4\nK:C\nA4|\nM:C\nA4|\nM:C|\nA4|\nM:6/8\nA3A3|\nM:none\nA4|\n"),

        ("mid-tune changes, on a line and inline",
            "X:1\nM:4/4\nL:1/8\nK:C\nABcd|\nK:G\nABcd|\nM:3/4\nABc|\nT:Second Part\nABcd|\nABcd[K:D]efga|[M:2/4]AB|\n"),

        ("voices",
            "X:1\nK:C\nV:1 clef=treble name=\"Soprano\"\nABcd|efga|\nV:2 clef=bass\nA,,B,,C,D,|E,F,G,A,|\n[V:1]cdef|\n"),

        ("lyrics with every alignment mark",
            "X:1\nL:1/4\nK:C\nABcd|efga|\nw:one two- three _ *|four~five six | sev\\-en\nw:se-cond verse for the same notes\n"),

        ("the header fields an engraver prints",
            "X:1\nT:Speed the Plough\nT:a second title\nC:Trad.\nO:England\nR:reel\nS:Sussex\nZ:transcribed here\nN:a note\nW:a verse printed under the score\nM:4/4\nL:1/8\nK:G\nGABc dedB|\n"),

        ("comments, directives and continuations",
            "X:1\n% a whole-line comment\n%%score (1 2)\nK:C\nABcd|efga| % a trailing comment\nABcd\\\nefga|\n"),

        ("spacers, overlays and the odd corner",
            "X:1\nK:C\nA y B y2 C|A&B|\n"),

        ("what nobody can read, which an editor holds all day",
            "X:1\nK:C\n[CEG ABc| {gAG ^ _ (3 \"unclosed |\n"),

        // The corpus found this one and the construct list had not: three bar lines in a row at the head
        // of a line open nothing, and the first two were being dropped.
        ("bar lines that open nothing, which real tunebooks are full of",
            "X:234\nM:4/2\nL:1/4\nK:C\n |  |  |  | E4E2E2 | G4F2F2 |\n|| |: A2 :|\n"),

        ("windows line endings, because a pasted tune has them",
            "X:1\r\nT:pasted\r\nK:C\r\nABcd|efga|\r\n"),

        ("no terminator on the last line, because an editor is mid-word",
            "X:1\nK:C\nABc"),
    ];

    /// <summary>Every tune, and every line of every tune — the shorter inputs a reader is mid-way through.</summary>
    public static IEnumerable<(string What, string Abc)> EverythingAndItsLines()
    {
        foreach (var (what, abc) in Everything)
        {
            yield return (what, abc);

            var lines = abc.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0)
                    yield return ($"{what}: line {i + 1}", lines[i]);
        }
    }
}
