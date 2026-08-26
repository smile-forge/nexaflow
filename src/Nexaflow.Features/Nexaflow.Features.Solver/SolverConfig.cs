using Nexaflow.Features.Common;
using Nexaflow.Features.Solver.Solving;

namespace Nexaflow.Features.Solver;

/// <summary>One symbol the recently-used strip should still be offering next time the app opens.</summary>
public sealed class RecentSymbol
{
    /// <summary>
    /// The text the key types. This rather than a tile id, because an id encodes where a symbol sits
    /// in the tree and the tree gets re-authored; what it types is what the symbol <i>is</i>.
    /// </summary>
    public string Insert { get; set; } = string.Empty;

    /// <summary>
    /// When it was last used. This is what decides which symbol is dropped when the strip is full, so
    /// it has to outlive the session — otherwise a restart would make eviction arbitrary.
    /// </summary>
    public DateTimeOffset LastUsed { get; set; }
}

/// <summary>Settings for the Solver tab, shown as a section in Options.</summary>
public sealed class SolverConfig : IFeatureConfig
{
    /// <inheritdoc/>
    public string ConfigName => "solver";

    /// <inheritdoc/>
    public string FriendlyName => "Solver";

    /// <summary>Which editor a freshly opened Solver tab starts on.</summary>
    [ConfigDisplayName("Start In")]
    [ListSource(typeof(SolverConfig), nameof(GetModeOptions))]
    public string StartMode { get; set; } = "Calc";

    /// <summary>
    /// How to read an angle. Degrees is the default because the calculator is the thing most people
    /// open this for, and typing <c>sin(45)</c> expecting radians is the rarer intent.
    /// </summary>
    [ConfigDisplayName("Angles")]
    [ListSource(typeof(SolverConfig), nameof(GetAngleOptions))]
    public string Angles { get; set; } = "Degrees";

    /// <summary>Places a decimal answer is rounded to.</summary>
    [ConfigDisplayName("Decimal Places")]
    [ListSource(typeof(SolverConfig), nameof(GetDecimalOptions))]
    public string DecimalPlaces { get; set; } = "6";

    /// <summary>Whether the button palette is open when a tab is created.</summary>
    [ConfigDisplayName("Show Palette")]
    public bool ShowPalette { get; set; } = true;

    /// <summary>
    /// The recently-used symbol strip, in the order it is drawn. Not an Options setting — it is
    /// written by the palette as you work, and persisted because a strip that emptied itself every
    /// time the app restarted would never be worth learning.
    /// </summary>
    public List<RecentSymbol> RecentSymbols { get; set; } = [];

    /// <summary>Options-panel sources.</summary>
    public static IEnumerable<string> GetModeOptions() => ["Calc", "Latex", "Text"];

    /// <inheritdoc cref="GetModeOptions"/>
    public static IEnumerable<string> GetAngleOptions() => ["Degrees", "Radians"];

    /// <inheritdoc cref="GetModeOptions"/>
    public static IEnumerable<string> GetDecimalOptions() => ["2", "4", "6", "8", "10", "15"];

    /// <summary>The configured start mode, defaulting to <see cref="DefinitionMode.Calc"/>.</summary>
    public DefinitionMode GetStartMode() =>
        Enum.TryParse<DefinitionMode>(StartMode, ignoreCase: true, out var mode) ? mode : DefinitionMode.Calc;

    /// <summary>The configured angle unit.</summary>
    public AngleUnit GetAngleUnit() =>
        string.Equals(Angles, "Radians", StringComparison.OrdinalIgnoreCase) ? AngleUnit.Radians : AngleUnit.Degrees;

    /// <summary>The configured rounding, clamped to something a formatter will accept.</summary>
    public int GetDecimalPlaces() =>
        int.TryParse(DecimalPlaces, out var places) ? Math.Clamp(places, 0, 15) : 6;
}
