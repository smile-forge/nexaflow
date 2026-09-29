using System;
using System.Collections.Generic;

using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A language content can be written in: what parses it, what the parse is worked over by, which builder lays it out, and —
/// where it has any — what an edit means in it.
///
/// <para>
/// Only a description — data, and delegates the engine calls. Nothing here runs anything or makes anything, and a language
/// never asks for another: the engine parses, reads what the parse holds in other languages, works it over, makes the builder
/// and lays it out, in that order, every time (<see cref="ContentEngine"/>).
/// </para>
/// <para>
/// Languages that share a parser, stages or a kit of drawing are still a language each: every Mermaid diagram is one, told
/// apart by the builder that draws it.
/// </para>
/// </summary>
/// <param name="Reads">Whether a fence calling itself a word is written in this language.</param>
/// <param name="Parser">
/// Makes the parse one showing of content keeps, which hands back the tree and every piece in it written in another language.
/// Most languages hand back the same parse every time; one worth reading again and again as it is written hands back a parse
/// that remembers what it read last time.
/// </param>
/// <param name="Stages">What the parse is worked over by, in order, given the tree and what this showing of it is. A null stage is none.</param>
/// <param name="Builder">
/// The <see cref="ContentBuilder"/> that lays the worked-over tree out. The engine makes it, from the five things every builder is
/// made from.
/// </param>
public sealed record ContentLanguage(
    Func<string?, bool> Reads,
    Func<Func<string, ContentParse>> Parser,
    Func<ContentNode, ContentShowing, IEnumerable<IAstStage?>> Stages,
    Type Builder)
{
    /// <summary>
    /// Whether somebody writing in the content writes in what is drawn. Where not, it is drawn read-only however it is shown, and
    /// is written in as its characters.
    /// </summary>
    public bool Writable { get; init; }

    /// <summary>
    /// A second reading of the same characters, slower than <see cref="Parser"/> and made away from the thread that lays content
    /// out — or null where the first reading is all there is. The engine lays the first reading at once, starts this one, and
    /// lays the content again with what it gives once it lands; until then, and wherever it fails, the first reading stands.
    /// Called on any thread, so it keeps no state between calls that is not its own to lock.
    /// </summary>
    public Func<string, ContentParse>? SlowParser { get; init; }

    /// <summary>What an edit, a gesture and a block's corner mean in it — what they mean everywhere, unless it says otherwise.</summary>
    public IContentLanguage Editing { get; init; } = Usual.Editing;

    /// <summary>
    /// Reads what each binding standing where content would be (<c>{{Path}}</c>) comes to into its place in a parse, given what
    /// the binding naming a path comes to (null where it comes to nothing) — or null for a language holding no such bindings.
    /// Run by the engine before the stages, so they see what was supplied as they see the rest.
    /// </summary>
    public Func<ContentNode, Func<string, string?>, ContentNode>? Bind { get; init; }

    /// <summary>A language whose editing means nothing of its own.</summary>
    private sealed class Usual : IContentLanguage
    {
        public static readonly IContentLanguage Editing = new Usual();
    }

    /// <summary>
    /// Words as a part of this language's tree can hold them — what its parser says makes them read back as what was meant — or null
    /// where the part can hold none of them. The engine asks it of every stretch an edit names as words (<see cref="ContentWrite.Words"/>),
    /// because only what reads the language knows what is safe to write in it. Null for a language that has said nothing, whose words
    /// are written as they came.
    /// </summary>
    public Func<ContentPart, string, string?>? SafeFormatText { get; init; }

    /// <summary>What a reader calls it — on a button offering to start a block of it, and read out for one — or null for one never offered.</summary>
    public string? DisplayName { get; init; }

    /// <summary>What it is drawn as where there is room only for a mark: one of the Fluent UI System Icons, by name.</summary>
    public IconRef Icon { get; init; }

    /// <summary>
    /// A whole block of it, fences and all, that reads without fault and draws something a reader can start from — what is written where
    /// somebody asks for a block of it. Null for a language nobody starts a block of: markdown itself, what nests another, code.
    /// </summary>
    public string? DefaultBlock { get; init; }
}

/// <summary>
/// One showing of some content: what it is drawn in, whether anybody is writing in it, and what the host showing it said.
/// What a language's stages are told, and all they are told.
/// </summary>
/// <param name="Named">The word the content was called by — a fence's language as written, or empty for content no fence names.</param>
/// <param name="Style">What it is drawn in.</param>
/// <param name="Writing">Whether somebody is writing in it, which puts a hole wherever something is still to be written.</param>
/// <param name="Shown">The stretch shown as typed rather than as what it says, counted in the document — or null.</param>
/// <param name="At">Where the content's own source starts in the document holding it.</param>
/// <param name="Inputs">What the host said about the content it shows: pictures, links, what diagrams are bound against.</param>
public sealed record ContentShowing(string Named, StyleFormat Style, bool Writing, RawZone? Shown, int At, ContentInputs Inputs)
{
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
