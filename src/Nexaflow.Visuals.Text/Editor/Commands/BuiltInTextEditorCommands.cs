using System.Collections.Generic;
using Nexaflow.IO.Common;

namespace Nexaflow.Visuals.Text.Editor.Commands;

/// <summary>
/// The editor's built-in command groups (Lines / Checksum / Encode / Decode), each a thin adapter over the
/// pure transforms in <c>Nexaflow.IO.Common</c>. Stateless, so a single shared list is reused everywhere.
/// </summary>
public static class BuiltInTextEditorCommands
{
    public static IReadOnlyList<ITextEditorCommand> All { get; } =
    [
        // ── Lines: reordering/munging (prose/data only — nonsensical for code) ──
        new DelegateEditorCommand("Remove empty lines", "Lines", TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.RemoveEmptyLines(c.DocumentText)), plainTextOnly: true),
        new DelegateEditorCommand("Remove duplicate lines", "Lines", TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.RemoveDuplicateLines(c.DocumentText)), plainTextOnly: true),
        new DelegateEditorCommand("Sort lines (A→Z)", "Lines", TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.SortLines(c.DocumentText)), plainTextOnly: true),
        new DelegateEditorCommand("Sort lines (Z→A)", "Lines", TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.SortLines(c.DocumentText, descending: true)), plainTextOnly: true),
        // ── Lines: EOL conversion (fine for code too) ──
        new DelegateEditorCommand("Convert to LF", "Lines", TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.NormalizeLineEndings(c.DocumentText, LineEnding.Lf))),
        new DelegateEditorCommand("Convert to CRLF", "Lines", TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.NormalizeLineEndings(c.DocumentText, LineEnding.CrLf))),
        new DelegateEditorCommand("Convert to CR", "Lines", TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.NormalizeLineEndings(c.DocumentText, LineEnding.Cr))),

        // ── Checksum (read-only; selection or whole document) ──
        new DelegateEditorCommand("MD5 of selection", "Checksum", TextEditScope.Selection, false,
            c => c.ShowResult?.Invoke("MD5", Hashing.Md5(c.SelectedText))),
        new DelegateEditorCommand("MD5 of document", "Checksum", TextEditScope.Document, false,
            c => c.ShowResult?.Invoke("MD5", Hashing.Md5(c.DocumentText))),
        new DelegateEditorCommand("SHA-256 of selection", "Checksum", TextEditScope.Selection, false,
            c => c.ShowResult?.Invoke("SHA-256", Hashing.Sha256(c.SelectedText))),
        new DelegateEditorCommand("SHA-256 of document", "Checksum", TextEditScope.Document, false,
            c => c.ShowResult?.Invoke("SHA-256", Hashing.Sha256(c.DocumentText))),

        // ── Encode / Decode (selection, destructive) ──
        new DelegateEditorCommand("Base64 encode selection", "Encode", TextEditScope.Selection, true,
            c => c.ReplaceSelection(Base64Codec.Encode(c.SelectedText))),
        new DelegateEditorCommand("Base64 decode selection", "Decode", TextEditScope.Selection, true,
            c =>
            {
                if (Base64Codec.TryDecode(c.SelectedText, out var decoded)) c.ReplaceSelection(decoded);
                else c.ShowError?.Invoke("Selection is not valid Base64.");
            }),
    ];
}
