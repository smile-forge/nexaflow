using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nexaflow.Features.Tabular.Detection;

/// <summary>
/// Parses individual cell strings into the most specific applicable <see cref="CsvDataType"/>,
/// and aggregates per-cell types across rows into a single column type.
/// </summary>
public static class ColumnTypeInferrer
{
    public static CsvDataType ClassifyCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return CsvDataType.String; // unknown — caller filters out blanks
        var v = value!.Trim();

        if (IsBoolean(v))                                                 return CsvDataType.Boolean;
        if (long.TryParse(v, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
                                                                          return CsvDataType.Integer;
        if (TryParseCurrency(v))                                          return CsvDataType.Currency;
        if (decimal.TryParse(v, NumberStyles.Number | NumberStyles.AllowExponent | NumberStyles.AllowParentheses,
                             CultureInfo.InvariantCulture, out _))        return CsvDataType.Decimal;
        if (TryParseDateTime(v, out var hasTime, out var hasDate))
            return hasDate && hasTime ? CsvDataType.DateTime
                 : hasDate            ? CsvDataType.Date
                 :                      CsvDataType.Time;
        return CsvDataType.String;
    }

    public static CsvDataType AggregateColumn(IEnumerable<string?> values)
    {
        CsvDataType? best = null;
        bool anyNonEmpty  = false;
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            anyNonEmpty = true;
            var t = ClassifyCell(v);
            best = best is null ? t : Promote(best.Value, t);
        }
        return anyNonEmpty ? best!.Value : CsvDataType.String;
    }

    /// <summary>
    /// When two cell classifications conflict, demote to the narrowest type that fits both.
    /// </summary>
    public static CsvDataType Promote(CsvDataType a, CsvDataType b)
    {
        if (a == b) return a;
        // Integer + Decimal/Currency → Decimal
        if ((a == CsvDataType.Integer  && b == CsvDataType.Decimal) ||
            (a == CsvDataType.Decimal  && b == CsvDataType.Integer)) return CsvDataType.Decimal;
        if ((a == CsvDataType.Integer  && b == CsvDataType.Currency) ||
            (a == CsvDataType.Currency && b == CsvDataType.Integer)) return CsvDataType.Currency;
        if ((a == CsvDataType.Decimal  && b == CsvDataType.Currency) ||
            (a == CsvDataType.Currency && b == CsvDataType.Decimal)) return CsvDataType.Currency;
        // Date + DateTime → DateTime
        if ((a == CsvDataType.Date && b == CsvDataType.DateTime) ||
            (a == CsvDataType.DateTime && b == CsvDataType.Date)) return CsvDataType.DateTime;
        // Anything else conflicts → String
        return CsvDataType.String;
    }

    private static bool IsBoolean(string v) =>
        v.Equals("true",  StringComparison.OrdinalIgnoreCase) ||
        v.Equals("false", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("yes",   StringComparison.OrdinalIgnoreCase) ||
        v.Equals("no",    StringComparison.OrdinalIgnoreCase) ||
        v.Equals("y",     StringComparison.OrdinalIgnoreCase) ||
        v.Equals("n",     StringComparison.OrdinalIgnoreCase);

    private static bool TryParseCurrency(string v)
    {
        if (v.Length < 2) return false;
        char first = v[0], last = v[^1];
        bool prefix = first is '$' or '€' or '£' or '¥' or '₹' or '¤';
        bool suffix = last  is '$' or '€' or '£' or '¥' or '₹' or '¤';
        if (!prefix && !suffix) return false;
        var stripped = prefix ? v[1..] : v[..^1];
        return decimal.TryParse(stripped, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy",
        "dd/MM/yyyy", "d/M/yyyy",   "dd-MM-yyyy", "yyyyMMdd",
        "dd.MM.yyyy", "d.M.yyyy",
    };
    private static readonly string[] TimeFormats =
    {
        "HH:mm:ss", "HH:mm", "H:mm:ss", "H:mm", "hh:mm:ss tt", "h:mm tt"
    };
    private static readonly string[] DateTimeFormats =
    {
        "yyyy-MM-ddTHH:mm:ss",  "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",     "MM/dd/yyyy HH:mm:ss",  "M/d/yyyy h:mm:ss tt",
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "dd.MM.yyyy HH:mm:ss",  "d.M.yyyy H:mm:ss",     "dd.MM.yyyy HH:mm",
    };

    private static bool TryParseDateTime(string v, out bool hasTime, out bool hasDate)
    {
        hasTime = false; hasDate = false;
        if (DateTime.TryParseExact(v, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
        { hasTime = true; hasDate = true; return true; }
        if (DateTime.TryParseExact(v, DateFormats,     CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        { hasDate = true; return true; }
        if (DateTime.TryParseExact(v, TimeFormats,     CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        { hasTime = true; return true; }
        return false;
    }
}
