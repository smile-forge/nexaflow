namespace Nexaflow.Visuals.Common.Editing;

/// <summary>
/// Undo and redo as a stack of whole snapshots: each step keeps the state as it stood, not the change that led to it,
/// so undo is exact and an edit needs no inverse. Made for immutable states — a photo's recipe, a document's page
/// arrangement, a layered picture whose pixels share tiles between steps.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is held, never copied, so <typeparamref name="T"/> must not change once committed. Committing one equal
/// to <see cref="Current"/> under the history's comparer is no edit, and leaves the redo stack alone.
/// </para>
/// <para>
/// A continuous gesture — a slider drag, a crop handle, a pen stroke — commits many snapshots but is one step: between
/// <see cref="BeginGesture"/> and <see cref="EndGesture"/> commits replace the current snapshot without stacking, and
/// the gesture leaves one entry, or none when it ended where it began.
/// </para>
/// <para>
/// A batch — several changes said at once, an assistant's say — is one step too: inside <see cref="Batch"/> every commit, and
/// every gesture with its own begin and end, folds into the one entry the outermost scope leaves when it is disposed.
/// </para>
/// <para>
/// Steps are kept up to a count and, where the history is given a cost, a budget: each kept snapshot costs what it holds
/// that the snapshot beside it does not — the tiles a layered picture's step changed — and the oldest steps fall off once
/// the kept ones together cost more than the budget.
/// </para>
/// </remarks>
public sealed class SnapshotHistory<T> where T : notnull
{
    public const int DefaultLimit = 200;

    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private readonly IEqualityComparer<T> _comparer;
    private readonly int _limit;
    private readonly Func<T, T, long>? _cost;
    private bool _inGesture;
    private T _gestureStart = default!;
    private int _batchDepth;
    private T _batchStart = default!;

    /// <summary>A kept snapshot and what keeping it costs.</summary>
    private readonly record struct Entry(T Snapshot, long Cost);

    /// <param name="initial">Where the history starts, with nothing to undo.</param>
    /// <param name="comparer">Decides whether a commit changes anything; <see cref="EqualityComparer{T}.Default"/> when null.</param>
    /// <param name="limit">The most steps kept each way; the oldest falls off past it.</param>
    /// <param name="cost">What keeping the first snapshot costs beside the second, where steps are budgeted; null for none.</param>
    /// <param name="budget">What the kept steps may cost together, by <paramref name="cost"/>.</param>
    public SnapshotHistory(T initial, IEqualityComparer<T>? comparer = null, int limit = DefaultLimit, Func<T, T, long>? cost = null, long budget = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        Current   = initial;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _limit    = limit;
        _cost     = cost;
        Budget    = budget;
    }

    public T Current { get; private set; }

    public bool CanUndo => _undo.Count > 0 || (_batchDepth > 0 ? !_comparer.Equals(_batchStart, Current)
                                                               : _inGesture && !_comparer.Equals(_gestureStart, Current));
    public bool CanRedo => _redo.Count > 0 && !_inGesture && _batchDepth == 0;

    /// <summary>A batch is open: what is committed folds into one step.</summary>
    public bool InBatch => _batchDepth > 0;
    public bool InGesture => _inGesture;

    /// <summary>What the kept steps may cost together; lowering it lets the oldest go at once.</summary>
    public long Budget
    {
        get => _budget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _budget = value;
            Trim();
        }
    }

    private long _budget;

    /// <summary>What the kept steps cost together, both ways.</summary>
    public long Cost => _undo.Sum(e => e.Cost) + _redo.Sum(e => e.Cost);

    /// <summary>How many steps can be undone.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>Raised whenever <see cref="Current"/>, <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>Makes <paramref name="next"/> current — one undo step, or part of the open gesture's.</summary>
    public void Commit(T next)
    {
        if (_comparer.Equals(next, Current)) return;
        var previous = Current;
        Current = next;
        if (!_inGesture && _batchDepth == 0)
        {
            Push(_undo, previous, Current);
            _redo.Clear();
            Trim();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Makes <paramref name="next"/> current without a step of its own: a change that is part of the state but no edit —
    /// which layer is active — so undo neither stops at it nor loses it.
    /// </summary>
    public void Amend(T next)
    {
        if (_comparer.Equals(next, Current)) return;
        Current = next;
        Changed?.Invoke();
    }

    public void BeginGesture()
    {
        if (_inGesture) EndGesture();
        _inGesture    = true;
        _gestureStart = Current;
    }

    public void EndGesture()
    {
        if (!_inGesture) return;
        var start = TakeGestureStart();
        if (_batchDepth == 0 && !_comparer.Equals(start, Current))
        {
            Push(_undo, start, Current);
            _redo.Clear();
            Trim();
        }
        Changed?.Invoke();
    }

    /// <summary>Abandons the open gesture, restoring the snapshot it began from.</summary>
    public void CancelGesture()
    {
        if (!_inGesture) return;
        Current = TakeGestureStart();
        Changed?.Invoke();
    }

    /// <summary>
    /// Opens one step for everything committed until the scope is disposed — gestures among it included — so changes said at once
    /// are undone at once. Scopes nest: the outermost leaves the step, or none where it ended where it began. Undo and redo wait
    /// while one is open.
    /// </summary>
    public IDisposable Batch()
    {
        if (_batchDepth++ == 0)
        {
            EndGesture();
            _batchStart = Current;
        }
        return new BatchScope(this);
    }

    private void EndBatch()
    {
        if (_batchDepth == 0 || --_batchDepth > 0) return;
        if (_inGesture) TakeGestureStart();   // a gesture still open is part of the batch
        var start = _batchStart;
        _batchStart = default!;
        if (!_comparer.Equals(start, Current))
        {
            Push(_undo, start, Current);
            _redo.Clear();
            Trim();
        }
        Changed?.Invoke();
    }

    /// <summary>Ends its batch once, however often it is disposed.</summary>
    private sealed class BatchScope(SnapshotHistory<T> history) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended) return;
            _ended = true;
            history.EndBatch();
        }
    }

    public bool Undo()
    {
        EndGesture();
        if (_undo.Count == 0 || _batchDepth > 0) return false;
        var restored = Pop(_undo);
        Push(_redo, Current, restored);
        Current = restored;
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        EndGesture();
        if (_redo.Count == 0 || _batchDepth > 0) return false;
        var restored = Pop(_redo);
        Push(_undo, Current, restored);
        Current = restored;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Takes a snapshot that arrived from outside the history — another window's edit, read back from where the state
    /// is kept — as an ordinary undoable step.
    /// </summary>
    public void Adopt(T external)
    {
        CancelGesture();
        Commit(external);
    }

    /// <summary>Starts over from <paramref name="snapshot"/> with nothing to undo or redo — after a save bakes the edits in.</summary>
    public void Reset(T snapshot)
    {
        TakeGestureStart();
        _undo.Clear();
        _redo.Clear();
        Current = snapshot;
        if (_batchDepth > 0) _batchStart = snapshot;
        Changed?.Invoke();
    }

    /// <summary>Closes the gesture and hands back where it began, letting go of the reference it held.</summary>
    private T TakeGestureStart()
    {
        var start = _gestureStart;
        _inGesture    = false;
        _gestureStart = default!;
        return start;
    }

    /// <summary>Keeps <paramref name="snapshot"/> on <paramref name="stack"/>, costed beside <paramref name="neighbour"/>, the
    /// snapshot it is restored next to.</summary>
    private void Push(List<Entry> stack, T snapshot, T neighbour)
    {
        stack.Add(new Entry(snapshot, _cost?.Invoke(snapshot, neighbour) ?? 0));
        if (stack.Count > _limit) stack.RemoveAt(0);
    }

    private static T Pop(List<Entry> stack)
    {
        var top = stack[^1].Snapshot;
        stack.RemoveAt(stack.Count - 1);
        return top;
    }

    /// <summary>The oldest steps let go while the kept ones cost more than the budget — the farthest redo steps after the
    /// undo steps run out.</summary>
    private void Trim()
    {
        if (_cost is null) return;
        long total = Cost;
        while (total > Budget && _undo.Count > 0)
        {
            total -= _undo[0].Cost;
            _undo.RemoveAt(0);
        }
        while (total > Budget && _redo.Count > 0)
        {
            total -= _redo[0].Cost;
            _redo.RemoveAt(0);
        }
    }
}
