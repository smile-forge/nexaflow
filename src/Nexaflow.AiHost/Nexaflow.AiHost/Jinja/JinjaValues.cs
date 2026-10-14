using System.Collections;
using System.Globalization;
using System.Text;

namespace Nexaflow.AiHost.Jinja;

/// <summary>A name or member a template asked for and the data does not have: it prints as nothing, is false, and
/// iterates as empty, but anything asked of it in turn is an error — Jinja's default undefined.</summary>
internal sealed class JinjaUndefined(string name)
{
    public string Name { get; } = name;

    public override string ToString() => "";
}

/// <summary>What <c>namespace(…)</c> makes: the one object a template may change, so a value set inside a loop
/// outlives it.</summary>
internal sealed class JinjaNamespace
{
    public Dictionary<string, object?> Attributes { get; } = new(StringComparer.Ordinal);
}

/// <summary>A tuple: a list Python writes in parentheses — what <c>items</c> and <c>dictsort</c> give, and
/// <c>(a, b)</c> makes.</summary>
internal sealed class JinjaTuple : List<object?>
{
    public JinjaTuple(IEnumerable<object?> items) : base(items) { }
}

/// <summary>A function a template can call — a built-in, or a method bound to its value.</summary>
internal delegate object? JinjaFunction(IReadOnlyList<object?> positional, IReadOnlyDictionary<string, object?> named);

internal sealed record JinjaCallable(string Name, JinjaFunction Invoke);

/// <summary>A macro, and the scope it was defined in, which it sees when called.</summary>
internal sealed record JinjaMacro(MacroNode Node, JinjaScope Scope);

/// <summary>The <c>loop</c> variable of a <c>for</c>.</summary>
internal sealed class JinjaLoop(IReadOnlyList<object?> items)
{
    public int Index0 { get; set; }

    public object? Member(string name) => name switch
    {
        "index0"    => (long)Index0,
        "index"     => (long)Index0 + 1,
        "revindex0" => (long)(items.Count - Index0 - 1),
        "revindex"  => (long)(items.Count - Index0),
        "first"     => Index0 == 0,
        "last"      => Index0 == items.Count - 1,
        "length"    => (long)items.Count,
        "previtem"  => Index0 > 0 ? items[Index0 - 1] : new JinjaUndefined("loop.previtem"),
        "nextitem"  => Index0 < items.Count - 1 ? items[Index0 + 1] : new JinjaUndefined("loop.nextitem"),
        "depth"     => 1L,
        "depth0"    => 0L,
        "cycle"     => new JinjaCallable("loop.cycle", (args, _) => args.Count == 0 ? null : args[Index0 % args.Count]),
        _           => new JinjaUndefined("loop." + name),
    };
}

/// <summary>
/// Python's rules for the values a template handles — what is true, how a value prints, how two compare, what <c>+</c>
/// does — since a template is written against them. Values are <see cref="string"/>, <see cref="long"/>,
/// <see cref="double"/>, <see cref="bool"/>, null for None, <see cref="IList{T}"/> of object for lists and tuples, and
/// <see cref="IDictionary{TKey,TValue}"/> of string to object for mappings, in insertion order.
/// </summary>
internal static class JinjaValues
{
    public static bool IsUndefined(object? value) => value is JinjaUndefined;

    public static bool IsTrue(object? value) => value switch
    {
        null or JinjaUndefined => false,
        bool b                 => b,
        long l                 => l != 0,
        double d               => d != 0,
        string s               => s.Length > 0,
        IDictionary<string, object?> d => d.Count > 0,
        IList<object?> l       => l.Count > 0,
        _                      => true,
    };

    public static bool IsMapping(object? value) => value is IDictionary<string, object?>;

    public static bool IsList(object? value) => value is IList<object?>;

    public static bool IsNumber(object? value) => value is long or double or bool;

    /// <summary>How a value prints: Python's <c>str()</c>.</summary>
    public static string Str(object? value) => value switch
    {
        null              => "None",
        JinjaUndefined    => "",
        string s          => s,
        bool b            => b ? "True" : "False",
        long l            => l.ToString(CultureInfo.InvariantCulture),
        double d          => Float(d),
        _                 => Repr(value),
    };

    /// <summary>Python's <c>repr()</c>: strings quoted, containers written out.</summary>
    public static string Repr(object? value) => value switch
    {
        string s                        => QuoteRepr(s),
        IDictionary<string, object?> d  => "{" + string.Join(", ", d.Select(p => QuoteRepr(p.Key) + ": " + Repr(p.Value))) + "}",
        JinjaTuple { Count: 1 } t       => "(" + Repr(t[0]) + ",)",
        JinjaTuple t                    => "(" + string.Join(", ", t.Select(Repr)) + ")",
        IList<object?> l                => "[" + string.Join(", ", l.Select(Repr)) + "]",
        JinjaNamespace n                => "<Namespace " + Repr(n.Attributes.ToDictionary(p => p.Key, p => p.Value)) + ">",
        JinjaMacro m                    => $"<Macro '{m.Node.Name}'>",
        JinjaCallable c                 => $"<function {c.Name}>",
        JinjaUndefined                  => "Undefined",
        _                               => Str(value),
    };

    private static string QuoteRepr(string s)
    {
        char quote = s.Contains('\'') && !s.Contains('"') ? '"' : '\'';
        var text = new StringBuilder(s.Length + 2).Append(quote);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': text.Append(@"\\"); break;
                case '\n': text.Append(@"\n"); break;
                case '\r': text.Append(@"\r"); break;
                case '\t': text.Append(@"\t"); break;
                default:
                    if (c == quote) text.Append('\\').Append(c);
                    else if (c < 0x20 || c == 0x7f) text.Append(@"\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    else text.Append(c);
                    break;
            }
        }
        return text.Append(quote).ToString();
    }

    /// <summary>A float as Python writes one: the shortest digits that read back, positional between 1e-4 and 1e16
    /// (with <c>.0</c> when whole), scientific outside.</summary>
    public static string Float(double value)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsInfinity(value)) return value > 0 ? "inf" : "-inf";
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";

        // The shortest round-trip digits, and where the decimal point goes.
        string shortest = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int e = shortest.IndexOfAny(['E', 'e']);
        string mantissa = e >= 0 ? shortest[..e] : shortest;
        int shift = e >= 0 ? int.Parse(shortest[(e + 1)..], CultureInfo.InvariantCulture) : 0;
        int dot = mantissa.IndexOf('.');
        string whole = dot >= 0 ? mantissa[..dot] : mantissa;
        string fraction = dot >= 0 ? mantissa[(dot + 1)..] : "";
        string digits = (whole + fraction).TrimStart('0');
        int leadingZeros = (whole + fraction).Length - digits.Length;
        int exponent = whole.Length - leadingZeros - 1 + shift;
        digits = digits.TrimEnd('0');
        if (digits.Length == 0) digits = "0";

        string sign = value < 0 ? "-" : "";
        if (exponent is >= -4 and < 16)
        {
            if (exponent >= digits.Length - 1) return sign + digits + new string('0', exponent - digits.Length + 1) + ".0";
            if (exponent >= 0) return sign + digits[..(exponent + 1)] + "." + digits[(exponent + 1)..];
            return sign + "0." + new string('0', -exponent - 1) + digits;
        }
        string body = digits.Length == 1 ? digits : digits[0] + "." + digits[1..];
        return sign + body + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }

    public static bool Equal(object? a, object? b)
    {
        if (a is JinjaUndefined || b is JinjaUndefined) return a is JinjaUndefined && b is JinjaUndefined;
        if (a is null || b is null) return a is null && b is null;
        if (IsNumber(a) && IsNumber(b)) return ToDouble(a) == ToDouble(b);
        if (a is string sa && b is string sb) return sa == sb;
        if (a is IList<object?> la && b is IList<object?> lb)
            return (a is JinjaTuple) == (b is JinjaTuple) && la.Count == lb.Count && la.Zip(lb).All(p => Equal(p.First, p.Second));
        if (a is IDictionary<string, object?> da && b is IDictionary<string, object?> db)
            return da.Count == db.Count && da.All(p => db.TryGetValue(p.Key, out var v) && Equal(p.Value, v));
        return ReferenceEquals(a, b) || a.Equals(b);
    }

    /// <summary>Python's ordering: numbers with numbers, strings by code point, lists item by item.</summary>
    public static int Compare(object? a, object? b, int line)
    {
        if (IsNumber(a) && IsNumber(b)) return ToDouble(a).CompareTo(ToDouble(b));
        if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
        if (a is IList<object?> la && b is IList<object?> lb)
        {
            for (int i = 0; i < Math.Min(la.Count, lb.Count); i++)
            {
                if (Equal(la[i], lb[i])) continue;
                return Compare(la[i], lb[i], line);
            }
            return la.Count.CompareTo(lb.Count);
        }
        throw new JinjaException($"{TypeName(a)} and {TypeName(b)} cannot be ordered.", line);
    }

    public static bool Contains(object? container, object? item, int line) => container switch
    {
        string s when item is string i         => s.Contains(i, StringComparison.Ordinal),
        string                                 => throw new JinjaException($"'in <string>' needs a string, not {TypeName(item)}.", line),
        IDictionary<string, object?> d         => item is string key && d.ContainsKey(key),
        IList<object?> l                       => l.Any(x => Equal(x, item)),
        JinjaUndefined u                       => throw new JinjaException($"'{u.Name}' is undefined.", line),
        _                                      => throw new JinjaException($"{TypeName(container)} cannot be searched with 'in'.", line),
    };

    public static long? Length(object? value) => value switch
    {
        string s                        => s.EnumerateRunes().Count(),
        IDictionary<string, object?> d  => d.Count,
        IList<object?> l                => l.Count,
        JinjaUndefined                  => 0,
        _                               => null,
    };

    /// <summary>What a <c>for</c> loops over: a list's items, a mapping's keys, a string's characters.</summary>
    public static IReadOnlyList<object?> Items(object? value, int line) => value switch
    {
        IList<object?> l                => [.. l],
        IDictionary<string, object?> d  => [.. d.Keys],
        string s                        => [.. s.EnumerateRunes().Select(r => (object?)r.ToString())],
        JinjaUndefined                  => [],
        null                            => throw new JinjaException("None cannot be looped over.", line),
        _                               => throw new JinjaException($"{TypeName(value)} cannot be looped over.", line),
    };

    public static double ToDouble(object? value) => value switch
    {
        long l   => l,
        double d => d,
        bool b   => b ? 1 : 0,
        _        => double.NaN,
    };

    public static string TypeName(object? value) => value switch
    {
        null                            => "None",
        JinjaUndefined u                => $"undefined '{u.Name}'",
        string                          => "a string",
        bool                            => "a boolean",
        long                            => "an integer",
        double                          => "a float",
        IDictionary<string, object?>    => "a mapping",
        IList<object?>                  => "a list",
        _                               => value.GetType().Name,
    };

    /// <summary>Python's <c>+ - * / // % **</c> and Jinja's <c>~</c>.</summary>
    public static object? Arithmetic(string op, object? left, object? right, int line)
    {
        if (op == "~") return Str(left) + Str(right);
        if (left is JinjaUndefined ul) throw new JinjaException($"'{ul.Name}' is undefined.", line);
        if (right is JinjaUndefined ur) throw new JinjaException($"'{ur.Name}' is undefined.", line);

        if (op == "+" && left is string ls && right is string rs) return ls + rs;
        if (op == "+" && left is IList<object?> ll && right is IList<object?> rl) return new List<object?>([.. ll, .. rl]);
        if (op == "*" && left is string repeated && right is long times) return string.Concat(Enumerable.Repeat(repeated, (int)Math.Max(0, times)));
        if (op == "*" && left is IList<object?> list && right is long count) return Enumerable.Repeat(list, (int)Math.Max(0, count)).SelectMany(x => x).ToList();
        if (op == "%" && left is string format) return Percent(format, right, line);

        if (!IsNumber(left) || !IsNumber(right))
            throw new JinjaException($"'{op}' does not apply to {TypeName(left)} and {TypeName(right)}.", line);

        if (left is not double && right is not double && op != "/")
        {
            long a = (long)ToDouble(left), b = (long)ToDouble(right);
            switch (op)
            {
                case "+":  return a + b;
                case "-":  return a - b;
                case "*":  return a * b;
                case "//": if (b == 0) throw new JinjaException("Division by zero.", line); return (long)Math.Floor((double)a / b);
                case "%":  if (b == 0) throw new JinjaException("Division by zero.", line); return ((a % b) + b) % b;
                case "**": return b >= 0 ? (object?)(long)Math.Pow(a, b) : Math.Pow(a, b);
            }
        }
        double x = ToDouble(left), y = ToDouble(right);
        return op switch
        {
            "+"  => x + y,
            "-"  => x - y,
            "*"  => x * y,
            "/"  => y == 0 ? throw new JinjaException("Division by zero.", line) : x / y,
            "//" => y == 0 ? throw new JinjaException("Division by zero.", line) : Math.Floor(x / y),
            "%"  => y == 0 ? throw new JinjaException("Division by zero.", line) : x - y * Math.Floor(x / y),
            "**" => Math.Pow(x, y),
            _    => throw new JinjaException($"Unknown operator '{op}'.", line),
        };
    }

    /// <summary>Python's <c>%</c> formatting, for the conversions templates use: <c>%s %r %d %i %f %%</c>.</summary>
    public static string Percent(string format, object? arguments, int line)
    {
        var values = arguments is IList<object?> list ? list : [arguments];
        var text = new StringBuilder();
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                text.Append(format[i]);
                continue;
            }
            char conversion = format[++i];
            if (conversion == '%')
            {
                text.Append('%');
                continue;
            }
            if (next >= values.Count) throw new JinjaException("Not enough arguments for the format string.", line);
            object? value = values[next++];
            text.Append(conversion switch
            {
                's'       => Str(value),
                'r'       => Repr(value),
                'd' or 'i' => ((long)ToDouble(value)).ToString(CultureInfo.InvariantCulture),
                'f'       => ToDouble(value).ToString("F6", CultureInfo.InvariantCulture),
                _         => throw new JinjaException($"'%{conversion}' is not a format templates can use.", line),
            });
        }
        return text.ToString();
    }

    /// <summary>How <c>json.dumps</c> is asked to write: its <c>indent</c>, <c>separators</c>, <c>sort_keys</c> and
    /// <c>ensure_ascii</c>. With no separators given, items are parted by <c>", "</c> on one line, or <c>","</c> when
    /// indented, and keys by <c>": "</c>.</summary>
    public sealed record JsonStyle(int? Indent = null, string? ItemSeparator = null, string KeySeparator = ": ", bool SortKeys = false,
                                   bool EnsureAscii = false)
    {
        public string Items => ItemSeparator ?? (Indent is null ? ", " : ",");
    }

    /// <summary>A value as JSON, as transformers' <c>tojson</c> writes it — <c>json.dumps</c>, non-ASCII kept unless
    /// asked otherwise.</summary>
    public static string Json(object? value, JsonStyle style, int line)
    {
        var text = new StringBuilder();
        WriteJson(text, value, style, 0, line);
        return text.ToString();
    }

    private static void WriteJson(StringBuilder text, object? value, JsonStyle style, int depth, int line)
    {
        switch (value)
        {
            case null: text.Append("null"); return;
            case bool b: text.Append(b ? "true" : "false"); return;
            case long l: text.Append(l.ToString(CultureInfo.InvariantCulture)); return;
            case double d:
                text.Append(double.IsNaN(d) ? "NaN" : double.IsInfinity(d) ? (d > 0 ? "Infinity" : "-Infinity") : Float(d));
                return;
            case string s: JsonString(text, s, style.EnsureAscii); return;
            case IDictionary<string, object?> map:
                if (map.Count == 0) { text.Append("{}"); return; }
                text.Append('{');
                bool firstPair = true;
                var pairs = style.SortKeys ? map.OrderBy(p => p.Key, StringComparer.Ordinal) : map.AsEnumerable();
                foreach (var (key, item) in pairs)
                {
                    if (!firstPair) text.Append(style.Items);
                    firstPair = false;
                    NewLine(text, style.Indent, depth + 1);
                    JsonString(text, key, style.EnsureAscii);
                    text.Append(style.KeySeparator);
                    WriteJson(text, item, style, depth + 1, line);
                }
                NewLine(text, style.Indent, depth);
                text.Append('}');
                return;
            case IList<object?> list:
                if (list.Count == 0) { text.Append("[]"); return; }
                text.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) text.Append(style.Items);
                    NewLine(text, style.Indent, depth + 1);
                    WriteJson(text, list[i], style, depth + 1, line);
                }
                NewLine(text, style.Indent, depth);
                text.Append(']');
                return;
            case JinjaUndefined u:
                throw new JinjaException($"'{u.Name}' is undefined and cannot be written as JSON.", line);
            default:
                throw new JinjaException($"{TypeName(value)} cannot be written as JSON.", line);
        }
    }

    private static void NewLine(StringBuilder text, int? indent, int depth)
    {
        if (indent is { } spaces) text.Append('\n').Append(' ', spaces * depth);
    }

    private static void JsonString(StringBuilder text, string s, bool ensureAscii)
    {
        text.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  text.Append("\\\""); break;
                case '\\': text.Append(@"\\"); break;
                case '\n': text.Append(@"\n"); break;
                case '\r': text.Append(@"\r"); break;
                case '\t': text.Append(@"\t"); break;
                case '\b': text.Append(@"\b"); break;
                case '\f': text.Append(@"\f"); break;
                default:
                    if (c < 0x20 || (ensureAscii && c > 0x7e)) text.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else text.Append(c);
                    break;
            }
        }
        text.Append('"');
    }

    /// <summary>Data from outside — JSON nodes, dictionaries, lists, numbers of any width — as template values.</summary>
    public static object? From(object? value) => value switch
    {
        null or string or bool or long or double or JinjaUndefined => value,
        int i                                  => (long)i,
        short s                                => (long)s,
        float f                                => (double)f,
        decimal m                              => (double)m,
        System.Text.Json.Nodes.JsonNode node   => FromJson(node),
        System.Text.Json.JsonElement element   => FromJson(System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText())),
        IDictionary<string, object?> map       => map,
        IList<object?> list                    => list,
        IEnumerable<KeyValuePair<string, object?>> pairs => Mapping(pairs.Select(p => (p.Key, From(p.Value)))),
        IEnumerable items when value is not string => items.Cast<object?>().Select(From).ToList(),
        _                                      => value,
    };

    private static object? FromJson(System.Text.Json.Nodes.JsonNode? node) => node switch
    {
        null => null,
        System.Text.Json.Nodes.JsonObject o => Mapping(o.Select(p => (p.Key, FromJson(p.Value)))),
        System.Text.Json.Nodes.JsonArray a  => a.Select(FromJson).ToList(),
        System.Text.Json.Nodes.JsonValue v  => v.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => v.GetValue<string>(),
            System.Text.Json.JsonValueKind.True   => true,
            System.Text.Json.JsonValueKind.False  => false,
            System.Text.Json.JsonValueKind.Number => v.TryGetValue<long>(out long l) ? (object?)l : v.GetValue<double>(),
            _                                     => null,
        },
        _ => null,
    };

    /// <summary>A mapping keeping its keys in the order given, as Python's dicts do.</summary>
    public static OrderedDictionary<string, object?> Mapping(IEnumerable<(string Key, object? Value)> pairs)
    {
        var map = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs) map[key] = value;
        return map;
    }
}
