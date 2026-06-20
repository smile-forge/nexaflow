using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.IO.Terminal;

/// <summary>
/// A minimal but correct VT100/ANSI screen buffer.
///
/// Raw ConPTY output is fed into <see cref="Feed"/>, which interprets
/// escape sequences and maintains a 2-D character grid.  The grid is the
/// source of truth: callers read finished lines from <see cref="TakeLines"/>
/// and the current prompt (if any) from <see cref="TakePromptLine"/>.
///
/// Why a screen buffer instead of stripping?
///   cmd.exe — and most terminal applications — use cursor-movement sequences
///   to implement editing.  The simplest example: the default prompt is
///   emitted as "C:\path&gt;" with a CR on the preceding line, so the drive
///   letter ends up in column 0 of the same row as previous content.
///   You cannot reconstruct the correct text without tracking the cursor.
/// </summary>
public sealed class TerminalScreen
{
    // ── Grid ─────────────────────────────────────────────────────────────

    private readonly int _cols;
    private readonly int _rows;

    // Each cell is one Unicode scalar value (or '\0' for empty).
    private readonly char[,] _cells;

    // Cursor position (0-based)
    private int _curRow;
    private int _curCol;

    // Lines that have scrolled off the top of the screen, awaiting consumption as scrollback.
    private readonly Queue<string> _scrollback = new();

    // Saved cursor position (ESC 7 / ESC 8 / CSI s / CSI u)
    private int _savedRow;
    private int _savedCol;

    // Scrolling margins (0-based, inclusive); default = full screen
    private int _scrollTop;
    private int _scrollBottom;

    // DEC Line Drawing mode (ESC ( 0 / ESC ( B)
    private bool _lineDrawing;

    // Custom tab stops (column indices, 0-based)
    private readonly SortedSet<int> _tabStops = new();

    // Set when ESC ( has been seen and we are waiting for the charset byte
    private bool _awaitingCharSet;

    // Alternate screen buffer support
    private char[,]? _altCells;
    private int      _altCurRow;
    private int      _altCurCol;
    private bool     _inAltBuffer;

    // ── Parser state ─────────────────────────────────────────────────────

    private readonly StringBuilder _escBuf  = new();   // accumulating escape sequence
    private bool                   _inEsc;             // inside ESC sequence
    private bool                   _inOsc;             // inside OSC (ESC ] ... BEL/ST)
    private bool                   _sawEsc;            // saw bare ESC, waiting for next byte

    // ── Construction ─────────────────────────────────────────────────────

    public TerminalScreen(int cols = 220, int rows = 50)
    {
        _cols         = cols;
        _rows         = rows;
        _cells        = new char[rows, cols];
        _scrollTop    = 0;
        _scrollBottom = rows - 1;
        // Default tab stops every 8 columns
        for (int c = 8; c < cols; c += 8)
            _tabStops.Add(c);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Feed raw bytes (already UTF-8 decoded) from the PTY into the screen buffer.
    /// </summary>
    public void Feed(string raw)
    {
        foreach (var ch in raw)
            ProcessChar(ch);
    }

    /// <summary>
    /// Removes and returns the lines that have scrolled off the top of the screen — the terminal's
    /// scrollback. (Unlike a "commit on every line feed" model, on-screen lines stay in the grid so the
    /// live screen — prompt, in-progress output, full-screen apps — renders correctly via <see cref="Snapshot"/>.)
    /// </summary>
    public IReadOnlyList<string> TakeScrollback()
    {
        var result = new List<string>(_scrollback.Count);
        while (_scrollback.TryDequeue(out var line))
            result.Add(line);
        return result;
    }

    /// <summary>The visible screen: grid rows 0..(last non-empty or cursor row), trailing blanks trimmed.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        int last = _curRow;
        for (int r = _rows - 1; r >= 0; r--)
            if (ReadRow(r).Length > 0) { last = Math.Max(last, r); break; }

        var rows = new List<string>(last + 1);
        for (int r = 0; r <= last; r++) rows.Add(ReadRow(r));
        return rows;
    }

    /// <summary>
    /// True while an alternate-screen-buffer application (e.g. vi, a full-screen TUI) owns the display.
    /// Its output is the live screen, not appended scrollback, so callers should read <see cref="Snapshot"/>
    /// rather than expect line-by-line output.
    /// </summary>
    public bool IsAltScreen => _inAltBuffer;

    /// <summary>Cursor row within the visible screen (0-based).</summary>
    public int CursorRow => _curRow;

    /// <summary>Cursor column within the visible screen (0-based).</summary>
    public int CursorCol => _curCol;

    /// <summary>
    /// The current cursor row's text if it looks like a shell prompt (ends with '&gt;'), else null.
    /// Used to track the working directory; the row is not modified.
    /// </summary>
    public string? PeekPromptLine()
    {
        var row = ReadRow(_curRow).TrimEnd();
        if (row.Length > 0 && row[^1] == '>')
            return row;
        return null;
    }

    // ── Character processing ──────────────────────────────────────────────

    private void ProcessChar(char ch)
    {
        // ── Character-set designator (follows ESC () ──────────────────────
        if (_awaitingCharSet)
        {
            _awaitingCharSet = false;
            _lineDrawing = ch == '0';   // ESC ( 0 = DEC line drawing; ESC ( B = ASCII
            return;
        }

        // ── OSC accumulation ─────────────────────────────────────────────
        if (_inOsc)
        {
            if (ch == '\x07' || ch == '\x9c') { _inOsc = false; _escBuf.Clear(); }
            else if (ch == '\x1b') { /* ST = ESC \ coming */ }
            else if (ch == '\\' && _escBuf.Length > 0 && _escBuf[^1] == '\x1b')
                                   { _inOsc = false; _escBuf.Clear(); }
            else _escBuf.Append(ch);
            return;
        }

        // ── ESC sequence accumulation ─────────────────────────────────────
        if (_inEsc)
        {
            _escBuf.Append(ch);

            // CSI: ESC [ ... final-byte (0x40–0x7E)
            if (_escBuf.Length == 1 && ch == '[') return;   // wait for params+final

            if (_escBuf.Length >= 2 && _escBuf[0] == '[')
            {
                if (ch >= 0x40 && ch <= 0x7E)
                {
                    ApplyCsi(_escBuf.ToString());
                    _escBuf.Clear();
                    _inEsc = false;
                }
                // else still accumulating parameter bytes
                return;
            }

            // OSC: ESC ]
            if (_escBuf.Length == 1 && ch == ']') { _inEsc = false; _inOsc = true; _escBuf.Clear(); return; }

            // Two-byte: ESC followed by any Intermediate/Final in 0x40–0x5F
            if (_escBuf.Length == 1)
            {
                Apply2ByteEsc(ch);
                _escBuf.Clear();
                _inEsc = false;
            }
            return;
        }

        // ── Bare ESC ──────────────────────────────────────────────────────
        if (_sawEsc)
        {
            _sawEsc = false;
            _inEsc  = true;
            _escBuf.Clear();
            _escBuf.Append(ch);
            // Re-process the next character as the second byte of the sequence
            if (_escBuf[0] == '[') return;   // wait for more
            if (_escBuf[0] == ']') { _inOsc = true; _escBuf.Clear(); _inEsc = false; return; }
            Apply2ByteEsc(ch);
            _escBuf.Clear();
            _inEsc = false;
            return;
        }

        if (ch == '\x1b') { _sawEsc = true; return; }

        // ── C0 controls ───────────────────────────────────────────────────
        switch (ch)
        {
            case '\r': _curCol = 0;        return;
            case '\n': CommitLine();       return;
            case '\b': if (_curCol > 0) _curCol--; return;
            case '\t':
            {
                // Advance to the next custom tab stop; fall back to end of line
                int next = _tabStops.FirstOrDefault(s => s > _curCol, _cols - 1);
                _curCol = Math.Min(next, _cols - 1);
                return;
            }
            case '\x07': /* BEL */         return;
            case '\x0f': /* SI */          return;
            case '\x0e': /* SO */          return;
        }

        // ── Printable ─────────────────────────────────────────────────────
        if (ch < ' ') return;   // remaining C0 controls ignored

        if (_curCol >= _cols)
        {
            // Auto-wrap: commit current row and move to next
            CommitLine();
        }

        // DEC Line Drawing translation
        char cell = _lineDrawing ? MapLineDrawing(ch) : ch;
        _cells[_curRow, _curCol] = cell;
        _curCol++;
    }

    // ── Escape handlers ───────────────────────────────────────────────────

    private static readonly Regex CsiNumsRe = new(@"\d+", RegexOptions.Compiled);

    private int[] ParseCsiParams(string csi)
    {
        var ms = CsiNumsRe.Matches(csi);
        var result = new int[ms.Count];
        for (int i = 0; i < ms.Count; i++) result[i] = int.Parse(ms[i].Value);
        return result;
    }

    private void ApplyCsi(string csi)
    {
        if (csi.Length < 2) return;
        char final = csi[^1];
        var ps = ParseCsiParams(csi[..^1]);
        int p1 = ps.Length > 0 ? ps[0] : 0;
        int p2 = ps.Length > 1 ? ps[1] : 0;

        bool isPrivate = csi.Length > 1 && csi[1] == '?';

        switch (final)
        {
            // Cursor movement
            case 'A': MoveCursor(_curRow - Math.Max(1, p1), _curCol);           break;  // CUU
            case 'B': MoveCursor(_curRow + Math.Max(1, p1), _curCol);           break;  // CUD
            case 'C': _curCol = Math.Min(_cols - 1, _curCol + Math.Max(1, p1)); break;  // CUF
            case 'D': _curCol = Math.Max(0, _curCol - Math.Max(1, p1));         break;  // CUB
            case 'E': MoveCursor(_curRow + Math.Max(1, p1), 0);                 break;  // CNL
            case 'F': MoveCursor(_curRow - Math.Max(1, p1), 0);                 break;  // CPL
            case 'G': _curCol = Math.Min(_cols - 1, Math.Max(0, p1 - 1));       break;  // CHA
            case 'H': case 'f':                                                         // CUP / HVP
                MoveCursor(Math.Max(0, (p1 == 0 ? 1 : p1) - 1),
                           Math.Max(0, (p2 == 0 ? 1 : p2) - 1));
                break;
            case 'd': MoveCursor(Math.Max(0, p1 - 1), _curCol);                 break;  // VPA
            case 'J': EraseDisplay(p1);                                         break;  // ED
            case 'K': EraseLine(p1);                                            break;  // EL
            case 'P': DeleteChars(Math.Max(1, p1));                             break;  // DCH
            case 'X': EraseChars(Math.Max(1, p1));                              break;  // ECH
            case '@': InsertBlanks(Math.Max(1, p1));                            break;  // ICH
            case 'S': ScrollUp(Math.Max(1, p1));                                break;  // SU
            case 'T': ScrollDown(Math.Max(1, p1));                              break;  // SD
            case 'L': InsertLines(Math.Max(1, p1));                             break;  // IL
            case 'M': DeleteLines(Math.Max(1, p1));                             break;  // DL
            case 'I': TabForward(Math.Max(1, p1));                              break;  // CHT
            case 'Z': TabBackward(Math.Max(1, p1));                             break;  // CBT
            case 'g':                                                                   // TBC
                if (p1 == 0) _tabStops.Remove(_curCol);
                else if (p1 == 3) _tabStops.Clear();
                break;
            case 'r':                                                                   // DECSTBM – Set Scrolling Region
                if (!isPrivate)
                {
                    _scrollTop    = Math.Max(0,        (p1 == 0 ? 1 : p1) - 1);
                    _scrollBottom = Math.Min(_rows - 1, (p2 == 0 ? _rows : p2) - 1);
                    if (_scrollTop >= _scrollBottom) { _scrollTop = 0; _scrollBottom = _rows - 1; }
                    MoveCursor(0, 0); // DECSTBM resets cursor to home
                }
                break;
            case 's': // ANSISYSSC – Save cursor
                _savedRow = _curRow;
                _savedCol = _curCol;
                break;
            case 'u': // ANSISYSRC – Restore cursor
                MoveCursor(_savedRow, _savedCol);
                break;
            case 'p':  // DECSTR – Soft Reset (ESC [ ! p)
                if (csi.Length >= 3 && csi[^2] == '!')
                    SoftReset();
                break;
            case 'h': case 'l':
                if (isPrivate) ApplyPrivateMode(p1, final == 'h');
                break;
            // Ignored (SGR attributes, device queries, etc.)
            case 'm': case 'n': case 't': case 'c': case 'x':
                break;
        }
    }

    private void Apply2ByteEsc(char ch)
    {
        switch (ch)
        {
            case 'M': // RI – Reverse Index: move up, scroll down if at top margin
                if (_curRow > _scrollTop) _curRow--;
                else ScrollDown(1);
                break;
            case '7': // DECSC – Save cursor position
                _savedRow = _curRow;
                _savedCol = _curCol;
                break;
            case '8': // DECRC – Restore cursor position
                MoveCursor(_savedRow, _savedCol);
                break;
            case 'H': // HTS – Horizontal Tab Set
                _tabStops.Add(_curCol);
                break;
            case '(': // Designate G0 character set – wait for next byte
                _awaitingCharSet = true;
                break;
            case '=': case '>': /* keypad modes */ break;
            case 'c': /* RIS – reset */ ResetScreen(); break;
        }
    }

    // ── Grid helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Line feed / auto-wrap: move the cursor down one row, keeping the row's content on screen. Only when
    /// already at the bottom margin does the screen scroll, evicting the top line to scrollback. This is
    /// real-terminal behaviour — on-screen lines stay in the grid (so <see cref="Snapshot"/> renders the
    /// live screen in order) rather than being committed and cleared on every newline.
    /// </summary>
    private void CommitLine()
    {
        if (_curRow < _scrollBottom)
        {
            _curRow++;
        }
        else
        {
            // At the bottom margin: scroll the region up one, evicting the top line to scrollback.
            if (!_inAltBuffer) _scrollback.Enqueue(ReadRow(_scrollTop));
            for (int r = _scrollTop; r < _scrollBottom; r++)
                for (int c = 0; c < _cols; c++)
                    _cells[r, c] = _cells[r + 1, c];
            ClearRow(_scrollBottom);
            _curRow = _scrollBottom;
        }
        _curCol = 0;
    }

    private void ClearRow(int row)
    {
        for (int c = 0; c < _cols; c++) _cells[row, c] = '\0';
    }

    private void MoveCursor(int row, int col)
    {
        _curRow = Math.Clamp(row, 0, _rows - 1);
        _curCol = Math.Clamp(col, 0, _cols - 1);
    }

    private void ScrollUp(int n, bool commit = true)
    {
        int top    = _scrollTop;
        int bottom = _scrollBottom;
        n = Math.Min(n, bottom - top + 1);
        for (int i = 0; i < n; i++)
        {
            if (commit && !_inAltBuffer) _scrollback.Enqueue(ReadRow(top));
            for (int r = top; r < bottom; r++)
                for (int c = 0; c < _cols; c++)
                    _cells[r, c] = _cells[r + 1, c];
            ClearRow(bottom);
        }
        // SU/SD commands do not move the cursor
    }

    private void ScrollDown(int n)
    {
        int top    = _scrollTop;
        int bottom = _scrollBottom;
        n = Math.Min(n, bottom - top + 1);
        for (int i = 0; i < n; i++)
        {
            for (int r = bottom; r > top; r--)
                for (int c = 0; c < _cols; c++)
                    _cells[r, c] = _cells[r - 1, c];
            ClearRow(top);
        }
        // SU/SD commands do not move the cursor
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // from cursor to end
                EraseLine(0);
                for (int r = _curRow + 1; r < _rows; r++)
                    for (int c = 0; c < _cols; c++) _cells[r, c] = '\0';
                break;
            case 1: // from start to cursor
                for (int r = 0; r < _curRow; r++)
                    for (int c = 0; c < _cols; c++) _cells[r, c] = '\0';
                EraseLine(1);
                break;
            case 2: case 3: // whole screen
                for (int r = 0; r < _rows; r++)
                    for (int c = 0; c < _cols; c++) _cells[r, c] = '\0';
                break;
        }
    }

    private void EraseLine(int mode)
    {
        switch (mode)
        {
            case 0: for (int c = _curCol; c < _cols; c++) _cells[_curRow, c] = '\0'; break;
            case 1: for (int c = 0; c <= _curCol; c++) _cells[_curRow, c] = '\0'; break;
            case 2: for (int c = 0; c < _cols; c++) _cells[_curRow, c] = '\0'; break;
        }
    }

    private void DeleteChars(int n)
    {
        for (int c = _curCol; c < _cols - n; c++) _cells[_curRow, c] = _cells[_curRow, c + n];
        for (int c = _cols - n; c < _cols; c++) _cells[_curRow, c] = '\0';
    }

    private void InsertBlanks(int n)
    {
        for (int c = _cols - 1; c >= _curCol + n; c--) _cells[_curRow, c] = _cells[_curRow, c - n];
        for (int c = _curCol; c < _curCol + n && c < _cols; c++) _cells[_curRow, c] = '\0';
    }

    private void InsertLines(int n)
    {
        int bottom = _scrollBottom;
        for (int r = bottom; r >= _curRow + n; r--)
            for (int c = 0; c < _cols; c++) _cells[r, c] = _cells[r - n, c];
        for (int r = _curRow; r < _curRow + n && r <= bottom; r++)
            for (int c = 0; c < _cols; c++) _cells[r, c] = '\0';
    }

    private void DeleteLines(int n)
    {
        int bottom = _scrollBottom;
        for (int r = _curRow; r <= bottom - n; r++)
            for (int c = 0; c < _cols; c++) _cells[r, c] = _cells[r + n, c];
        for (int r = Math.Max(_curRow, bottom - n + 1); r <= bottom; r++)
            for (int c = 0; c < _cols; c++) _cells[r, c] = '\0';
    }

    private void ResetScreen()
    {
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++) _cells[r, c] = '\0';
        _curRow = 0; _curCol = 0;
        _scrollTop    = 0;
        _scrollBottom = _rows - 1;
        _lineDrawing  = false;
        _tabStops.Clear();
        for (int c = 8; c < _cols; c += 8) _tabStops.Add(c);
    }

    private void SoftReset()
    {
        // DECSTR – reset margins, cursor position, character set
        _scrollTop    = 0;
        _scrollBottom = _rows - 1;
        _lineDrawing  = false;
        _savedRow = 0; _savedCol = 0;
    }

    private void EraseChars(int n)
    {
        // ECH – overwrite n chars from cursor position with NUL (space)
        for (int c = _curCol; c < _curCol + n && c < _cols; c++)
            _cells[_curRow, c] = '\0';
    }

    private void TabForward(int n)
    {
        // CHT – advance to nth next tab stop
        for (int i = 0; i < n; i++)
        {
            if (_curCol >= _cols - 1) { _curCol = 0; CommitLine(); break; }
            int next = _tabStops.FirstOrDefault(s => s > _curCol, _cols - 1);
            _curCol = Math.Min(next, _cols - 1);
        }
    }

    private void TabBackward(int n)
    {
        // CBT – move to nth previous tab stop
        for (int i = 0; i < n; i++)
        {
            if (_curCol == 0) break;
            int prev = _tabStops.LastOrDefault(s => s < _curCol, 0);
            _curCol = prev;
        }
    }

    private void ApplyPrivateMode(int mode, bool enable)
    {
        switch (mode)
        {
            case 47:   // Alternate screen buffer (old)
            case 1047: // Alternate screen buffer
            case 1049: // Alternate screen buffer + save/restore cursor
                if (enable) EnterAltBuffer();
                else        LeaveAltBuffer();
                break;
            // 25 (cursor visibility), 1 (cursor keys mode), 12, 3 etc. – no grid effect
        }
    }

    private void EnterAltBuffer()
    {
        if (_inAltBuffer) return;
        // Save main buffer
        _altCells  = _cells.Clone() as char[,] ?? new char[_rows, _cols];
        _altCurRow = _curRow;
        _altCurCol = _curCol;
        _inAltBuffer = true;
        // Clear current grid for alternate buffer
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++) _cells[r, c] = '\0';
        _curRow = 0; _curCol = 0;
        _scrollTop = 0; _scrollBottom = _rows - 1;
    }

    private void LeaveAltBuffer()
    {
        if (!_inAltBuffer) return;
        // Restore main buffer
        if (_altCells != null)
            Array.Copy(_altCells, _cells, _altCells.Length);
        _curRow = _altCurRow;
        _curCol = _altCurCol;
        _inAltBuffer = false;
        _altCells    = null;
        _scrollTop = 0; _scrollBottom = _rows - 1;
    }

    /// <summary>Maps a printable ASCII character to its DEC Special Graphics equivalent.</summary>
    private static char MapLineDrawing(char ch) => ch switch
    {
        'j' => '\u2518', // ┘
        'k' => '\u2510', // ┐
        'l' => '\u250C', // ┌
        'm' => '\u2514', // └
        'n' => '\u253C', // ┼
        'q' => '\u2500', // ─
        't' => '\u251C', // ├
        'u' => '\u2524', // ┤
        'v' => '\u2534', // ┴
        'w' => '\u252C', // ┬
        'x' => '\u2502', // │
        _   => ch,
    };

    /// <summary>Read a row as a string, trailing NUL/spaces stripped.</summary>
    private string ReadRow(int row)
    {
        var sb = new StringBuilder(_cols);
        for (int c = 0; c < _cols; c++)
        {
            char ch = _cells[row, c];
            sb.Append(ch == '\0' ? ' ' : ch);
        }
        return sb.ToString().TrimEnd();
    }
}
