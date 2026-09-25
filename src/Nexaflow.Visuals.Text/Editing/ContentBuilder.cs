using System;
using System.Collections.Generic;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Markdown;

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
    /// <param name="reading">The content, read and worked over before it ever got here.</param>
    /// <param name="state">What is being written, and where — see <see cref="State"/>.</param>
    /// <param name="style">What it is drawn in: colours, type, and anything else about this showing of it.</param>
    /// <param name="isReadOnly">Whether the block is only being looked at.</param>
    protected ContentBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
    {
        Reading = reading;
        State = state;
        Style = style;
        IsReadOnly = isReadOnly;
    }

    /// <summary>The content this is laying out, read and worked over before it ever got here.</summary>
    protected ContentReading Reading { get; }

    /// <summary>
    /// What is being written, and where. A builder reads one thing from it — the stretch being shown as its
    /// own characters rather than as what it says, because somebody is typing in it.
    /// </summary>
    /// <remarks>
    /// Nothing to do with editing, which the element owns. This is how a builder draws content that is
    /// half-written: the characters, so the caret stands between the ones the reader can see.
    /// </remarks>
    protected EditState State { get; }

    /// <summary>What this showing of the content is drawn in — colours, type, and whatever else it carries.</summary>
    protected StyleFormat Style { get; }

    /// <summary>Whether the block is only being looked at.</summary>
    protected bool IsReadOnly { get; }

    /// <summary>
    /// Whether somebody is writing in the block rather than only reading it. Content being written draws what is
    /// still to be written — a hole where a label goes, a row for a value with nothing yet to draw.
    /// </summary>
    protected bool Writing => !IsReadOnly;

    /// <summary>
    /// How wide it may be laid out, in the content's own units — infinity where nothing says.
    ///
    /// <para>
    /// The one size a builder is told, because where a line breaks is a layout decision and only what measures
    /// the text can make it. Everything else about how big the content is set is a fact about the content or its
    /// style, not about the room it landed in.
    /// </para>
    /// </summary>
    protected double Room { get; private set; } = double.PositiveInfinity;

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
    public Laid Lay(double room = double.PositiveInfinity)
    {
        Room = double.IsNaN(room) || room <= 0 ? double.PositiveInfinity : room;

        try
        {
            return Build() ?? AsSource([]);
        }
        catch (Exception error)
        {
            // The message is the reader's, so it says what happened to their formula rather than which
            // method threw. The part blamed is the whole tree, because a builder that fell over has no
            // opinion about which part of it was to blame.
            return AsSource([(Reading.Root, $"This could not be set: {error.Message}")]);
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

    /// <summary>
    /// The block as it is written, each part blamed standing over its own characters and why written beneath — what a builder
    /// shows for what it cannot draw. It names parts of the tree it was given, never characters: turning the tree back into
    /// what was written is <see cref="SourceShown"/>'s.
    /// </summary>
    protected Laid AsSource(IReadOnlyList<(ContentPart Part, string Reason)> blamed) =>
        SourceShown.Lay(Reading.Root, blamed, Characters, Style, Room);

    /// <summary>The whole block as it is written, and why none of it could be drawn.</summary>
    protected Laid AsSource(string reason) => AsSource([(Reading.Root, reason)]);
}
