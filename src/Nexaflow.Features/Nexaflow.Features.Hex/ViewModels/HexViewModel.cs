using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Hex.Buffer;
using Nexaflow.Visuals.Common.Formatting;
using System.Globalization;
using System.IO;
using System.Text;

namespace Nexaflow.Features.Hex.ViewModels;

public enum HexEditMode   { ReadOnly, Insert, Overwrite }
public enum HexEncoding   { Auto, Ascii, Utf8, Utf16LE, Utf16BE }

public sealed partial class HexViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IShellServices _shell;

    // ── Buffer ────────────────────────────────────────────────────────────────
    public HexBuffer Buffer { get; }

    // ── File info ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _filePath    = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;

    // ── Scroll ────────────────────────────────────────────────────────────────
    [ObservableProperty] private long _topRow;
    [ObservableProperty] private long _totalRows;
    [ObservableProperty] private int  _visibleRowCount = 20;

    // ── Cursor / Selection ────────────────────────────────────────────────────
    [ObservableProperty] private long _cursorOffset   = -1;
    [ObservableProperty] private long _selectionStart = -1;
    [ObservableProperty] private long _selectionLength;
    [ObservableProperty] private string _selectionText = string.Empty;

    // ── Edit mode ─────────────────────────────────────────────────────────────
    [ObservableProperty] private HexEditMode _editMode = HexEditMode.ReadOnly;
    [ObservableProperty] private bool _isReadOnly  = true;
    [ObservableProperty] private bool _isInsert;
    [ObservableProperty] private bool _isOverwrite;
    [ObservableProperty] private bool _isModified;

    // ── Encoding ──────────────────────────────────────────────────────────────
    [ObservableProperty] private HexEncoding _encoding = HexEncoding.Auto;
    [ObservableProperty] private HexEncoding _resolvedEncoding = HexEncoding.Ascii;

    public string[] EncodingNames { get; } = ["Auto", "ASCII", "UTF-8", "UTF-16"];

    // ── UI toggles ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _showEvaluatePane = true;

    // ── Goto ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _gotoText = string.Empty;

    // ── Event to request visual refresh of both panels ────────────────────────
    public event Action? InvalidateView;

    public HexViewModel(string filePath, IShellServices shell)
    {
        _shell   = shell;
        FilePath = filePath;
        Buffer   = new HexBuffer(filePath);
        TotalRows = Buffer.TotalRows;
        FileSizeText = FormatSize(Buffer.FileLength);
        DetectEncoding();
        UpdateSelectionText();
    }

    // ── Partial callbacks ─────────────────────────────────────────────────────

    partial void OnTopRowChanged(long value)
    {
        Buffer.EnsureWindow(value, VisibleRowCount);
        InvalidateView?.Invoke();
    }

    partial void OnVisibleRowCountChanged(int value)
    {
        Buffer.EnsureWindow(TopRow, value);
        InvalidateView?.Invoke();

        // A reveal requested before the view had measured itself could not scroll anywhere; now it can.
        if (value > 0 && _pendingRevealOffset >= 0)
        {
            long offset = _pendingRevealOffset, length = _pendingRevealLength;
            _pendingRevealOffset = -1;
            RevealRange(offset, length);
        }
    }

    // ── Reveal a range ────────────────────────────────────────────────────────

    private long _pendingRevealOffset = -1;
    private long _pendingRevealLength;

    /// <summary>
    /// Selects <paramref name="length"/> bytes at <paramref name="offset"/> and scrolls them into
    /// view — how another feature says "show me this part of the file" (a PE section, a resource, a
    /// TLS callback).
    /// <para>
    /// Called before the view has laid out — which is the normal case when the tab is opening — the
    /// request is held until <see cref="VisibleRowCount"/> is known, because scrolling is meaningless
    /// until then.
    /// </para>
    /// </summary>
    public void RevealRange(long offset, long length)
    {
        if (offset < 0) return;

        if (VisibleRowCount <= 0)
        {
            _pendingRevealOffset = offset;
            _pendingRevealLength = length;
            return;
        }

        if (length > 0) SetSelection(offset, length);
        else            SetCursor(offset);

        ScrollToRow(offset / 16);
        InvalidateView?.Invoke();
    }

    partial void OnCursorOffsetChanged(long value)
    {
        UpdateSelectionText();
        InvalidateView?.Invoke();
    }

    partial void OnSelectionLengthChanged(long value)
    {
        UpdateSelectionText();
        InvalidateView?.Invoke();
    }

    partial void OnEncodingChanged(HexEncoding value)
    {
        if (value == HexEncoding.Auto)
            DetectEncoding();
        else
            ResolvedEncoding = value;
        InvalidateView?.Invoke();
    }

    partial void OnResolvedEncodingChanged(HexEncoding value)
        => InvalidateView?.Invoke();

    // ── Selection helpers ─────────────────────────────────────────────────────

    private void UpdateSelectionText()
    {
        SelectionText = SelectionLength > 0
            ? $"Selection: {SelectionLength:N0} bytes  (0x{SelectionStart:X} – 0x{SelectionStart + SelectionLength - 1:X})"
            : CursorOffset >= 0
                ? $"Offset: 0x{CursorOffset:X8}  ({CursorOffset:N0})"
                : string.Empty;
    }

    public void SetCursor(long offset)
    {
        CursorOffset   = Math.Clamp(offset, 0, Math.Max(0, Buffer.VirtualLength - 1));
        SelectionStart  = CursorOffset;
        SelectionLength = 0;
    }

    public void SetSelection(long start, long length)
    {
        SelectionStart  = Math.Clamp(start,  0, Math.Max(0, Buffer.VirtualLength - 1));
        SelectionLength = Math.Clamp(length, 0, Buffer.VirtualLength - SelectionStart);
        CursorOffset    = SelectionStart;
        UpdateSelectionText();
    }

    public void ExtendSelection(long toOffset)
    {
        toOffset = Math.Clamp(toOffset, 0, Math.Max(0, Buffer.VirtualLength - 1));
        long anchor = SelectionLength == 0 ? CursorOffset : SelectionStart;
        if (toOffset >= anchor)
        {
            SelectionStart  = anchor;
            SelectionLength = toOffset - anchor + 1;
        }
        else
        {
            SelectionStart  = toOffset;
            SelectionLength = anchor - toOffset + 1;
        }
        CursorOffset = toOffset;
        UpdateSelectionText();
    }

    // ── Edit mode commands ────────────────────────────────────────────────────

    [RelayCommand]
    private void SetModeReadOnly()
    {
        EditMode   = HexEditMode.ReadOnly;
        IsReadOnly = true; IsInsert = false; IsOverwrite = false;
    }

    [RelayCommand]
    private void SetModeInsert()
    {
        EditMode   = HexEditMode.Insert;
        IsReadOnly = false; IsInsert = true; IsOverwrite = false;
    }

    [RelayCommand]
    private void SetModeOverwrite()
    {
        EditMode   = HexEditMode.Overwrite;
        IsReadOnly = false; IsInsert = false; IsOverwrite = true;
    }

    // ── Encoding commands ─────────────────────────────────────────────────────

    [RelayCommand] private void SetEncodingAuto()    => Encoding = HexEncoding.Auto;
    [RelayCommand] private void SetEncodingAscii()   => Encoding = HexEncoding.Ascii;
    [RelayCommand] private void SetEncodingUtf8()    => Encoding = HexEncoding.Utf8;
    [RelayCommand] private void SetEncodingUtf16LE() => Encoding = HexEncoding.Utf16LE;
    [RelayCommand] private void SetEncodingUtf16BE() => Encoding = HexEncoding.Utf16BE;

    // ── Evaluate pane ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleEvaluatePane() => ShowEvaluatePane = !ShowEvaluatePane;

    // ── Goto ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void GotoOffset()
    {
        string text = GotoText.Trim().TrimStart('0', 'x', 'X');
        if (!long.TryParse(
                string.IsNullOrEmpty(text) ? "0" : text,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out long offset))
        {
            // Try decimal
            if (!long.TryParse(GotoText.Trim(), out offset)) return;
        }

        offset = Math.Clamp(offset, 0, Math.Max(0, Buffer.VirtualLength - 1));
        long row = offset / 16;
        CursorOffset = offset;
        SelectionStart = offset; SelectionLength = 0;
        ScrollToRow(row);
    }

    // ── Undo / Redo ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Buffer.Undo();
        SyncBufferState();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        Buffer.Redo();
        SyncBufferState();
    }

    private bool CanUndo() => Buffer.CanUndo;
    private bool CanRedo() => Buffer.CanRedo;

    private void SyncBufferState()
    {
        IsModified = Buffer.IsModified;
        TotalRows  = Buffer.TotalRows;
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        InvalidateView?.Invoke();
    }

    // ── Write byte (called by panels on key input) ────────────────────────────

    public void WriteByte(long virtualOffset, byte value)
    {
        if (EditMode == HexEditMode.ReadOnly) return;

        if (EditMode == HexEditMode.Overwrite)
            Buffer.Overwrite(virtualOffset, value);
        else
            Buffer.Insert(virtualOffset, value);

        SyncBufferState();
    }

    public void DeleteByte(long virtualOffset)
    {
        if (EditMode == HexEditMode.ReadOnly) return;
        Buffer.Delete(virtualOffset);
        SyncBufferState();
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        try
        {
            Buffer.Save();
            IsModified = false;
            TotalRows  = Buffer.TotalRows;
            FileSizeText = FormatSize(Buffer.FileLength);
            SaveCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _shell.ShowError($"Save failed: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAs()
    {
        var ext  = Path.GetExtension(FilePath);
        var path = await _shell.PickSaveFileAsync(
            Path.GetFileName(FilePath),
            string.IsNullOrEmpty(ext) ? null : [ext],
            Path.GetDirectoryName(FilePath));
        if (path is null) return;

        try
        {
            Buffer.Save(path);
            FilePath     = path;
            IsModified   = false;
            FileSizeText = FormatSize(Buffer.FileLength);
            SaveCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _shell.ShowError($"Save failed: {ex.Message}");
        }
    }

    private bool CanSave() => IsModified;

    // ── Scroll helpers ────────────────────────────────────────────────────────

    public void ScrollToRow(long row)
    {
        long maxRow = Math.Max(0, TotalRows - VisibleRowCount);
        TopRow = Math.Clamp(row, 0, maxRow);
    }

    public void EnsureCursorVisible()
    {
        long cursorRow = CursorOffset / 16;
        if (cursorRow < TopRow)
            ScrollToRow(cursorRow);
        else if (cursorRow >= TopRow + VisibleRowCount)
            ScrollToRow(cursorRow - VisibleRowCount + 1);
    }

    // ── Encoding detection ────────────────────────────────────────────────────

    private void DetectEncoding()
    {
        if (Buffer.VirtualLength < 2) { ResolvedEncoding = HexEncoding.Ascii; return; }
        var bom = Buffer.ReadRange(0, 4);
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            ResolvedEncoding = HexEncoding.Utf8;
        else if (bom[0] == 0xFF && bom[1] == 0xFE)
            ResolvedEncoding = HexEncoding.Utf16LE;
        else if (bom[0] == 0xFE && bom[1] == 0xFF)
            ResolvedEncoding = HexEncoding.Utf16BE;
        else
            ResolvedEncoding = HexEncoding.Ascii;
    }

    // ── IPageViewModel ────────────────────────────────────────────────────────

    public string GetContext()
    {
        if (string.IsNullOrEmpty(FilePath)) return "Hex editor: no file loaded.";

        long vlen  = Buffer.VirtualLength;
        var  dirty = IsModified ? " (unsaved edits)" : string.Empty;
        var  cursor = CursorOffset >= 0
            ? $"0x{CursorOffset:X} ({CursorOffset:N0})"
            : "unset";
        var  selection = SelectionLength > 0
            ? $"0x{SelectionStart:X}-0x{SelectionStart + SelectionLength - 1:X} ({SelectionLength:N0} bytes)"
            : "none";
        long topOffset = TopRow * 16;
        long endOffset = Math.Min(vlen, (TopRow + VisibleRowCount) * 16);
        long lastByte  = Math.Max(topOffset, endOffset - 1);

        return
            $"Hex editor - file: {FilePath}, size: {FileSizeText} ({vlen:N0} bytes){dirty}. "
          + $"Edit mode: {EditMode}. Encoding: {ResolvedEncoding} (setting: {Encoding}). "
          + $"Cursor: {cursor}. Selection: {selection}. "
          + $"Visible: rows {TopRow:N0}-{TopRow + VisibleRowCount - 1:N0} "
          + $"(bytes 0x{topOffset:X}-0x{lastByte:X}).\n"
          + "Use read_bytes to read any byte range as hex+ASCII (including beyond the visible window), "
          + "or find_bytes to locate a hex/text pattern.";
    }

    public string? GetSecurityContext() => string.IsNullOrEmpty(FilePath) ? null : FilePath;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Read / explore (auto-run) — reach any byte, not just the visible window ──
        new DelegateClientTool(
            "read_bytes",
            "Read a range of bytes and return them as a hex + ASCII dump. Use this to inspect any part of "
          + "the file, including bytes outside the currently visible window.",
            [
                new ClientToolParameter("offset", "Start byte offset - decimal, or hex with a 0x prefix (e.g. 256 or 0x100)."),
                new ClientToolParameter("length", "How many bytes to read (default 64, max 4096).", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            (args, _) =>
            {
                long vlen = Buffer.VirtualLength;
                if (vlen == 0) return Task.FromResult(ToolResult.Error("The file is empty."));
                if (!TryParseOffset(ToolArgs.Str(args, "offset", "at", "position"), out long offset))
                    return Task.FromResult(ToolResult.Error("Missing or invalid 'offset'."));
                if (offset < 0 || offset >= vlen)
                    return Task.FromResult(ToolResult.Error($"Offset 0x{offset:X} is outside the file (0..0x{vlen - 1:X})."));
                int length = Math.Clamp(ToolArgs.Int(args, "length", 64), 1, 4096);
                length = (int)Math.Min(length, vlen - offset);
                var bytes = Buffer.ReadRange(offset, length);
                return Task.FromResult(ToolResult.Ok(
                    $"read {length} byte(s) at 0x{offset:X}", HexDump(offset, bytes, length)));
            }),

        new DelegateClientTool(
            "find_bytes",
            "Search the file for a byte pattern and return the offsets where it occurs. The pattern is "
          + "either a hex byte string (e.g. \"DE AD BE EF\") or literal text.",
            [
                new ClientToolParameter("pattern", "Bytes to find - a hex byte string or literal text."),
                new ClientToolParameter("type", "\"hex\" or \"text\" - how to read 'pattern' (default: hex if it looks like hex bytes, else text).", Required: false),
                new ClientToolParameter("start", "Offset to start searching from (default 0).", Required: false),
                new ClientToolParameter("max", "Maximum match offsets to return (default 20, max 200).", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            (args, _) =>
            {
                long vlen = Buffer.VirtualLength;
                if (vlen == 0) return Task.FromResult(ToolResult.Error("The file is empty."));
                var pattern = ToolArgs.Str(args, "pattern", "needle", "query");
                if (string.IsNullOrEmpty(pattern)) return Task.FromResult(ToolResult.Error("No 'pattern' provided."));
                var needle = PatternToBytes(pattern, ToolArgs.Str(args, "type", "mode"));
                if (needle is null || needle.Length == 0)
                    return Task.FromResult(ToolResult.Error("Invalid pattern (a hex pattern needs an even number of hex digits)."));
                TryParseOffset(ToolArgs.Str(args, "start", "from", "offset"), out long start);
                int max = Math.Clamp(ToolArgs.Int(args, "max", 20), 1, 200);
                var (hits, scannedToEnd) = FindPattern(needle, Math.Max(0, start), max);
                if (hits.Count == 0)
                    return Task.FromResult(ToolResult.Ok("no match", scannedToEnd
                        ? "No occurrences found." : "No occurrences found in the scanned region."));
                var body = string.Join("\n", hits.Select(o => $"0x{o:X} ({o:N0})"));
                var note = hits.Count >= max ? $"\n(stopped at the first {max} match(es).)"
                         : scannedToEnd ? string.Empty : "\n(reached the scan limit; more may exist further in.)";
                return Task.FromResult(ToolResult.Ok($"{hits.Count} match(es)", $"Found at:\n{body}{note}"));
            }),

        new DelegateClientTool(
            "get_selection",
            "Report the current cursor offset and selection range, plus the selected bytes as hex+ASCII (if any).",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                var sb = new StringBuilder();
                sb.Append(CursorOffset >= 0 ? $"Cursor: 0x{CursorOffset:X} ({CursorOffset:N0})." : "Cursor: unset.");
                if (SelectionLength > 0)
                {
                    sb.Append($" Selection: 0x{SelectionStart:X}-0x{SelectionStart + SelectionLength - 1:X} ({SelectionLength:N0} bytes).\n");
                    int take = (int)Math.Min(SelectionLength, 256);
                    sb.Append(HexDump(SelectionStart, Buffer.ReadRange(SelectionStart, take), take));
                    if (SelectionLength > take) sb.Append($"(showing the first {take} of {SelectionLength:N0} selected bytes.)");
                }
                else sb.Append(" No selection.");
                return Task.FromResult(ToolResult.Ok("reported cursor/selection", sb.ToString()));
            }),

        // ── Navigate (auto-run — moves the cursor/selection, nothing committed) ──
        new DelegateClientTool(
            "goto_offset",
            "Move the cursor to a byte offset and scroll it into view.",
            [ new ClientToolParameter("offset", "Byte offset - decimal, or hex with a 0x prefix (e.g. 256 or 0x100).") ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                long vlen = Buffer.VirtualLength;
                if (vlen == 0) return ToolResult.Error("The file is empty.");
                if (!TryParseOffset(ToolArgs.Str(args, "offset", "at", "position"), out long offset))
                    return ToolResult.Error("Missing or invalid 'offset'.");
                offset = Math.Clamp(offset, 0, vlen - 1);
                await _shell.RunOnUiAsync(() => { SetCursor(offset); ScrollToRow(offset / 16); });
                return ToolResult.Ok($"cursor at 0x{offset:X}", $"Moved the cursor to 0x{offset:X} ({offset:N0}).");
            }),

        new DelegateClientTool(
            "select_range",
            "Select a range of bytes (moves the cursor to the start and scrolls it into view).",
            [
                new ClientToolParameter("offset", "Start byte offset - decimal or 0x-hex."),
                new ClientToolParameter("length", "Number of bytes to select.", Type: "number"),
            ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                long vlen = Buffer.VirtualLength;
                if (vlen == 0) return ToolResult.Error("The file is empty.");
                if (!TryParseOffset(ToolArgs.Str(args, "offset", "at", "start"), out long offset))
                    return ToolResult.Error("Missing or invalid 'offset'.");
                int length = ToolArgs.Int(args, "length", 0);
                if (length <= 0) return ToolResult.Error("'length' must be a positive number of bytes.");
                await _shell.RunOnUiAsync(() => { SetSelection(offset, length); ScrollToRow(offset / 16); });
                return ToolResult.Ok(
                    $"selected {SelectionLength:N0} byte(s) at 0x{SelectionStart:X}",
                    $"Selected 0x{SelectionStart:X}-0x{SelectionStart + SelectionLength - 1:X} ({SelectionLength:N0} bytes).");
            }),

        // ── Change display setting (auto-run — reversible, not committed) ──
        new DelegateClientTool(
            "set_encoding",
            "Set how the decoded (ASCII) pane interprets bytes.",
            [ new ClientToolParameter("encoding", "One of: auto, ascii, utf8, utf16le, utf16be.") ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var name = ToolArgs.Str(args, "encoding", "enc")?
                    .Replace("-", "").Replace("_", "").ToLowerInvariant();
                HexEncoding? enc = name switch
                {
                    "auto"                => HexEncoding.Auto,
                    "ascii"               => HexEncoding.Ascii,
                    "utf8"                => HexEncoding.Utf8,
                    "utf16" or "utf16le"  => HexEncoding.Utf16LE,
                    "utf16be"             => HexEncoding.Utf16BE,
                    _                     => null,
                };
                if (enc is null)
                    return ToolResult.Error($"Unknown encoding '{name}'. Use auto, ascii, utf8, utf16le, or utf16be.");
                await _shell.RunOnUiAsync(() => Encoding = enc.Value);
                return ToolResult.Ok($"encoding: {ResolvedEncoding}", $"Set the encoding to {enc.Value} (resolved: {ResolvedEncoding}).");
            }),

        // ── Mutating (approval-gated) ──
        new DelegateClientTool(
            "overwrite_bytes",
            "Overwrite bytes in place at an offset (does not change the file length). Provide the replacement "
          + "bytes as a hex string. The edit is uncommitted until save.",
            [
                new ClientToolParameter("offset", "Start byte offset - decimal or 0x-hex."),
                new ClientToolParameter("bytes", "Replacement bytes as hex (e.g. \"DE AD BE EF\")."),
            ],
            ToolSafety.RequiresApproval,
            async (args, _) =>
            {
                long vlen = Buffer.VirtualLength;
                if (vlen == 0) return ToolResult.Error("The file is empty.");
                if (!TryParseOffset(ToolArgs.Str(args, "offset", "at", "position"), out long offset))
                    return ToolResult.Error("Missing or invalid 'offset'.");
                var data = PatternToBytes(ToolArgs.Str(args, "bytes", "hex", "value") ?? string.Empty, "hex");
                if (data is null || data.Length == 0)
                    return ToolResult.Error("'bytes' must be a non-empty hex string with an even number of digits.");
                if (offset < 0 || offset + data.Length > vlen)
                    return ToolResult.Error(
                        $"Overwrite [0x{offset:X}..0x{offset + data.Length - 1:X}] is outside the file (0..0x{vlen - 1:X}). "
                      + "Overwrite does not extend the file length.");
                await _shell.RunOnUiAsync(() =>
                {
                    for (int i = 0; i < data.Length; i++) Buffer.Overwrite(offset + i, data[i]);
                    SyncBufferState();
                });
                return ToolResult.Ok(
                    $"overwrote {data.Length} byte(s) at 0x{offset:X}",
                    $"Overwrote {data.Length} byte(s) at 0x{offset:X}. Unsaved - call save to persist.");
            }),

        new DelegateClientTool(
            "save",
            "Save all edits to the file on disk.",
            [],
            ToolSafety.RequiresApproval,
            async (_, _) =>
            {
                if (!IsModified) return ToolResult.Ok("nothing to save", "There are no unsaved edits.");
                await _shell.RunOnUiAsync(() => SaveCommand.Execute(null));
                return IsModified
                    ? ToolResult.Error("save failed", "The save did not complete; the file may be read-only or in use.")
                    : ToolResult.Ok("saved", "Saved all edits to disk.");
            }),
    ];

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Parses a byte offset: "0x"-prefixed hex, otherwise decimal, otherwise bare hex.</summary>
    private static bool TryParseOffset(string? text, out long offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset)) return true;
        return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset);
    }

    /// <summary>Turns a search pattern into bytes: a hex byte string, or literal text (UTF-8).</summary>
    private static byte[]? PatternToBytes(string pattern, string? type)
    {
        bool hex = type is not null
            ? type.StartsWith("hex", StringComparison.OrdinalIgnoreCase)
            : LooksLikeHex(pattern);
        if (!hex) return System.Text.Encoding.UTF8.GetBytes(pattern);

        var cleaned = new string(pattern.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (cleaned.Length == 0 || cleaned.Length % 2 != 0) return null;
        var bytes = new byte[cleaned.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            if (!byte.TryParse(cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                return null;
        return bytes;
    }

    private static bool LooksLikeHex(string s)
    {
        var stripped = s.Where(c => !char.IsWhiteSpace(c)).ToArray();
        return stripped.Length > 0 && stripped.Length % 2 == 0 && stripped.All(Uri.IsHexDigit);
    }

    // Linear scan for a byte pattern. Bounded by MaxScanBytes so a huge file can't hang the tool;
    // returns whether the scan reached the end of the file.
    private (List<long> hits, bool scannedToEnd) FindPattern(byte[] needle, long start, int max)
    {
        const long MaxScanBytes = 64L * 1024 * 1024;
        var hits = new List<long>();
        long vlen = Buffer.VirtualLength;
        if (needle.Length == 0 || needle.Length > vlen || start >= vlen) return (hits, true);

        long scanEnd    = Math.Min(vlen, start + MaxScanBytes);
        long lastStart  = scanEnd - needle.Length;
        for (long i = start; i <= lastStart; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (Buffer.ReadByte(i + j) != needle[j]) { match = false; break; }
            if (match)
            {
                hits.Add(i);
                if (hits.Count >= max) return (hits, false);
                i += needle.Length - 1;   // non-overlapping
            }
        }
        return (hits, scanEnd >= vlen);
    }

    /// <summary>Classic offset / hex / ASCII dump of <paramref name="count"/> bytes from <paramref name="baseOffset"/>.</summary>
    private static string HexDump(long baseOffset, byte[] bytes, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i += 16)
        {
            sb.Append($"{baseOffset + i:X8}  ");
            int rowLen = Math.Min(16, count - i);
            for (int j = 0; j < 16; j++)
            {
                sb.Append(j < rowLen ? $"{bytes[i + j]:X2} " : "   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int j = 0; j < rowLen; j++)
            {
                byte b = bytes[i + j];
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    public void Dispose() => Buffer.Dispose();
}
