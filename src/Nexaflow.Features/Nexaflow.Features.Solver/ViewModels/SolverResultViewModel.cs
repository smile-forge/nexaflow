using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Solver.Solving;

namespace Nexaflow.Features.Solver.ViewModels;

/// <summary>
/// One entry in the result list: what was asked, what came back, and what can be done with it.
/// <para>
/// A cell owns the cancellation of its own work, so removing a slow AI answer actually stops it
/// rather than leaving it running to complete into a cell nobody can see.
/// </para>
/// </summary>
public sealed partial class SolverResultViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Captured up front, because <see cref="CancellationTokenSource.Token"/> throws once the source
    /// is disposed — and this cell disposes its source the moment it is removed, which is exactly
    /// when something still holding the cell wants to ask whether its work was cancelled. A token
    /// taken before disposal stays readable and stays cancelled.
    /// </summary>
    private readonly CancellationToken _token;

    private readonly Action<SolverResultViewModel> _remove;
    private readonly Action<string> _useAsDefinition;
    private readonly Action<string> _copy;
    private bool _disposed;

    /// <summary>Creates a cell that is already running.</summary>
    /// <param name="chipLabel">Which chip produced it.</param>
    /// <param name="definition">The definition as it stood when the chip was pressed.</param>
    /// <param name="mode">Which editor that definition came from.</param>
    /// <param name="remove">Removes this cell from the list.</param>
    /// <param name="useAsDefinition">Puts text back into the definition area.</param>
    /// <param name="copy">Copies text to the clipboard.</param>
    public SolverResultViewModel(
        string chipLabel,
        string definition,
        DefinitionMode mode,
        Action<SolverResultViewModel> remove,
        Action<string> useAsDefinition,
        Action<string> copy)
    {
        _token = _cts.Token;
        ChipLabel = chipLabel;
        Definition = definition;
        Mode = mode;
        _remove = remove;
        _useAsDefinition = useAsDefinition;
        _copy = copy;
    }

    /// <summary>Which chip produced this.</summary>
    public string ChipLabel { get; }

    /// <summary>The definition this answers — shown as the cell's subtitle so a stack of cells stays readable.</summary>
    public string Definition { get; }

    /// <summary>Which editor the definition came from.</summary>
    public DefinitionMode Mode { get; }

    /// <summary>Cancelled when the cell is removed or the tab closes.</summary>
    public CancellationToken Token => _token;

    /// <summary>The answer, as markdown. Empty while <see cref="IsBusy"/>.</summary>
    [ObservableProperty]
    private string _markdown = string.Empty;

    /// <summary>True while the solver is still working.</summary>
    [ObservableProperty]
    private bool _isBusy = true;

    /// <summary>True when the result reports a failure rather than an answer.</summary>
    [ObservableProperty]
    private bool _isError;

    /// <summary>Records the outcome and stops the spinner.</summary>
    public void Complete(SolverResult result)
    {
        Markdown = result.Markdown;
        IsError = result.IsError;
        IsBusy = false;
    }

    /// <summary>Records a cancellation as a quiet, non-error outcome.</summary>
    public void Cancelled()
    {
        Markdown = "*Cancelled.*";
        IsError = false;
        IsBusy = false;
    }

    /// <summary>Copies the answer's markdown.</summary>
    [RelayCommand]
    private void Copy() => _copy(Markdown);

    /// <summary>
    /// Feeds this answer back into the definition area, which is what makes the list a workbook
    /// rather than a log — the usual next step after simplifying something is to differentiate it.
    /// </summary>
    [RelayCommand]
    private void UseAsDefinition() => _useAsDefinition(Markdown);

    /// <summary>Removes the cell, cancelling its work if it is still running.</summary>
    [RelayCommand]
    private void Remove()
    {
        _remove(this);
        Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        _cts.Dispose();
    }
}
