using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hex.Buffer;
using System.Globalization;
using System.IO;

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

    [RelayCommand(CanExecute = nameof(CanUndo))]
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
        => $"Hex editor — file: {FilePath}, size: {FileSizeText}, mode: {EditMode}";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)           return $"{bytes} B";
        if (bytes < 1024 * 1024)   return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public void Dispose() => Buffer.Dispose();
}
