using Nexaflow.IO.Common;
using System.IO;

namespace Nexaflow.Features.Hex.Buffer;

public enum PieceSource { File, Add }

// A contiguous span of the virtual byte space backed by either the original file or the add buffer.
public sealed record Piece(PieceSource Source, long Start, long Length);

/// <summary>
/// Piece-table hex buffer with a sliding read window for file-backed pieces.
/// Memory usage for unedited content is bounded by the window size (~8 KB) regardless of file size.
/// </summary>
public sealed class HexBuffer : IDisposable
{
    private const int BytesPerRow  = 16;
    private const int WindowRows   = 512;
    private const int WindowSize   = WindowRows * BytesPerRow; // 8 192 bytes
    private const int ReloadMargin = WindowRows / 4;           // rows before edge that triggers reload

    private Stream? _stream;   // a VFS stream: a real FileStream, or a seekable temp for an in-archive entry
    private readonly string _filePath;

    // ── File read window ──────────────────────────────────────────────────────
    private long _windowOffset = -1;
    private readonly byte[] _windowData = new byte[WindowSize];
    private int _windowBytesLoaded;

    // ── Piece table ───────────────────────────────────────────────────────────
    // Append-only; never truncated so undo/redo piece-list snapshots stay valid.
    private readonly List<byte> _addBuffer = [];
    private List<Piece> _pieces = [];

    // ── Undo / Redo stacks (store piece-list snapshots) ───────────────────────
    private readonly Stack<List<Piece>> _undoStack = new();
    private readonly Stack<List<Piece>> _redoStack = new();

    // ── Public state ──────────────────────────────────────────────────────────
    public long FileLength  { get; private set; }
    public long VirtualLength => _pieces.Sum(p => p.Length);
    public long TotalRows   => (VirtualLength + BytesPerRow - 1) / BytesPerRow;
    public bool IsModified  { get; private set; }
    public bool CanUndo     => _undoStack.Count > 0;
    public bool CanRedo     => _redoStack.Count > 0;

    public HexBuffer(string filePath)
    {
        _filePath = filePath;
        if (!string.IsNullOrEmpty(filePath) && VirtualFileSystem.Instance.Exists(filePath))
        {
            _stream     = VirtualFileSystem.Instance.OpenRead(filePath);
            FileLength  = _stream.Length;
        }
        if (FileLength > 0)
            _pieces.Add(new Piece(PieceSource.File, 0, FileLength));
    }

    // ── Window management ─────────────────────────────────────────────────────

    private void LoadWindow(long centerOffset)
    {
        if (_stream == null || FileLength == 0) return;
        long startRow   = Math.Max(0, centerOffset / BytesPerRow - WindowRows / 2);
        _windowOffset   = startRow * BytesPerRow;
        long available  = Math.Min(WindowSize, FileLength - _windowOffset);
        if (available <= 0) { _windowBytesLoaded = 0; return; }

        _stream.Position = _windowOffset;
        int toRead = (int)available;
        int read   = 0;
        while (read < toRead)
        {
            int n = _stream.Read(_windowData, read, toRead - read);
            if (n <= 0) break;
            read += n;
        }
        _windowBytesLoaded = read;
    }

    /// <summary>Called by the ViewModel/panels when the visible row range changes.</summary>
    public void EnsureWindow(long topRow, int visibleRows)
    {
        if (_stream == null) return;
        long topOff  = topRow * BytesPerRow;
        long botOff  = (topRow + visibleRows) * BytesPerRow;
        long winEnd  = _windowOffset + _windowBytesLoaded;
        long margin  = ReloadMargin * BytesPerRow;

        if (topOff < _windowOffset + margin || botOff > winEnd - margin)
            LoadWindow((topRow + visibleRows / 2) * BytesPerRow);
    }

    private byte ReadFileAt(long physicalOffset)
    {
        if (physicalOffset < 0 || physicalOffset >= FileLength) return 0;

        // Fast path: in window
        if (physicalOffset >= _windowOffset &&
            physicalOffset <  _windowOffset + _windowBytesLoaded)
            return _windowData[physicalOffset - _windowOffset];

        // Window miss: load and return (rare during normal scrolling)
        LoadWindow(physicalOffset);
        if (physicalOffset >= _windowOffset &&
            physicalOffset <  _windowOffset + _windowBytesLoaded)
            return _windowData[physicalOffset - _windowOffset];

        // Absolute fallback
        if (_stream == null) return 0;
        _stream.Position = physicalOffset;
        int b = _stream.ReadByte();
        return b < 0 ? (byte)0 : (byte)b;
    }

    // ── Reads ─────────────────────────────────────────────────────────────────

    public byte ReadByte(long virtualOffset)
    {
        if (virtualOffset < 0 || virtualOffset >= VirtualLength) return 0;
        long remaining = virtualOffset;
        foreach (var p in _pieces)
        {
            if (remaining < p.Length)
                return p.Source == PieceSource.File
                    ? ReadFileAt(p.Start + remaining)
                    : _addBuffer[(int)(p.Start + remaining)];
            remaining -= p.Length;
        }
        return 0;
    }

    public byte[] ReadRange(long virtualOffset, int length)
    {
        var result = new byte[length];
        long vLen  = VirtualLength;
        for (int i = 0; i < length; i++)
        {
            long o = virtualOffset + i;
            if (o >= vLen) break;
            result[i] = ReadByte(o);
        }
        return result;
    }

    /// <summary>True if the byte at virtualOffset comes from the add-buffer (i.e. was edited).</summary>
    public bool IsEdited(long virtualOffset)
    {
        if (virtualOffset < 0 || virtualOffset >= VirtualLength) return false;
        long remaining = virtualOffset;
        foreach (var p in _pieces)
        {
            if (remaining < p.Length) return p.Source == PieceSource.Add;
            remaining -= p.Length;
        }
        return false;
    }

    // ── Piece-table mutations ─────────────────────────────────────────────────

    private void PushUndo()
    {
        _undoStack.Push(_pieces.ToList());
        _redoStack.Clear();
        IsModified = true;
    }

    public void Overwrite(long virtualOffset, byte value)
    {
        if (virtualOffset < 0 || virtualOffset >= VirtualLength) return;
        PushUndo();
        long addIdx = _addBuffer.Count;
        _addBuffer.Add(value);
        _pieces = SplitAt(_pieces, virtualOffset, (left, _, right) =>
        {
            var r = new List<Piece>();
            if (left  != null) r.Add(left);
            r.Add(new Piece(PieceSource.Add, addIdx, 1));
            if (right != null) r.Add(right);
            return r;
        });
    }

    public void Insert(long virtualOffset, byte value)
    {
        if (virtualOffset < 0 || virtualOffset > VirtualLength) return;
        PushUndo();
        long addIdx = _addBuffer.Count;
        _addBuffer.Add(value);

        if (virtualOffset == VirtualLength)
        {
            _pieces = [.. _pieces, new Piece(PieceSource.Add, addIdx, 1)];
            return;
        }

        _pieces = SplitAt(_pieces, virtualOffset, (left, mid, right) =>
        {
            var r = new List<Piece>();
            if (left != null) r.Add(left);
            r.Add(new Piece(PieceSource.Add, addIdx, 1));
            if (mid  != null) r.Add(mid);
            if (right != null) r.Add(right);
            return r;
        });
    }

    public void Delete(long virtualOffset)
    {
        if (virtualOffset < 0 || virtualOffset >= VirtualLength) return;
        PushUndo();
        _pieces = SplitAt(_pieces, virtualOffset, (left, _, right) =>
        {
            var r = new List<Piece>();
            if (left  != null) r.Add(left);
            if (right != null) r.Add(right);
            return r;
        });
    }

    private delegate List<Piece> SplitDelegate(Piece? left, Piece? mid, Piece? right);

    private static List<Piece> SplitAt(List<Piece> pieces, long vOffset, SplitDelegate action)
    {
        long remaining = vOffset;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (remaining < p.Length)
            {
                Piece? left  = remaining > 0
                    ? new Piece(p.Source, p.Start, remaining) : null;
                Piece  mid   = new(p.Source, p.Start + remaining, 1);
                Piece? right = remaining + 1 < p.Length
                    ? new Piece(p.Source, p.Start + remaining + 1, p.Length - remaining - 1) : null;

                var modified = action(left, mid, right);
                var result   = new List<Piece>(pieces.Count - 1 + modified.Count);
                result.AddRange(pieces.Take(i));
                result.AddRange(modified);
                result.AddRange(pieces.Skip(i + 1));
                return result;
            }
            remaining -= p.Length;
        }
        return pieces.ToList();
    }

    // ── Undo / Redo ───────────────────────────────────────────────────────────

    public void Undo()
    {
        if (!_undoStack.TryPop(out var saved)) return;
        _redoStack.Push(_pieces.ToList());
        _pieces    = saved;
        IsModified = _undoStack.Count > 0;
    }

    public void Redo()
    {
        if (!_redoStack.TryPop(out var saved)) return;
        _undoStack.Push(_pieces.ToList());
        _pieces    = saved;
        IsModified = true;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    public void Save(string? savePath = null)
    {
        string target = savePath ?? _filePath;
        bool isSaveAs = savePath != null && savePath != _filePath;

        if (isSaveAs)
        {
            using var outFs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            WriteAll(outFs);
        }
        else if (File.Exists(target))
        {
            // Real file: write to a temp then replace (atomic-ish), memory-light for large files.
            string tmp = target + ".hextmp";
            try
            {
                using (var tmpFs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    WriteAll(tmpFs);

                _stream?.Dispose();
                _stream = null;

                File.Replace(tmp, target, null);

                _stream    = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                FileLength = _stream.Length;
                // Re-initialise piece table to single file piece
                _pieces    = [new Piece(PieceSource.File, 0, FileLength)];
                _windowOffset = -1;
            }
            catch
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                throw;
            }
        }
        else
        {
            // In-archive entry: build the new bytes and rewrite the owning archive through the VFS.
            byte[] bytes;
            using (var ms = new MemoryStream()) { WriteAll(ms); bytes = ms.ToArray(); }

            _stream?.Dispose();
            _stream = null;

            VirtualFileSystem.Instance.Replace(target, bytes);

            _stream    = VirtualFileSystem.Instance.OpenRead(target);
            FileLength = _stream.Length;
            _pieces    = [new Piece(PieceSource.File, 0, FileLength)];
            _windowOffset = -1;
        }

        _undoStack.Clear();
        _redoStack.Clear();
        IsModified = false;
    }

    private void WriteAll(Stream target)
    {
        const int bufSize = 64 * 1024;
        var buf = new byte[bufSize];
        long vLen = VirtualLength;
        for (long offset = 0; offset < vLen; offset += bufSize)
        {
            int count = (int)Math.Min(bufSize, vLen - offset);
            for (int i = 0; i < count; i++)
                buf[i] = ReadByte(offset + i);
            target.Write(buf, 0, count);
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
