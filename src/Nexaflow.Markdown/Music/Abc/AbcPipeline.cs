using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// The actors ABC runs between the parser and the builder, and the order they run in.
///
/// <para>
/// The order is the only thing here that is an argument. Each of these needs the answers of the ones
/// before it: nothing can work out a length without the unit note length, nothing can group a tuplet
/// without the meter that gives its default, a tuplet beams as one group so it has to be a group before
/// the beams are found, a beam never crosses a bar line so the beams have to be found before the bars
/// close, and an accidental lasts a bar so the notes cannot be resolved until the bars exist.
/// </para>
/// <para>
/// The last two are a surface being written on asking for what a reader needs and a page being read does
/// not: a stretch shown exactly as typed while somebody is mid-keystroke, and a line under what nothing
/// can draw. Both are generic — one is shared with every other language, and the other only needs to be
/// told what the engraver knows.
/// </para>
/// </summary>
public static class AbcPipeline
{
    /// <summary>
    /// The tree to build a tune from: what was written, with everything a builder would otherwise have to
    /// work out for itself already worked out and hung underneath it.
    /// </summary>
    /// <param name="draws">
    /// Whether whatever is going to engrave this knows how to draw a named decoration. Asked for rather
    /// than known, because what can be drawn is a fact about an engraver and this is a reader.
    /// </param>
    /// <param name="editing">
    /// A stretch somebody is in the middle of typing, shown rather than read for as long as they are.
    /// Runs last on purpose: a half-written bar is invalid almost by definition, and saying so on every
    /// keystroke would be the wrong thing to draw.
    /// </param>
    public static ContentNode Read(string abc, Func<string, bool>? draws = null,
                                   (int Start, int Length)? editing = null) =>
        Of(draws, editing).Run(AbcParser.Parse(abc));

    /// <summary>The pipeline itself, for anything that wants to run the stages over a tree it already has.</summary>
    public static AstPipeline Of(Func<string, bool>? draws = null, (int Start, int Length)? editing = null) =>
        new AstPipeline(
            new ResolveContext(),      // what key, meter, unit length and voice are in force
            new GroupTuplets(),        // a marker and the events it covers
            new GroupBeams(),          // what was written together
            new GroupBars(),           // what is between two bar lines
            new ResolveNotes(),        // what each note sounds and how long each event lasts
            new AlignLyrics(),         // which word is sung on which note
            new CheckDrawable(draws))  // what is written correctly and still cannot be drawn
            .Then(ShowAsWritten.Of(editing));
}
