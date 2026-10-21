using System;
using System.Collections.Generic;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Text.Editor.Commands;

/// <summary>
/// The editor's built-in command groups (Lines / Checksum / Encode / Decode), each a thin adapter over the
/// pure transforms in <c>Nexaflow.IO.Common</c>. Stateless, so a single shared list is reused everywhere; what each
/// says is asked for as it is shown.
/// </summary>
public static class BuiltInTextEditorCommands
{
    private static readonly Func<string> Lines = static () => Str.Get("Text.Editor.Group.Lines");
    private static readonly Func<string> Checksum = static () => Str.Get("Text.Editor.Group.Checksum");
    private static readonly Func<string> Encode = static () => Str.Get("Text.Editor.Group.Encode");
    private static readonly Func<string> Decode = static () => Str.Get("Text.Editor.Group.Decode");

    public static IReadOnlyList<ITextEditorCommand> All { get; } =
    [
        // ── Lines: reordering/munging (prose/data only — nonsensical for code) ──
        new DelegateEditorCommand("Remove empty lines", static () => Str.Get("Text.Editor.Cmd.RemoveEmpty"), "Lines", Lines, TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.RemoveEmptyLines(c.DocumentText)), plainTextOnly: true),
        new DelegateEditorCommand("Remove duplicate lines", static () => Str.Get("Text.Editor.Cmd.RemoveDuplicates"), "Lines", Lines, TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.RemoveDuplicateLines(c.DocumentText)), plainTextOnly: true),
        new DelegateEditorCommand("Sort lines (A→Z)", static () => Str.Get("Text.Editor.Cmd.SortAscending"), "Lines", Lines, TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.SortLines(c.DocumentText)), plainTextOnly: true),
        new DelegateEditorCommand("Sort lines (Z→A)", static () => Str.Get("Text.Editor.Cmd.SortDescending"), "Lines", Lines, TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.SortLines(c.DocumentText, descending: true)), plainTextOnly: true),
        // ── Lines: EOL conversion (fine for code too) ──
        new DelegateEditorCommand("Convert to LF", static () => Str.Format("Text.Editor.Cmd.ConvertTo", "LF"), "Lines", Lines, TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.NormalizeLineEndings(c.DocumentText, LineEnding.Lf))),
        new DelegateEditorCommand("Convert to CRLF", static () => Str.Format("Text.Editor.Cmd.ConvertTo", "CRLF"), "Lines", Lines, TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.NormalizeLineEndings(c.DocumentText, LineEnding.CrLf))),
        new DelegateEditorCommand("Convert to CR", static () => Str.Format("Text.Editor.Cmd.ConvertTo", "CR"), "Lines", Lines, TextEditScope.Document, true,
            c => c.ReplaceDocument(TextTransforms.NormalizeLineEndings(c.DocumentText, LineEnding.Cr))),

        // ── Checksum (read-only; selection or whole document) ──
        new DelegateEditorCommand("MD5 of selection", static () => Str.Format("Text.Editor.Cmd.OfSelection", "MD5"), "Checksum", Checksum, TextEditScope.Selection, false,
            c => c.ShowResult?.Invoke("MD5", Hashing.Md5(c.SelectedText))),
        new DelegateEditorCommand("MD5 of document", static () => Str.Format("Text.Editor.Cmd.OfDocument", "MD5"), "Checksum", Checksum, TextEditScope.Document, false,
            c => c.ShowResult?.Invoke("MD5", Hashing.Md5(c.DocumentText))),
        new DelegateEditorCommand("SHA-256 of selection", static () => Str.Format("Text.Editor.Cmd.OfSelection", "SHA-256"), "Checksum", Checksum, TextEditScope.Selection, false,
            c => c.ShowResult?.Invoke("SHA-256", Hashing.Sha256(c.SelectedText))),
        new DelegateEditorCommand("SHA-256 of document", static () => Str.Format("Text.Editor.Cmd.OfDocument", "SHA-256"), "Checksum", Checksum, TextEditScope.Document, false,
            c => c.ShowResult?.Invoke("SHA-256", Hashing.Sha256(c.DocumentText))),

        // ── Encode / Decode (selection, destructive) ──
        new DelegateEditorCommand("Base64 encode selection", static () => Str.Get("Text.Editor.Cmd.Base64Encode"), "Encode", Encode, TextEditScope.Selection, true,
            c => c.ReplaceSelection(Base64Codec.Encode(c.SelectedText))),
        new DelegateEditorCommand("Base64 decode selection", static () => Str.Get("Text.Editor.Cmd.Base64Decode"), "Decode", Decode, TextEditScope.Selection, true,
            c =>
            {
                if (Base64Codec.TryDecode(c.SelectedText, out var decoded)) c.ReplaceSelection(decoded);
                else c.ShowError?.Invoke(Str.Get("Text.Editor.NotBase64"));
            }),
    ];
}
