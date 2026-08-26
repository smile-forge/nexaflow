using System.Globalization;
using System.Text;

namespace Nexaflow.Features.Solver.Solving;

/// <summary>
/// Descriptive statistics over a list of numbers pasted into the definition area.
/// <para>
/// This is the one solver that does not go near the algebra engine: a series is not an expression,
/// and <c>4, 8, 15</c> is not something to parse as maths.
/// </para>
/// </summary>
public sealed class StatsSolver : ISolver
{
    /// <summary>Two values is the smallest series where any of these say anything.</summary>
    private const int MinValues = 2;

    /// <inheritdoc/>
    public string Id => "stats";

    /// <inheritdoc/>
    public string DisplayName => "Statistics";

    /// <inheritdoc/>
    public int Order => 30;

    /// <inheritdoc/>
    public IReadOnlyList<SolverChip> CanSolve(SolverInput input)
    {
        if (!TryParseSeries(input.Trimmed, out var values)) return [];

        return
        [
            new SolverChip(Id, "summary", "stats", "Σ", $"Every statistic for these {values.Count} values", values),
            new SolverChip(Id, "sum", "sum", "+", "Add them up", values),
            new SolverChip(Id, "mean", "avg", "x̄", "The arithmetic mean", values),
            new SolverChip(Id, "median", "median", "‖", "The middle value", values),
            new SolverChip(Id, "stddev", "σ", "σ", "Sample standard deviation", values),
        ];
    }

    /// <inheritdoc/>
    public Task<SolverResult> SolveAsync(SolverChip chip, SolverInput input, CancellationToken ct)
    {
        if (chip.Payload is not IReadOnlyList<double> values || values.Count < MinValues)
            return Task.FromResult(SolverResult.Error("The definition changed — press the chip again."));

        var d = input.Decimals;

        return Task.FromResult(chip.Id switch
        {
            "summary" => new SolverResult(Summary(values, d)),
            "sum" => Single("Sum", values.Sum(), d),
            "mean" => Single("Mean", Mean(values), d),
            "median" => Single("Median", Median(values), d),
            "stddev" => Single("Standard deviation (sample)", StdDev(values), d),
            _ => SolverResult.Error($"Unknown statistic '{chip.Id}'."),
        });
    }

    /// <summary>
    /// Reads a series of numbers separated by commas, whitespace or newlines. Returns false unless
    /// the whole definition is numbers — a single stray word means this is prose, not data.
    /// </summary>
    public static bool TryParseSeries(string text, out IReadOnlyList<double> values)
    {
        values = [];
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < MinValues) return false;

        var parsed = new List<double>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            parsed.Add(v);
        }

        values = parsed;
        return true;
    }

    private static SolverResult Single(string label, double value, int decimals)
        => new($"$$\n\\text{{{label}}} = {Format(value, decimals)}\n$$");

    private static string Summary(IReadOnlyList<double> v, int decimals)
    {
        var sorted = v.OrderBy(x => x).ToArray();
        var mean = Mean(v);

        var sb = new StringBuilder();
        sb.Append("| Statistic | Value |\n|---|---:|\n");
        Row(sb, "Count", v.Count.ToString(CultureInfo.InvariantCulture));
        Row(sb, "Sum", Format(v.Sum(), decimals));
        Row(sb, "Mean", Format(mean, decimals));
        Row(sb, "Median", Format(Median(v), decimals));
        Row(sb, "Minimum", Format(sorted[0], decimals));
        Row(sb, "Maximum", Format(sorted[^1], decimals));
        Row(sb, "Range", Format(sorted[^1] - sorted[0], decimals));
        Row(sb, "Variance (sample)", Format(Variance(v), decimals));
        Row(sb, "Std deviation (sample)", Format(StdDev(v), decimals));
        return sb.ToString();

        static void Row(StringBuilder sb, string name, string value)
            => sb.Append("| ").Append(name).Append(" | ").Append(value).Append(" |\n");
    }

    private static double Mean(IReadOnlyList<double> v) => v.Sum() / v.Count;

    private static double Median(IReadOnlyList<double> v)
    {
        var sorted = v.OrderBy(x => x).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Sample variance — the n−1 divisor, which is what "standard deviation" means for data.</summary>
    private static double Variance(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return 0;
        var mean = Mean(v);
        return v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1);
    }

    private static double StdDev(IReadOnlyList<double> v) => Math.Sqrt(Variance(v));

    private static string Format(double value, int decimals)
    {
        var s = value.ToString($"F{Math.Clamp(decimals, 0, 15)}", CultureInfo.InvariantCulture);
        if (!s.Contains('.')) return s;
        s = s.TrimEnd('0').TrimEnd('.');
        return s.Length == 0 || s == "-" ? "0" : s;
    }
}
