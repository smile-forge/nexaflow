using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Solver.Solving;

namespace Nexaflow.Features.Solver.ViewModels;

/// <summary>One chip in the strip: a solver's offer, and the command that takes it up.</summary>
public sealed partial class SolverChipViewModel : ObservableObject
{
    private readonly Func<SolverChip, Task> _run;

    /// <summary>Wraps <paramref name="chip"/> with the action that runs it.</summary>
    public SolverChipViewModel(SolverChip chip, Func<SolverChip, Task> run)
    {
        Chip = chip;
        _run = run;
    }

    /// <summary>The offer this chip represents.</summary>
    public SolverChip Chip { get; }

    /// <summary>Unique across the strip — what the view keys items by.</summary>
    public string Key => Chip.Key;

    /// <summary>What the chip reads.</summary>
    public string Label => Chip.Label;

    /// <summary>Leading symbol, or empty.</summary>
    public string Glyph => Chip.Glyph;

    /// <summary>
    /// Whether to draw the glyph. False when the label already opens with it — the "=" chip carries
    /// "=" as both and the integral chip is "∫" plus "∫ dx", so drawing both reads as "= =" and
    /// "∫ ∫ dx".
    /// </summary>
    public bool ShowGlyph =>
        Chip.Glyph.Length > 0 && !Chip.Label.StartsWith(Chip.Glyph, StringComparison.Ordinal);

    /// <summary>Tooltip.</summary>
    public string Description => Chip.Description;

    /// <summary>
    /// The automation id the UI journey reaches this chip by. Derived from the chip's stable key
    /// rather than its label, so renaming what a chip says never breaks a test.
    /// </summary>
    public string AutomationId => "SolverChip_" + Chip.Key.Replace('.', '_').Replace('/', '_');

    /// <summary>Runs this chip, appending a result cell.</summary>
    [RelayCommand]
    private Task RunAsync() => _run(Chip);
}
