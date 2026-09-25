using System.Globalization;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Dates as Mermaid reads and writes them: in a format of day.js tokens — <c>YYYY-MM-DD</c>, <c>HH:mm</c>, <c>X</c> — which is
/// how a gantt chart's <c>dateFormat</c> says its dates are written.
///
/// <para>
/// Reading is strict, as Mermaid reads: a date is only a date where writing it back in the same format gives exactly what was
/// written, so <c>2014-1-5</c> is no <c>YYYY-MM-DD</c> date. A format that names no year, month or day takes them from today.
/// Dates are the wall clock's; a date written with its offset from UTC is moved onto this machine's clock.
/// </para>
/// </summary>
public static class MermaidDate
{
    /// <summary>The format dates are written in where nothing says otherwise.</summary>
    public const string Default = "YYYY-MM-DD";

    private static readonly string[] Months = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames[..12];
    private static readonly string[] ShortMonths = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames[..12];
    private static readonly string[] Days = CultureInfo.InvariantCulture.DateTimeFormat.DayNames;
    private static readonly string[] ShortDays = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames;

    /// <summary>
    /// What <paramref name="text"/> says as a date in <paramref name="format"/>, or null where it is not one — the day it falls on
    /// taken from <paramref name="today"/> where the format names none.
    /// </summary>
    public static DateTime? Read(string text, string format, DateTime? today = null)
    {
        (text, format) = (text.Trim(), format.Trim());
        if (text.Length == 0 || format.Length == 0) return null;

        var read = format is "X" or "x" ? Stamp(text, format) : Parsed(text, format, today ?? DateTime.Now);
        return read is { } date && Write(date, format) == text ? date : null;
    }

    /// <summary>
    /// What <paramref name="text"/> says as a date however it is written, as a browser reads one — for a start date not written in
    /// the chart's format — or null. A year past ten thousand either way is no date.
    /// </summary>
    public static DateTime? Loosely(string text) =>
        DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) && date.Year <= 10000
            ? date
            : null;

    /// <summary><paramref name="date"/> written in <paramref name="format"/>. Anything in square brackets is written as it is.</summary>
    public static string Write(DateTime date, string format)
    {
        var written = new System.Text.StringBuilder();
        var offset = TimeZoneInfo.Local.GetUtcOffset(date);

        for (var at = 0; at < format.Length;)
        {
            if (format[at] == '[' && format.IndexOf(']', at + 1) is var close and > 0)
            {
                written.Append(format, at + 1, close - at - 1);
                at = close + 1;
                continue;
            }

            var token = WriteToken(format, at);
            written.Append(token switch
            {
                "Q" => ((date.Month + 2) / 3).ToString(CultureInfo.InvariantCulture),
                "Do" => Ordinal(date.Day),
                "X" => Seconds(date).ToString(CultureInfo.InvariantCulture),
                "x" => Milliseconds(date).ToString(CultureInfo.InvariantCulture),
                "k" => (date.Hour == 0 ? 24 : date.Hour).ToString(CultureInfo.InvariantCulture),
                "kk" => (date.Hour == 0 ? 24 : date.Hour).ToString("00", CultureInfo.InvariantCulture),
                "YY" => (date.Year % 100).ToString("00", CultureInfo.InvariantCulture),
                "YYYY" => date.Year.ToString("0000", CultureInfo.InvariantCulture),
                "M" => date.Month.ToString(CultureInfo.InvariantCulture),
                "MM" => date.Month.ToString("00", CultureInfo.InvariantCulture),
                "MMM" => ShortMonths[date.Month - 1],
                "MMMM" => Months[date.Month - 1],
                "D" => date.Day.ToString(CultureInfo.InvariantCulture),
                "DD" => date.Day.ToString("00", CultureInfo.InvariantCulture),
                "DDD" => date.DayOfYear.ToString(CultureInfo.InvariantCulture),
                "DDDD" => date.DayOfYear.ToString("000", CultureInfo.InvariantCulture),
                "d" => ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                "dd" => Days[(int)date.DayOfWeek][..2],
                "ddd" => ShortDays[(int)date.DayOfWeek],
                "dddd" => Days[(int)date.DayOfWeek],
                "H" => date.Hour.ToString(CultureInfo.InvariantCulture),
                "HH" => date.Hour.ToString("00", CultureInfo.InvariantCulture),
                "h" => Twelve(date.Hour).ToString(CultureInfo.InvariantCulture),
                "hh" => Twelve(date.Hour).ToString("00", CultureInfo.InvariantCulture),
                "a" => date.Hour < 12 ? "am" : "pm",
                "A" => date.Hour < 12 ? "AM" : "PM",
                "m" => date.Minute.ToString(CultureInfo.InvariantCulture),
                "mm" => date.Minute.ToString("00", CultureInfo.InvariantCulture),
                "s" => date.Second.ToString(CultureInfo.InvariantCulture),
                "ss" => date.Second.ToString("00", CultureInfo.InvariantCulture),
                "S" => (date.Millisecond / 100).ToString(CultureInfo.InvariantCulture),
                "SS" => (date.Millisecond / 10).ToString("00", CultureInfo.InvariantCulture),
                "SSS" => date.Millisecond.ToString("000", CultureInfo.InvariantCulture),
                "Z" => Offset(offset, ":"),
                "ZZ" => Offset(offset, string.Empty),
                _ => token,
            });

            at += token.Length;
        }

        return written.ToString();
    }

    /// <summary>How many milliseconds after the start of 1970, on this machine's clock, <paramref name="date"/> is.</summary>
    public static long Milliseconds(DateTime date) =>
        new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)).ToUnixTimeMilliseconds();

    /// <summary>The date <paramref name="milliseconds"/> after the start of 1970, on this machine's clock.</summary>
    public static DateTime FromMilliseconds(double milliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(milliseconds)).LocalDateTime;

    private static long Seconds(DateTime date) => (long)Math.Floor(Milliseconds(date) / 1000d);

    private static int Twelve(int hour) => hour % 12 == 0 ? 12 : hour % 12;

    private static string Ordinal(int day)
    {
        var suffix = (day % 100) is 11 or 12 or 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return day.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string Offset(TimeSpan offset, string between) =>
        (offset < TimeSpan.Zero ? "-" : "+") + Math.Abs(offset.Hours).ToString("00", CultureInfo.InvariantCulture) + between
        + Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>The token a format writes at <paramref name="at"/>: the longest day.js token there, or the one character.</summary>
    private static string WriteToken(string format, int at)
    {
        foreach (var token in (string[])["Do", "kk", "k", "Q", "X", "x", "YYYY", "YY", "MMMM", "MMM", "MM", "M", "DDDD", "DDD", "DD", "D", "dddd", "ddd", "dd", "d",
                                         "HH", "H", "hh", "h", "a", "A", "mm", "m", "ss", "s", "SSS", "SS", "S", "ZZ", "Z"])
            if (string.CompareOrdinal(format, at, token, 0, token.Length) == 0)
                return token;

        return format.Substring(at, 1);
    }

    /// <summary>A date written as seconds (<c>X</c>) or milliseconds (<c>x</c>) since the start of 1970.</summary>
    private static DateTime? Stamp(string text, string format) =>
        double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var stamp)
        && Math.Abs(stamp) < 1e14
            ? FromMilliseconds(format == "X" ? stamp * 1000 : stamp)
            : null;

    /// <summary>A date read token by token, as day.js reads one: the tokens it knows, the separators between them skipped.</summary>
    private static DateTime? Parsed(string text, string format, DateTime today)
    {
        int? year = null, month = null, day = null, offset = null, dayOfYear = null;
        int hours = 0, minutes = 0, seconds = 0, milliseconds = 0;
        bool? afternoon = null;
        var at = 0;

        for (var f = 0; f < format.Length;)
        {
            if (format[f] == '[' && format.IndexOf(']', f + 1) is var close and > 0)
            {
                at += close - f - 1;
                f = close + 1;
                continue;
            }

            if (format[f] is '-' or '_' or ':' or '/' or '.' or ',' or '(' or ')' || char.IsWhiteSpace(format[f]))
            {
                at++;
                f++;
                continue;
            }

            var token = ParseToken(format, f);
            f += Math.Max(1, token.Length);
            if (token.Length == 0) continue;

            if (at > text.Length) return null;
            var rest = text.AsSpan(at);

            switch (token)
            {
                case "A" or "a":
                    var word = Word(rest);
                    if (word.Length == 0) return null;
                    afternoon = word.Equals("pm", StringComparison.OrdinalIgnoreCase);
                    at += word.Length;
                    break;

                case "Do":
                    var ordinal = Word(rest);
                    var digits = Digits(ordinal, 1, 2);
                    if (digits.Length == 0) return null;
                    day = int.Parse(digits, CultureInfo.InvariantCulture);
                    at += ordinal.Length;
                    break;

                case "MMM" or "MMMM":
                    var name = Word(rest).ToString();
                    var index = Array.FindIndex(token == "MMM" ? ShortMonths : Months, known => known == name);
                    if (index < 0) return null;
                    month = index + 1;
                    at += name.Length;
                    break;

                case "Z" or "ZZ":
                    if (rest.StartsWith("Z")) { offset = 0; at++; break; }
                    if (rest.Length < 3 || rest[0] is not ('+' or '-')) return null;
                    var sign = rest[0] == '-' ? -1 : 1;
                    var hh = Digits(rest[1..], 2, 2);
                    if (hh.Length < 2) return null;
                    var used = 3;
                    if (rest.Length > used && rest[used] == ':') used++;
                    var mm = rest.Length > used ? Digits(rest[used..], 2, 2) : [];
                    used += mm.Length;
                    offset = sign * ((int.Parse(hh, CultureInfo.InvariantCulture) * 60) + (mm.Length == 2 ? int.Parse(mm, CultureInfo.InvariantCulture) : 0));
                    at += used;
                    break;

                default:
                    var (least, most) = token switch
                    {
                        "YYYY" => (4, 4), "YY" => (2, 2), "Y" => (1, 9), "Q" or "S" => (1, 1), "SS" => (2, 2), "SSS" or "DDDD" => (3, 3), "DDD" => (1, 3),
                        "MM" or "DD" or "HH" or "hh" or "mm" or "ss" => (2, 2),
                        _ => (1, 2),
                    };
                    var number = Digits(rest, least, most);
                    if (number.Length < least) return null;
                    var value = int.Parse(number, CultureInfo.InvariantCulture);
                    at += number.Length;

                    switch (token)
                    {
                        case "YYYY" or "Y": year = value; break;
                        case "YY": year = value + (value > 68 ? 1900 : 2000); break;
                        case "Q": month = ((value - 1) * 3) + 1; break;
                        case "M" or "MM": month = value; break;
                        case "D" or "DD": day = value; break;
                        case "DDD" or "DDDD": dayOfYear = value; break;
                        case "H" or "HH" or "h" or "hh": hours = value; break;
                        case "m" or "mm": minutes = value; break;
                        case "s" or "ss": seconds = value; break;
                        case "S": milliseconds = value * 100; break;
                        case "SS": milliseconds = value * 10; break;
                        case "SSS": milliseconds = value; break;
                    }

                    break;
            }
        }

        if (afternoon is { } pm) hours = pm && hours < 12 ? hours + 12 : !pm && hours == 12 ? 0 : hours;

        var y = year ?? today.Year;
        var d = day ?? (year is null && month is null ? today.Day : 1);
        var m = year is not null && month is null ? 1 : month ?? today.Month;
        if (y is < 1 or > 9998) return null;

        // A day of the year counts from the year's first day, whatever month or day is written with it.
        var date = (dayOfYear is { } counted ? new DateTime(y, 1, 1).AddDays(counted - 1) : new DateTime(y, 1, 1).AddMonths(m - 1).AddDays(d - 1))
            .AddHours(hours).AddMinutes(minutes).AddSeconds(seconds).AddMilliseconds(milliseconds);

        return offset is { } minutesEast
            ? DateTime.SpecifyKind(date.AddMinutes(-minutesEast), DateTimeKind.Utc).ToLocalTime()
            : date;
    }

    /// <summary>The day.js token a format reads at <paramref name="at"/> — or nothing, for a character it skips.</summary>
    private static string ParseToken(string format, int at)
    {
        foreach (var token in (string[])["A", "a", "Q", "YYYY", "YY", "Y", "MMMM", "MMM", "MM", "M", "Do", "DDDD", "DDD", "DD", "D", "hh", "h", "HH", "H",
                                         "mm", "m", "ss", "s", "SSS", "SS", "S", "ZZ", "Z"])
            if (string.CompareOrdinal(format, at, token, 0, token.Length) == 0)
                return token;

        return string.Empty;
    }

    private static ReadOnlySpan<char> Digits(ReadOnlySpan<char> text, int least, int most)
    {
        var length = 0;
        while (length < most && length < text.Length && char.IsAsciiDigit(text[length])) length++;
        return length < least ? [] : text[..length];
    }

    /// <summary>A word as day.js matches one: digits, then anything that is no digit, separator or space.</summary>
    private static ReadOnlySpan<char> Word(ReadOnlySpan<char> text)
    {
        var length = 0;
        while (length < text.Length && char.IsAsciiDigit(text[length])) length++;
        while (length < text.Length && !char.IsAsciiDigit(text[length]) && text[length] is not ('-' or '_' or ':' or '/' or ',' or '(' or ')')
               && !char.IsWhiteSpace(text[length])) length++;
        return text[..length];
    }
}

/// <summary>
/// A length of time as Mermaid writes one: a number and its unit — <c>500ms</c>, <c>30s</c>, <c>30m</c>, <c>4h</c>, <c>3d</c>,
/// <c>2w</c>, <c>1M</c>, <c>1y</c> — the number whole or with decimals.
/// </summary>
public static class MermaidDuration
{
    /// <summary>What <paramref name="text"/> says as a length of time, or null where it is none.</summary>
    public static (double Amount, string Unit)? Read(string text)
    {
        text = text.Trim();
        var unit = text.EndsWith("ms", StringComparison.Ordinal) ? "ms"
                 : text.Length > 0 && text[^1] is 'M' or 'd' or 'h' or 'm' or 's' or 'w' or 'y' ? text[^1..] : null;
        if (unit is null) return null;

        var number = text[..^unit.Length];
        var point = number.IndexOf('.');
        var whole = point < 0 ? number : number[..point];
        var fraction = point < 0 ? "1" : number[(point + 1)..];

        return whole.Length > 0 && fraction.Length > 0 && whole.All(char.IsAsciiDigit) && fraction.All(char.IsAsciiDigit)
               && double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? (amount, unit)
            : null;
    }

    /// <summary>
    /// <paramref name="start"/> moved on by the length of time: months and years by whole ones, days and weeks by the nearest whole
    /// day, and anything shorter to the nearest millisecond — as day.js adds them.
    /// </summary>
    public static DateTime After(DateTime start, (double Amount, string Unit) length)
    {
        var (amount, unit) = length;
        try
        {
            return unit switch
            {
                "y" => start.AddYears((int)Math.Truncate(amount)),
                "M" => start.AddMonths((int)Math.Truncate(amount)),
                "w" => start.AddDays(Math.Floor((amount * 7) + 0.5)),
                "d" => start.AddDays(Math.Floor(amount + 0.5)),
                "h" => start.AddMilliseconds(Math.Floor((amount * 3_600_000) + 0.5)),
                "m" => start.AddMilliseconds(Math.Floor((amount * 60_000) + 0.5)),
                "s" => start.AddMilliseconds(Math.Floor((amount * 1000) + 0.5)),
                _ => start.AddMilliseconds(Math.Floor(amount + 0.5)),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return start;
        }
    }
}

/// <summary>
/// Dates written as a Mermaid axis writes them: in a d3 time format of <c>%</c> directives — <c>%Y-%m-%d</c>, <c>%H:%M</c>, <c>%b %e</c>
/// — in d3's en-US locale. A directive's padding may be changed after its <c>%</c>: <c>-</c> for none, <c>_</c> for spaces,
/// <c>0</c> for zeros.
/// </summary>
public static class MermaidTimeFormat
{
    private static readonly DateTimeFormatInfo English = CultureInfo.InvariantCulture.DateTimeFormat;

    /// <summary><paramref name="date"/> written in <paramref name="format"/>.</summary>
    public static string Write(DateTime date, string format)
    {
        var written = new System.Text.StringBuilder();

        for (var at = 0; at < format.Length; at++)
        {
            if (format[at] != '%' || at + 1 >= format.Length)
            {
                written.Append(format[at]);
                continue;
            }

            var directive = format[++at];
            char? pad = null;
            if (directive is '-' or '_' or '0' && at + 1 < format.Length)
            {
                pad = directive;
                directive = format[++at];
            }

            written.Append(Directive(date, directive, pad));
        }

        return written.ToString();
    }

    private static string Directive(DateTime date, char directive, char? pad)
    {
        var yday = date.DayOfYear - 1;
        var wday = (int)date.DayOfWeek;

        return directive switch
        {
            'a' => English.AbbreviatedDayNames[wday],
            'A' => English.DayNames[wday],
            'b' => English.AbbreviatedMonthNames[date.Month - 1],
            'B' => English.MonthNames[date.Month - 1],
            'c' => Write(date, "%-m/%-d/%Y, %-I:%M:%S %p"),
            'd' => Pad(date.Day, pad ?? '0', 2),
            'e' => Pad(date.Day, pad ?? ' ', 2),
            'f' => Pad(date.Millisecond * 1000, pad ?? '0', 6),
            'g' => Pad(ISOWeek.GetYear(date) % 100, pad ?? '0', 2),
            'G' => Pad(ISOWeek.GetYear(date) % 10000, pad ?? '0', 4),
            'H' => Pad(date.Hour, pad ?? '0', 2),
            'I' => Pad(date.Hour % 12 == 0 ? 12 : date.Hour % 12, pad ?? '0', 2),
            'j' => Pad(date.DayOfYear, pad ?? '0', 3),
            'L' => Pad(date.Millisecond, pad ?? '0', 3),
            'm' => Pad(date.Month, pad ?? '0', 2),
            'M' => Pad(date.Minute, pad ?? '0', 2),
            'p' => date.Hour < 12 ? "AM" : "PM",
            'q' => ((date.Month + 2) / 3).ToString(CultureInfo.InvariantCulture),
            'Q' => MermaidDate.Milliseconds(date).ToString(CultureInfo.InvariantCulture),
            's' => ((long)Math.Floor(MermaidDate.Milliseconds(date) / 1000d)).ToString(CultureInfo.InvariantCulture),
            'S' => Pad(date.Second, pad ?? '0', 2),
            'u' => (wday == 0 ? 7 : wday).ToString(CultureInfo.InvariantCulture),
            'U' => Pad((yday + 7 - wday) / 7, pad ?? '0', 2),
            'V' => Pad(ISOWeek.GetWeekOfYear(date), pad ?? '0', 2),
            'w' => wday.ToString(CultureInfo.InvariantCulture),
            'W' => Pad((yday + 7 - ((wday + 6) % 7)) / 7, pad ?? '0', 2),
            'x' => Write(date, "%-m/%-d/%Y"),
            'X' => Write(date, "%-I:%M:%S %p"),
            'y' => Pad(date.Year % 100, pad ?? '0', 2),
            'Y' => Pad(date.Year % 10000, pad ?? '0', 4),
            'Z' => Zone(TimeZoneInfo.Local.GetUtcOffset(date)),
            '%' => "%",
            _ => directive.ToString(),
        };
    }

    private static string Pad(int value, char pad, int width)
    {
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        var sign = value < 0 ? "-" : string.Empty;
        return pad == '-' ? sign + digits : sign + digits.PadLeft(width, pad == '_' ? ' ' : pad);
    }

    private static string Zone(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + Math.Abs(offset.Hours).ToString("00", CultureInfo.InvariantCulture)
        + Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture);
}
