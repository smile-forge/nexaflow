using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Markdown;

/// <summary>
/// The file types that are one markdown block rather than a markdown document, and which language that
/// block is written in.
///
/// <para>
/// A <c>.abc</c> file is a tune. It is not a markdown document that happens to contain a tune, and
/// nothing in it is markdown — so the fence that makes the renderer treat it as music is a rendering
/// detail rather than part of the file, and the bytes on disk must never carry one. That is exactly what
/// <c>InlineMarkdownEditor.SingleBlock</c> already does for the Solver's LaTeX tab; this is the same
/// arrangement pointed at a file instead of a text box.
/// </para>
/// <para>
/// It is a table rather than a guess because the mapping is not derivable: an extension says what a file
/// holds, and only the renderer knows which fenced language draws that. Adding a row is the whole of what
/// it takes to open another notation this way — the reading, the editing, the saving and the dirty
/// tracking are the markdown tab's, unchanged.
/// </para>
/// </summary>
public static class SingleBlockFiles
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".abc"] = "abc",
        [".ly"] = "lilypond",
    };

    /// <summary>
    /// The fenced language a file's whole contents are, or null when the file is an ordinary document.
    /// </summary>
    public static string? LanguageOf(string path) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(path) ?? string.Empty);

    /// <summary>Every extension that opens as one block — what the file map has to route here.</summary>
    public static IReadOnlyCollection<string> Extensions => ByExtension.Keys;
}
