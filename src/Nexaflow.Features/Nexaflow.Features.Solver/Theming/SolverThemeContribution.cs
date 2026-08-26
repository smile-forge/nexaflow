using Nexaflow.Features.Common;

namespace Nexaflow.Features.Solver.Theming;

/// <summary>
/// Contributes the Solver's key and chip tokens (<c>Solver.*</c>) as theme fallbacks.
/// FeatureManager discovers this by reflection and ThemeManager merges the dictionary below the
/// active theme, so every theme gets a usable keypad and any theme may retune a key by name — with
/// no Core⇄feature reference either way.
/// </summary>
public sealed class SolverThemeContribution : IThemeContribution
{
    /// <inheritdoc/>
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Solver;component/Theming/SolverTheme.xaml"),
    ];
}
