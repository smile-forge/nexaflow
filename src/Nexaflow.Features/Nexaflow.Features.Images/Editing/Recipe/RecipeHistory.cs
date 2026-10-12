using System.Collections.Generic;

namespace Nexaflow.Features.Images.Editing.Recipe;

/// <summary>
/// Undo and redo for one photo's recipe: a stack of whole recipes. A recipe is a few hundred bytes, so a snapshot per
/// step costs nothing and undo is exact.
/// </summary>
/// <remarks>
/// A continuous gesture — a slider drag, a crop handle, a pen stroke — commits many recipes but is one step: between
/// <see cref="BeginGesture"/> and <see cref="EndGesture"/> commits replace the current recipe without stacking, and
/// the gesture leaves one entry, or none when it ended where it began.
/// </remarks>
public sealed class RecipeHistory
{
    public const int DefaultLimit = 200;

    private readonly List<ImageRecipe> _undo = [];
    private readonly List<ImageRecipe> _redo = [];
    private readonly int _limit;
    private ImageRecipe? _gestureStart;

    public RecipeHistory(ImageRecipe initial, int limit = DefaultLimit)
    {
        Current = initial;
        _limit  = limit;
    }

    public ImageRecipe Current { get; private set; }

    public bool CanUndo => _undo.Count > 0 || (_gestureStart is not null && !_gestureStart.Equals(Current));
    public bool CanRedo => _redo.Count > 0 && _gestureStart is null;
    public bool InGesture => _gestureStart is not null;

    /// <summary>Raised whenever <see cref="Current"/>, <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>Makes <paramref name="next"/> current — one undo step, or part of the open gesture's.</summary>
    public void Commit(ImageRecipe next)
    {
        if (next.Equals(Current)) return;
        if (_gestureStart is null)
        {
            Push(_undo, Current);
            _redo.Clear();
        }
        Current = next;
        Changed?.Invoke();
    }

    public void BeginGesture()
    {
        if (_gestureStart is not null) EndGesture();
        _gestureStart = Current;
    }

    public void EndGesture()
    {
        if (_gestureStart is not { } start) return;
        _gestureStart = null;
        if (!start.Equals(Current))
        {
            Push(_undo, start);
            _redo.Clear();
        }
        Changed?.Invoke();
    }

    /// <summary>Abandons the open gesture, restoring the recipe it began from.</summary>
    public void CancelGesture()
    {
        if (_gestureStart is not { } start) return;
        _gestureStart = null;
        Current = start;
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
    /// Takes a recipe that arrived from outside the history — another window's edit, read back from the sidecar — as
    /// an ordinary undoable step.
    /// </summary>
    public void Adopt(ImageRecipe external)
    {
        CancelGesture();
        Commit(external);
    }

    /// <summary>Starts over from <paramref name="recipe"/> with nothing to undo or redo — after a save bakes the edits in.</summary>
    public void Reset(ImageRecipe recipe)
    {
        _gestureStart = null;
        _undo.Clear();
        _redo.Clear();
        Current = recipe;
        Changed?.Invoke();
    }

    private void Push(List<ImageRecipe> stack, ImageRecipe recipe)
    {
        stack.Add(recipe);
        if (stack.Count > _limit) stack.RemoveAt(0);
    }

    private static ImageRecipe Pop(List<ImageRecipe> stack)
    {
        var top = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return top;
    }
}
