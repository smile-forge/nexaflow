using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Features.Tabular.ViewModels;

/// <summary>
/// Per-column filter state. The orchestrator combines all column filters into a single
/// <c>RowFilter</c> by AND-ing each column's <see cref="Matches"/> over its raw cell text.
/// </summary>
public abstract class ColumnFilter
{
    public abstract bool IsActive { get; }
    public abstract bool Matches(string raw);

    public static ColumnFilter ForType(CsvDataType type) => type switch
    {
        CsvDataType.Integer or CsvDataType.Decimal or CsvDataType.Currency => new NumericColumnFilter(),
        CsvDataType.Date or CsvDataType.DateTime or CsvDataType.Time      => new DateColumnFilter(),
        CsvDataType.Boolean                                                => new BooleanColumnFilter(),
        _                                                                  => new StringColumnFilter(),
    };
}

public sealed class StringColumnFilter : ColumnFilter
{
    public string? Text     { get; set; }
    public bool    UseRegex { get; set; }

    public override bool IsActive => !string.IsNullOrEmpty(Text);

    public override bool Matches(string raw)
    {
        if (!IsActive) return true;
        if (UseRegex)
        {
            try { return Regex.IsMatch(raw, Text!, RegexOptions.IgnoreCase); }
            catch { return true; }
        }
        return raw.Contains(Text!, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class NumericColumnFilter : ColumnFilter
{
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }

    public override bool IsActive => Min.HasValue || Max.HasValue;

    public override bool Matches(string raw)
    {
        if (!IsActive) return true;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim('$', '€', '£', '¥', '₹', '¤', ' ');
        if (!decimal.TryParse(trimmed,
                              NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return false;
        if (Min.HasValue && v < Min.Value) return false;
        if (Max.HasValue && v > Max.Value) return false;
        return true;
    }
}

public sealed class DateColumnFilter : ColumnFilter
{
    public DateTime? From { get; set; }
    public DateTime? To   { get; set; }

    public override bool IsActive => From.HasValue || To.HasValue;

    public override bool Matches(string raw)
    {
        if (!IsActive) return true;
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d)) return false;
        if (From.HasValue && d < From.Value) return false;
        if (To.HasValue   && d > To.Value)   return false;
        return true;
    }
}

public sealed class BooleanColumnFilter : ColumnFilter
{
    public bool ShowTrue  { get; set; } = true;
    public bool ShowFalse { get; set; } = true;

    public override bool IsActive => !(ShowTrue && ShowFalse);

    public override bool Matches(string raw)
    {
        if (!IsActive) return true;
        bool? b = raw.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "1" => true,
            "false" or "no" or "n" or "0" => false,
            _ => (bool?)null,
        };
        if (b is null) return false;
        return b.Value ? ShowTrue : ShowFalse;
    }
}
