using System;
using System.Collections.Generic;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What every builder has in common, which turns out to be one promise: <strong>laying something out
/// always produces a layout.</strong>
///
/// <para>
/// Not "usually". A builder that may hand back nothing makes everything downstream of it carry a second
/// state — the formula element grew ten <c>is null</c> guards and a whole second measure, render and
/// caret path, drawing the source in a monospaced font because there was no tree to draw. Every one of
/// those would have to be written again for the next kind of content, and the state they guard against
/// only exists because a builder was allowed to say no.
/// </para>
/// <para>
/// So there are three answers and they are all a <see cref="Laid"/>: what the reader meant, when it can
/// be read; the source shown as its own characters when it cannot; and the same again with the reason
/// attached when the attempt threw. The third is why <see cref="Lay"/> catches — a builder is reading
/// text somebody is in the middle of typing, so it will be handed nonsense continually, and "the
/// content vanished" is never the right way to say so.
/// </para>
/// <para>
/// The promise is kept here rather than asked of each builder, because a promise each builder makes for
/// itself is one each builder can break. <see cref="Build"/> is theirs and may return null or throw;
/// <see cref="Lay"/> is not theirs, and cannot.
/// </para>
/// <para>
/// A builder is handed content that has already been read and worked over, and turns it into a layout. It owns
/// nothing and outlives nothing: reading the source is the parser's, what the source means together is the
/// pipeline's, and what happens when somebody types into the picture is the element's. One step of the chain,
/// with an <see cref="ContentReading"/> going in and a <see cref="Laid"/> coming out.
/// </para>
/// </summary>
public abstract class ContentBuilder
{
    protected ContentBuilder(ContentReading reading)
    {
        Reading = reading;
    }

    /// <summary>The content this is laying out, read and worked over before it ever got here.</summary>
    protected ContentReading Reading { get; }

    /// <summary>The source this is laying out — what a selection over the result yields.</summary>
    public string Source => Reading.Source;

    /// <summary>
    /// Where <see cref="Source"/> begins in the document that holds it: nought for content that is a document of
    /// its own, and the offset of the slice for content written inside another's — see <see cref="ContentPart.Of"/>.
    /// </summary>
    protected int At => Reading.Root.Start;

    /// <summary>
    /// Lays the source out. Never null, and never throws.
    /// </summary>
    public Laid Lay()
    {
        try
        {
            return Build() ?? Shown([]);
        }
        catch (Exception error)
        {
            // The message is the reader's, so it says what happened to their formula rather than which
            // method threw. The stretch is the whole source, because a builder that fell over has no
            // opinion about which part of it was to blame.
            return Shown([new Diagnostic(
                0, Source.Length, DiagnosticSeverity.Error,
                $"This could not be set: {error.Message}")]);
        }
    }

    /// <summary>
    /// Lay the content out, or null where none of it is this builder's to draw.
    ///
    /// <para>
    /// Null is for "nothing here is mine to draw", not for trouble: content that is partly drawable comes
    /// back as a layout with <see cref="Laid.Trouble"/> on it, which is how a half-typed command draws as
    /// a formula with a wave under one word rather than as a page of characters.
    /// </para>
    /// </summary>
    protected abstract Laid? Build();

    /// <summary>
    /// How this content sets raw characters — its typeface, its size — for the source it could not read.
    /// <para>
    /// The text is handed in rather than taken from <see cref="Source"/>, because empty source is set as
    /// a blank so the line still has a height.
    /// </para>
    /// </summary>
    protected abstract FormattedText Characters(string text);

    /// <summary>The source as itself, with whatever there is to say about why.</summary>
    private Laid Shown(IReadOnlyList<Diagnostic> trouble) =>
        LayoutText.Shown(Source, Characters(Source.Length == 0 ? " " : Source), trouble);
}
