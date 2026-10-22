using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Plot;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Languages;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Every language content can be written in — the one table, and the only one.
///
/// <para>
/// Whatever holds content in another language only says which language it is; the engine looks the language up here, has
/// its parser read the content and its builder lay it out (<see cref="ContentEngine"/>). That is true wherever it appears:
/// a block of a document, a label on a flowchart node, a caption under a picture. One table answers all of them, so a
/// language added once is drawn everywhere.
/// </para>
/// </summary>
public static class ContentLanguages
{
    /// <summary>
    /// The table as it stands, and what each word was found to name in it. Replaced whole when a language is added and never
    /// changed after: a language's <see cref="ContentLanguage.Reads"/> is code from wherever it was registered, so asking it while
    /// holding a lock deadlocks the moment that code waits on something that is itself waiting for the lock — a type initialiser
    /// registering languages, say. A lookup reads whichever table is current and asks it with nothing held.
    /// </summary>
    private static volatile LanguageTable Table = new(
    [
        Shipped.Mermaid,
        Shipped.Rtf,
        .. Shipped.Diagrams,
        Shipped.Nomnoml,
        Shipped.Qr,
        Shipped.Barcode,
        Shipped.Abc,
        Shipped.LilyPond,
        Shipped.DataMatrix,
        Shipped.Pdf417,
        Shipped.Aztec,
        Shipped.Smiles,
        Shipped.Latex,
        Shipped.WordCloud,
        Shipped.Plot(PlotFence.Scatter),
        Shipped.Plot(PlotFence.Bubble),
        Shipped.Plot(PlotFence.Heatmap),
        Shipped.Plot(PlotFence.Density2d),

        // Last, and deliberately. Code answers to dozens of words, so a fence calling itself something a language of its own
        // already claims must reach that one first.
        .. Shipped.Codes,
    ]);

    /// <summary>The languages in the order they are asked, and what each word was found to name among them.</summary>
    private sealed record LanguageTable(ImmutableArray<ContentLanguage> Known)
    {
        /// <summary>What each word was found to name, so a word is looked for in the table once — until a language is added.</summary>
        public ConcurrentDictionary<string, ContentLanguage?> Answered { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Taken only to replace the table, so two languages added at once are both kept.</summary>
    private static readonly Lock Adding = new();

    /// <summary>What content is written in where nothing names a language.</summary>
    public static ContentLanguage Markdown => Shipped.Markdown;

    /// <summary>What content is shown as where the language it names is one nothing reads: its characters, as code with no grammar.</summary>
    public static ContentLanguage Code => Shipped.Code;

    /// <summary>
    /// Adds a language, or replaces one already registered under a name it answers to. Last in wins, which is what lets a host
    /// override a language that ships.
    /// </summary>
    public static void Register(ContentLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);

        lock (Adding) Table = new([language, .. Table.Known]);
    }

    /// <summary>Whether anything here reads content calling itself <paramref name="language"/>.</summary>
    public static bool Reads(string? language) => For(language) is not null;

    /// <summary>What reads it, or null where nothing does.</summary>
    public static ContentLanguage? For(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;

        var table = Table;
        return table.Answered.GetOrAdd(language, static (word, known) => known.FirstOrDefault(each => each.Reads(word)), table.Known);
    }

    /// <summary>
    /// Every language a block can be started in (<see cref="ContentLanguage.DefaultBlock"/>), in the table's order, each once: where a
    /// host has put one in place of one that ships, only the host's.
    /// </summary>
    public static IReadOnlyList<ContentLanguage> Insertable =>
        [.. Table.Known.Where(known => known.DefaultBlock is not null).DistinctBy(known => known.Name)];

    /// <summary>
    /// The language <paramref name="part"/> holds content written in, where it holds content in one anything reads — a fence,
    /// a formula, a label written in another language — or null.
    /// </summary>
    public static ContentLanguage? Held(ContentPart part) =>
        ContentNested.Language(part) is { } named && part.Part(Roles.Body) is not null ? For(named) : null;

    /// <summary>
    /// The language the tree holding <paramref name="part"/> was read in: the one its root names (<see cref="BlockNode"/>). Markdown is
    /// what content is written in where nothing names a language, so nothing in the table answers to it; its root is known by the
    /// name its parser gives it.
    /// </summary>
    public static ContentLanguage? WrittenIn(ContentPart part) =>
        (part.Ancestors().LastOrDefault() ?? part).Node switch
        {
            BlockNode { Language: MarkdownParser.Language } => Markdown,
            BlockNode block => For(block.Language),
            _ => null,
        };
}
