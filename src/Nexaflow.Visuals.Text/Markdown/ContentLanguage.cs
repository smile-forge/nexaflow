using System;
using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A language content can be written in: what parses it, what the parse is worked over by, what lays it out, and — where it
/// has any — what an edit means in it.
///
/// <para>
/// Only a description. Nothing here runs anything, and a language never asks for another: the engine parses, reads what the
/// parse holds in other languages, works it over and lays it out, in that order, every time (<see cref="ContentEngine"/>).
/// </para>
/// </summary>
/// <param name="Reads">Whether a fence calling itself a word is written in this language.</param>
/// <param name="Parser">
/// Makes the parse one showing of content keeps. Most languages hand back the same parse every time; one worth reading again
/// and again as it is written hands back a parse that remembers what it read last time.
/// </param>
/// <param name="Stages">What the parse is worked over by, in order, given the tree and what this showing of it is. A null stage is none.</param>
/// <param name="Builder">The builder that lays the worked-over tree out, made for this showing.</param>
public sealed record ContentLanguage(
    Func<string?, bool> Reads,
    Func<Func<string, ContentNode>> Parser,
    Func<ContentNode, ContentShowing, IEnumerable<IAstStage?>> Stages,
    Func<ContentReading, ContentShowing, ContentBuilder> Builder)
{
    /// <summary>What an edit, a gesture and a block's corner mean in it — what they mean everywhere, unless it says otherwise.</summary>
    public IContentLanguage Editing { get; init; } = Usual.Editing;

    /// <summary>A language whose editing means nothing of its own.</summary>
    private sealed class Usual : IContentLanguage
    {
        public static readonly IContentLanguage Editing = new Usual();
    }
}

/// <summary>
/// One showing of some content: what it is drawn in, whether anybody is writing in it, and what the host showing it said.
/// What a language's stages and its builder are told, and all they are told.
/// </summary>
/// <param name="Named">The word the content was called by — a fence's language as written, or empty for content no fence names.</param>
/// <param name="Style">What it is drawn in.</param>
/// <param name="Writing">Whether somebody is writing in it, which puts a hole wherever something is still to be written.</param>
/// <param name="Shown">The stretch shown as typed rather than as what it says, counted in the document — or null.</param>
/// <param name="At">Where the content's own source starts in the document holding it.</param>
/// <param name="Options">What the host said about the content it shows: pictures, links, what diagrams are bound against.</param>
public sealed record ContentShowing(string Named, StyleFormat Style, bool Writing, RawZone? Shown, int At, DiagramRenderOptions? Options)
{
    /// <summary>What lays out content written in another language inside this one — the one thing a builder may ask for.</summary>
    public required Nesting Nesting { get; init; }

    /// <summary>
    /// Whether a word names a language anything reads, for a stage that has to know whether a stretch is another language's
    /// to show. Nothing is laid out by asking.
    /// </summary>
    public required Func<string?, bool> Reads { get; init; }

    /// <summary>
    /// What says which of a document's blocks read as they did last time, for the content the engine was asked to show — null
    /// for content inside it, which is laid again with the block holding it or kept with it.
    /// </summary>
    public IAstStage? Unchanged { get; init; }

    /// <summary><see cref="Shown"/> counted in this content's own source, or null where it is not inside it.</summary>
    public RawZone? Own(int length) =>
        Shown is { } zone && zone.Start >= At && zone.End <= At + length ? new RawZone(zone.Start - At, zone.End - At) : null;
}
