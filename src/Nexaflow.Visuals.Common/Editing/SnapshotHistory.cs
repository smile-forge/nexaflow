namespace Nexaflow.Visuals.Common.Editing;

/// <summary>
/// Undo and redo as a stack of whole snapshots: each step keeps the state as it stood, not the change that led to it,
/// so undo is exact and an edit needs no inverse. Made for small immutable states — a photo's recipe, a document's page
/// arrangement — where a snapshot per step costs next to nothing.
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
/// </remarks>
public sealed class SnapshotHistory<T> where T : notnull
{
    public const int DefaultLimit = 200;

    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];
    private readonly IEqualityComparer<T> _comparer;
    private readonly int _limit;
    private bool _inGesture;
    private T _gestureStart = default!;

    /// <param name="initial">Where the history starts, with nothing to undo.</param>
    /// <param name="comparer">Decides whether a commit changes anything; <see cref="EqualityComparer{T}.Default"/> when null.</param>
    /// <param name="limit">The most steps kept each way; the oldest falls off past it.</param>
    public SnapshotHistory(T initial, IEqualityComparer<T>? comparer = null, int limit = DefaultLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        Current   = initial;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _limit    = limit;
    }

    public T Current { get; private set; }

    public bool CanUndo => _undo.Count > 0 || (_inGesture && !_comparer.Equals(_gestureStart, Current));
    public bool CanRedo => _redo.Count > 0 && !_inGesture;
    public bool InGesture => _inGesture;

    /// <summary>Raised whenever <see cref="Current"/>, <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>Makes <paramref name="next"/> current — one undo step, or part of the open gesture's.</summary>
    public void Commit(T next)
    {
        if (_comparer.Equals(next, Current)) return;
        if (!_inGesture)
        {
            Push(_undo, Current);
            _redo.Clear();
        }
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
        if (!_comparer.Equals(start, Current))
        {
            Push(_undo, start);
            _redo.Clear();
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

    public bool Undo()
    {
        EndGesture();
        if (_undo.Count == 0) return false;
        Push(_redo, Current);
        Current = Pop(_undo);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        EndGesture();
        if (_redo.Count == 0) return false;
        Push(_undo, Current);
        Current = Pop(_redo);
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

    private void Push(List<T> stack, T snapshot)
    {
        stack.Add(snapshot);
        if (stack.Count > _limit) stack.RemoveAt(0);
    }

    private static T Pop(List<T> stack)
    {
        var top = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return top;
    }
}
