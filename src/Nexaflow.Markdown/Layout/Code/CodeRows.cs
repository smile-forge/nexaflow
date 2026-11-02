namespace Nexaflow.Markdown.Layout.Code;

/// <summary>
/// Where a line of code wraps onto rows in a room (<see cref="LayoutHost.WrapsLines"/>): as many characters as fit, broken after the
/// last space that fits, and a word wider than the row broken where the row ends. A space never breaks a row — the spaces a row ends on
/// hang past it, as a text box lets them — and nor does a character that takes no room. Nor is a row ended after nothing but the
/// line's indent: a row holding only spaces reads as a line with nothing on it, so a word too long to follow the indent is broken where
/// the row ends instead.
///
/// <para>
/// Fed a line's characters one at a time, each with how wide it is set, so laying a line (<see cref="CodeBuilder"/>) and saying how
/// tall a stretch stands without laying it (<see cref="CodeSetting.HeightOf"/>) break it by the one rule — the first measuring each
/// character, the second setting each at a monospaced face's advance, which for a monospaced face is the same thing.
/// </para>
/// </summary>
internal struct CodeRows(double room)
{
    /// <summary>Where in the line the row being filled starts.</summary>
    private int _start;

    /// <summary>Where in the line a row could start instead: just past the last space on the row being filled, or -1 where it has none.</summary>
    private int _after = -1;

    /// <summary>Whether the row being filled holds anything but spaces before <see cref="_after"/> — a row of nothing but an indent is no row to end.</summary>
    private bool _afterWords;

    /// <summary>Whether the row being filled holds anything but spaces.</summary>
    private bool _words;

    /// <summary>How far across the row being filled its characters reach.</summary>
    private double _across;

    /// <summary>How far across the characters since <see cref="_after"/> reach.</summary>
    private double _since;

    /// <summary>Where in the line the next character stands.</summary>
    private int _at;

    /// <summary>How many times the line fed so far has broken onto a row beneath.</summary>
    public int Breaks { get; private set; }

    /// <summary>
    /// The line's next character, set <paramref name="wide"/> across: where in the line the row it moves onto starts, where it does not
    /// fit on the row being filled, or -1 where it does.
    /// </summary>
    public int Next(char character, double wide)
    {
        var at = _at++;

        if (character is ' ' or '\t')
        {
            _across += wide;
            (_after, _since, _afterWords) = (at + 1, 0, _words);

            return -1;
        }

        // A character fits where there is room for it, where it takes none, and where it is the first on its row — a row holds a
        // character however narrow the room.
        if (_across + wide <= room || wide == 0 || at == _start)
        {
            _across += wide;
            _since += wide;
            _words = true;

            return -1;
        }

        // It does not fit: the row breaks after the last space on it, carrying the word written since onto the next — where the row has
        // words before that space — or else here.
        if (_after > _start && _afterWords) (_start, _across) = (_after, _since + wide);
        else (_start, _across) = (at, wide);

        (_after, _since, _afterWords, _words) = (-1, _across, false, true);
        this.Breaks++;

        return _start;
    }

    /// <summary>The next line starts: a row of its own with nothing on it.</summary>
    public void NewLine() => (_start, _after, _afterWords, _words, _across, _since, _at, this.Breaks) = (0, -1, false, false, 0, 0, 0, 0);
}
