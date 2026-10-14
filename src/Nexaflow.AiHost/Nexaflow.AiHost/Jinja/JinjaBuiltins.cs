using System.Globalization;
using System.Text;

namespace Nexaflow.AiHost.Jinja;

/// <summary>
/// What a template can reach beyond its own variables: the globals transformers gives chat templates
/// (<c>range</c>, <c>namespace</c>, <c>raise_exception</c>, <c>strftime_now</c>, <c>dict</c>), Jinja's filters and
/// tests, and the methods Python's strings, dicts and lists answer to. As in transformers' immutable sandbox, nothing
/// here changes a list or a dict — a template keeps state in a <c>namespace</c>.
/// </summary>
internal static class JinjaBuiltins
{
    private static readonly IReadOnlyDictionary<string, object?> NoNames = new Dictionary<string, object?>();

    public static object? Global(string name) => name switch
    {
        "range"           => new JinjaCallable(name, (args, _) => Range(args)),
        "namespace"       => new JinjaCallable(name, (args, named) => Namespace(args, named)),
        "dict"            => new JinjaCallable(name, (_, named) => JinjaValues.Mapping(named.Select(p => (p.Key, p.Value)))),
        "raise_exception" => new JinjaCallable(name, (args, _) => throw new JinjaException(args.Count > 0 ? JinjaValues.Str(args[0]) : "The template raised an exception.")),
        "strftime_now"    => new JinjaCallable(name, (args, _) => Strftime(DateTime.Now, args.Count > 0 ? JinjaValues.Str(args[0]) : "%Y-%m-%d")),
        _                 => null,
    };

    private static List<object?> Range(IReadOnlyList<object?> args)
    {
        long start = 0, stop, step = 1;
        switch (args.Count)
        {
            case 1: stop = Integer(args[0]); break;
            case 2: start = Integer(args[0]); stop = Integer(args[1]); break;
            case 3: start = Integer(args[0]); stop = Integer(args[1]); step = Integer(args[2]); break;
            default: throw new JinjaException("range() takes one to three arguments.");
        }
        if (step == 0) throw new JinjaException("range() step cannot be zero.");
        var items = new List<object?>();
        for (long i = start; step > 0 ? i < stop : i > stop; i += step)
        {
            items.Add(i);
            if (items.Count > 100_000) throw new JinjaException("range() is too long for a template.");
        }
        return items;
    }

    private static JinjaNamespace Namespace(IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?> named)
    {
        var ns = new JinjaNamespace();
        foreach (var arg in args)
            if (arg is IDictionary<string, object?> initial)
                foreach (var (key, value) in initial) ns.Attributes[key] = value;
        foreach (var (key, value) in named) ns.Attributes[key] = value;
        return ns;
    }

    private static long Integer(object? value) => value switch
    {
        long l   => l,
        bool b   => b ? 1 : 0,
        double d => (long)d,
        _        => throw new JinjaException($"Expected an integer, got {JinjaValues.TypeName(value)}."),
    };

    // ---- attributes, items, slices -----------------------------------------------------------------------------

    /// <summary><c>value.name</c>: Python's attribute first — a method of a string, dict or list — then the item of
    /// that name, as Jinja's <c>getattr</c> does.</summary>
    public static object? Attribute(object? value, string name, int line)
    {
        switch (value)
        {
            case JinjaUndefined undefined:
                throw new JinjaException($"'{undefined.Name}' is undefined, so it has no '{name}'.", line);
            case JinjaNamespace ns:
                return ns.Attributes.TryGetValue(name, out var attribute) ? attribute : new JinjaUndefined(name);
            case JinjaLoop loop:
                return loop.Member(name);
        }
        if (Method(value, name, line) is { } method) return method;
        return value switch
        {
            IDictionary<string, object?> map when map.TryGetValue(name, out var item) => item,
            _ => new JinjaUndefined(name),
        };
    }

    /// <summary><c>value[key]</c>: the item first, then an attribute of that name; missing is undefined.</summary>
    public static object? Item(object? value, object? key, int line)
    {
        switch (value)
        {
            case JinjaUndefined undefined:
                throw new JinjaException($"'{undefined.Name}' is undefined, so it has no [{JinjaValues.Repr(key)}].", line);
            case IDictionary<string, object?> map when key is string text:
                if (map.TryGetValue(text, out var item)) return item;
                break;
            case IList<object?> list when key is long or bool:
                long index = Index(key);
                if (index < 0) index += list.Count;
                return index >= 0 && index < list.Count ? list[(int)index] : new JinjaUndefined($"[{key}]");
            case string s when key is long or bool:
                var runes = s.EnumerateRunes().ToArray();
                long at = Index(key);
                if (at < 0) at += runes.Length;
                return at >= 0 && at < runes.Length ? runes[at].ToString() : new JinjaUndefined($"[{key}]");
        }
        return key is string name ? Attribute(value, name, line) : new JinjaUndefined($"[{JinjaValues.Repr(key)}]");
    }

    private static long Index(object? key) => key is bool b ? (b ? 1 : 0) : (long)key!;

    public static object? Slice(object? value, object? start, object? stop, object? step, int line)
    {
        long by = step is null ? 1 : Integer(step);
        if (by == 0) throw new JinjaException("A slice step cannot be zero.", line);
        switch (value)
        {
            case IList<object?> list:
                return Sliced(list.Count, start, stop, by).Select(i => list[i]).ToList();
            case string s:
                var runes = s.EnumerateRunes().ToArray();
                return string.Concat(Sliced(runes.Length, start, stop, by).Select(i => runes[i].ToString()));
            case JinjaUndefined undefined:
                throw new JinjaException($"'{undefined.Name}' is undefined and cannot be sliced.", line);
            default:
                throw new JinjaException($"{JinjaValues.TypeName(value)} cannot be sliced.", line);
        }
    }

    /// <summary>The indices Python's <c>[start:stop:step]</c> picks out of <paramref name="count"/>.</summary>
    private static IEnumerable<int> Sliced(int count, object? start, object? stop, long step)
    {
        long Clamp(object? bound, long fallback, long low, long high)
        {
            if (bound is null) return fallback;
            long b = Integer(bound);
            if (b < 0) b += count;
            return Math.Clamp(b, low, high);
        }
        if (step > 0)
        {
            long from = Clamp(start, 0, 0, count), to = Clamp(stop, count, 0, count);
            for (long i = from; i < to; i += step) yield return (int)i;
        }
        else
        {
            long from = Clamp(start, count - 1, -1, count - 1), to = Clamp(stop, -1, -1, count - 1);
            for (long i = from; i > to; i += step) yield return (int)i;
        }
    }

    // ---- methods -----------------------------------------------------------------------------------------------

    private static JinjaCallable? Method(object? value, string name, int line) => value switch
    {
        string s                        => StringMethod(s, name, line),
        IDictionary<string, object?> d  => DictMethod(d, name),
        IList<object?> l                => ListMethod(l, name, line),
        _                               => null,
    };

    private static JinjaCallable? DictMethod(IDictionary<string, object?> map, string name) => name switch
    {
        "get"    => new(name, (args, _) => args.Count > 0 && args[0] is string key && map.TryGetValue(key, out var v) ? v : args.Count > 1 ? args[1] : null),
        "items"  => new(name, (_, _) => map.Select(p => (object?)new JinjaTuple([p.Key, p.Value])).ToList()),
        "keys"   => new(name, (_, _) => map.Keys.Select(k => (object?)k).ToList()),
        "values" => new(name, (_, _) => map.Values.ToList()),
        "copy"   => new(name, (_, _) => JinjaValues.Mapping(map.Select(p => (p.Key, p.Value)))),
        _        => null,
    };

    private static JinjaCallable? ListMethod(IList<object?> list, string name, int line) => name switch
    {
        "index" => new(name, (args, _) =>
        {
            for (int i = 0; i < list.Count; i++) if (JinjaValues.Equal(list[i], Arg(args, 0))) return (long)i;
            throw new JinjaException($"{JinjaValues.Repr(Arg(args, 0))} is not in the list.", line);
        }),
        "count" => new(name, (args, _) => (long)list.Count(x => JinjaValues.Equal(x, Arg(args, 0)))),
        "copy"  => new(name, (_, _) => list.ToList()),
        "append" or "extend" or "insert" or "pop" or "remove" or "clear" or "sort" or "reverse"
                => new(name, (_, _) => throw new JinjaException($"A template cannot change a list (.{name}); keep state in a namespace.", line)),
        _       => null,
    };

    private static object? Arg(IReadOnlyList<object?> args, int index) => index < args.Count ? args[index] : null;

    private static string? StringArg(IReadOnlyList<object?> args, int index) => Arg(args, index) is { } value ? JinjaValues.Str(value) : null;

    private static JinjaCallable? StringMethod(string s, string name, int line) => name switch
    {
        "upper"      => new(name, (_, _) => s.ToUpperInvariant()),
        "lower"      => new(name, (_, _) => s.ToLowerInvariant()),
        "title"      => new(name, (_, _) => Title(s)),
        "capitalize" => new(name, (_, _) => Capitalize(s)),
        "swapcase"   => new(name, (_, _) => string.Concat(s.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)))),
        "strip"      => new(name, (args, _) => StringArg(args, 0) is { } chars ? s.Trim(chars.ToCharArray()) : s.Trim()),
        "lstrip"     => new(name, (args, _) => StringArg(args, 0) is { } chars ? s.TrimStart(chars.ToCharArray()) : s.TrimStart()),
        "rstrip"     => new(name, (args, _) => StringArg(args, 0) is { } chars ? s.TrimEnd(chars.ToCharArray()) : s.TrimEnd()),
        "split"      => new(name, (args, named) => Split(s, Arg(args, 0) ?? named.GetValueOrDefault("sep"), Arg(args, 1) ?? named.GetValueOrDefault("maxsplit"), fromEnd: false)),
        "rsplit"     => new(name, (args, named) => Split(s, Arg(args, 0) ?? named.GetValueOrDefault("sep"), Arg(args, 1) ?? named.GetValueOrDefault("maxsplit"), fromEnd: true)),
        "splitlines" => new(name, (_, _) => SplitLines(s)),
        "startswith" => new(name, (args, _) => Prefixes(Arg(args, 0)).Any(p => s.StartsWith(p, StringComparison.Ordinal))),
        "endswith"   => new(name, (args, _) => Prefixes(Arg(args, 0)).Any(p => s.EndsWith(p, StringComparison.Ordinal))),
        "replace"    => new(name, (args, _) => Replace(s, StringArg(args, 0) ?? "", StringArg(args, 1) ?? "", Arg(args, 2) is { } count ? Integer(count) : -1)),
        "find"       => new(name, (args, _) => (long)s.IndexOf(StringArg(args, 0) ?? "", StringComparison.Ordinal)),
        "rfind"      => new(name, (args, _) => (long)s.LastIndexOf(StringArg(args, 0) ?? "", StringComparison.Ordinal)),
        "count"      => new(name, (args, _) => (long)Count(s, StringArg(args, 0) ?? "")),
        "join"       => new(name, (args, _) => string.Join(s, JinjaValues.Items(Arg(args, 0), line).Select(JinjaValues.Str))),
        "format"     => new(name, (args, named) => Format(s, args, named, line)),
        "isdigit"    => new(name, (_, _) => s.Length > 0 && s.All(char.IsDigit)),
        "isalpha"    => new(name, (_, _) => s.Length > 0 && s.All(char.IsLetter)),
        "isalnum"    => new(name, (_, _) => s.Length > 0 && s.All(char.IsLetterOrDigit)),
        "isspace"    => new(name, (_, _) => s.Length > 0 && s.All(char.IsWhiteSpace)),
        "islower"    => new(name, (_, _) => s.Any(char.IsLetter) && !s.Any(char.IsUpper)),
        "isupper"    => new(name, (_, _) => s.Any(char.IsLetter) && !s.Any(char.IsLower)),
        "zfill"      => new(name, (args, _) => s.PadLeft((int)Integer(Arg(args, 0)), '0')),
        _            => null,
    };

    private static IEnumerable<string> Prefixes(object? value)
        => value is IList<object?> options ? options.Select(JinjaValues.Str) : [JinjaValues.Str(value)];

    private static List<object?> Split(string s, object? separator, object? maxsplit, bool fromEnd)
    {
        int limit = maxsplit is null ? -1 : (int)Integer(maxsplit);
        if (separator is null)
        {
            var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (limit < 0 || words.Length <= limit + 1) return [.. words];
            // Python keeps the rest of the string, from where the last split word starts.
            if (!fromEnd)
            {
                var head = s.TrimStart();
                var parts = new List<object?>();
                for (int i = 0; i < limit; i++)
                {
                    int end = head.IndexOfAny([' ', '\t', '\n', '\r', '\f', '\v']);
                    parts.Add(head[..end]);
                    head = head[end..].TrimStart();
                }
                parts.Add(head);
                return parts;
            }
            var tail = s.TrimEnd();
            var reversed = new List<object?>();
            for (int i = 0; i < limit; i++)
            {
                int start = tail.LastIndexOfAny([' ', '\t', '\n', '\r', '\f', '\v']);
                reversed.Add(tail[(start + 1)..]);
                tail = tail[..start].TrimEnd();
            }
            reversed.Add(tail);
            reversed.Reverse();
            return reversed;
        }

        string sep = JinjaValues.Str(separator);
        if (sep.Length == 0) throw new JinjaException("split() separator cannot be empty.");
        if (limit < 0) return [.. s.Split(sep)];
        if (!fromEnd) return [.. s.Split(sep, limit + 1)];
        var pieces = new List<object?>();
        string rest = s;
        for (int i = 0; i < limit; i++)
        {
            int at = rest.LastIndexOf(sep, StringComparison.Ordinal);
            if (at < 0) break;
            pieces.Add(rest[(at + sep.Length)..]);
            rest = rest[..at];
        }
        pieces.Add(rest);
        pieces.Reverse();
        return pieces;
    }

    private static string Replace(string s, string old, string replacement, long count)
    {
        if (count < 0) return old.Length == 0 ? string.Join(replacement, ["", .. s.Select(c => c.ToString()), ""]) : s.Replace(old, replacement, StringComparison.Ordinal);
        var text = new StringBuilder();
        int from = 0;
        for (long done = 0; done < count; done++)
        {
            int at = s.IndexOf(old, from, StringComparison.Ordinal);
            if (at < 0 || old.Length == 0) break;
            text.Append(s, from, at - from).Append(replacement);
            from = at + old.Length;
        }
        return text.Append(s, from, s.Length - from).ToString();
    }

    private static int Count(string s, string part)
    {
        if (part.Length == 0) return s.Length + 1;
        int count = 0;
        for (int at = s.IndexOf(part, StringComparison.Ordinal); at >= 0; at = s.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>Python's <c>str.format</c> with <c>{}</c>, <c>{0}</c> and <c>{name}</c> fields and doubled braces.</summary>
    private static string Format(string format, IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?> named, int line)
    {
        var text = new StringBuilder();
        int auto = 0;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '{' && i + 1 < format.Length && format[i + 1] == '{') { text.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < format.Length && format[i + 1] == '}') { text.Append('}'); i++; continue; }
            if (c != '{') { text.Append(c); continue; }
            int close = format.IndexOf('}', i);
            if (close < 0) throw new JinjaException("A format field is never closed.", line);
            string field = format[(i + 1)..close].Split(':')[0];
            object? value = field.Length == 0 ? Arg(args, auto++)
                          : int.TryParse(field, out int index) ? Arg(args, index)
                          : named.GetValueOrDefault(field);
            text.Append(JinjaValues.Str(value));
            i = close;
        }
        return text.ToString();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>Jinja's <c>title</c>: each word's first letter upper, the rest lower, a word starting after a space,
    /// a bracket or a hyphen.</summary>
    private static string Title(string s)
    {
        var text = new StringBuilder(s.Length);
        bool start = true;
        foreach (char c in s)
        {
            text.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            start = char.IsWhiteSpace(c) || c is '-' or '(' or '[' or '{' or '<' or '_';
        }
        return text.ToString();
    }

    // ---- filters -----------------------------------------------------------------------------------------------

    public static object? Filter(string name, object? value, IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?> named,
                                 JinjaRenderer renderer, int line)
    {
        object? A(int index, string key, object? fallback = null)
            => index < args.Count ? args[index] : named.TryGetValue(key, out var v) ? v : fallback;

        switch (name)
        {
            case "default" or "d":
                bool boolean = JinjaValues.IsTrue(A(1, "boolean", false));
                return value is JinjaUndefined || (boolean && !JinjaValues.IsTrue(value)) ? A(0, "default_value", "") : value;
            case "upper":      return JinjaValues.Str(value).ToUpperInvariant();
            case "lower":      return JinjaValues.Str(value).ToLowerInvariant();
            case "capitalize": return Capitalize(JinjaValues.Str(value));
            case "title":      return Title(JinjaValues.Str(value));
            case "trim":
                return A(0, "chars") is { } chars ? JinjaValues.Str(value).Trim(JinjaValues.Str(chars).ToCharArray()) : JinjaValues.Str(value).Trim();
            case "string":     return JinjaValues.Str(value);
            case "safe":       return value;
            case "escape" or "e" or "forceescape":
                return Escape(JinjaValues.Str(value));
            case "length" or "count":
                return JinjaValues.Length(value) ?? throw new JinjaException($"{JinjaValues.TypeName(value)} has no length.", line);
            case "list":       return JinjaValues.Items(value, line).ToList();
            case "first":
            {
                var items = JinjaValues.Items(value, line);
                return items.Count > 0 ? items[0] : new JinjaUndefined("first");
            }
            case "last":
            {
                var items = JinjaValues.Items(value, line);
                return items.Count > 0 ? items[^1] : new JinjaUndefined("last");
            }
            case "reverse":
                return value is string text ? string.Concat(text.EnumerateRunes().Reverse().Select(r => r.ToString()))
                                            : JinjaValues.Items(value, line).Reverse().ToList();
            case "join":
            {
                var items = JinjaValues.Items(value, line).AsEnumerable();
                if (A(1, "attribute") is { } attribute) items = items.Select(i => Path(i, attribute, line));
                return string.Join(JinjaValues.Str(A(0, "d", "")), items.Select(JinjaValues.Str));
            }
            case "replace":
                return Replace(JinjaValues.Str(value), JinjaValues.Str(A(0, "old", "")), JinjaValues.Str(A(1, "new", "")),
                               A(2, "count") is { } count ? Integer(count) : -1);
            case "items":
                return value is IDictionary<string, object?> map ? map.Select(p => (object?)new JinjaTuple([p.Key, p.Value])).ToList()
                     : value is JinjaUndefined ? new List<object?>()
                     : throw new JinjaException($"items needs a mapping, not {JinjaValues.TypeName(value)}.", line);
            case "dictsort":
                return DictSort(value, JinjaValues.IsTrue(A(0, "case_sensitive", false)), JinjaValues.Str(A(1, "by", "key")),
                                JinjaValues.IsTrue(A(2, "reverse", false)), line);
            case "sort":
                return Sorted(JinjaValues.Items(value, line), JinjaValues.IsTrue(A(0, "reverse", false)),
                              JinjaValues.IsTrue(A(1, "case_sensitive", false)), A(2, "attribute"), line);
            case "unique":
            {
                bool caseSensitive = JinjaValues.IsTrue(A(0, "case_sensitive", false));
                var attribute = A(1, "attribute");
                var seen = new List<object?>();
                var kept = new List<object?>();
                foreach (var item in JinjaValues.Items(value, line))
                {
                    var key = Folded(attribute is null ? item : Path(item, attribute, line), caseSensitive);
                    if (seen.Any(s => JinjaValues.Equal(s, key))) continue;
                    seen.Add(key);
                    kept.Add(item);
                }
                return kept;
            }
            case "map":
                return Map(value, args, named, renderer, line);
            case "select" or "reject":
                return JinjaValues.Items(value, line).Where(item =>
                    (args.Count == 0 ? JinjaValues.IsTrue(item) : Test(JinjaValues.Str(args[0]), item, [.. args.Skip(1)], NoNames, renderer, line))
                    == (name == "select")).ToList();
            case "selectattr" or "rejectattr":
                return JinjaValues.Items(value, line).Where(item =>
                {
                    var member = Path(item, A(0, "attribute"), line);
                    bool passed = args.Count < 2 ? JinjaValues.IsTrue(member) : Test(JinjaValues.Str(args[1]), member, [.. args.Skip(2)], NoNames, renderer, line);
                    return passed == (name == "selectattr");
                }).ToList();
            case "attr":
                return Attribute(value, JinjaValues.Str(A(0, "name")), line);
            case "tojson":
            {
                var separators = A(2, "separators") as IList<object?>;
                return JinjaValues.Json(value, new JinjaValues.JsonStyle(
                    Indent: A(1, "indent") is { } indent ? (int)Integer(indent) : null,
                    ItemSeparator: separators is { Count: 2 } ? JinjaValues.Str(separators[0]) : null,
                    KeySeparator: separators is { Count: 2 } ? JinjaValues.Str(separators[1]) : ": ",
                    SortKeys: JinjaValues.IsTrue(A(3, "sort_keys", false)),
                    EnsureAscii: JinjaValues.IsTrue(A(0, "ensure_ascii", false))), line);
            }
            case "int":
                return ToInteger(value, A(0, "default", 0L), A(1, "base", 10L));
            case "float":
                return ToFloat(value, A(0, "default", 0.0));
            case "abs":
                return value switch { long l => (object?)Math.Abs(l), double d => Math.Abs(d), bool b => b ? 1L : 0L,
                                      _ => throw new JinjaException($"abs needs a number, not {JinjaValues.TypeName(value)}.", line) };
            case "round":
            {
                int precision = (int)Integer(A(0, "precision", 0L));
                string method = JinjaValues.Str(A(1, "method", "common"));
                double number = JinjaValues.ToDouble(value), scale = Math.Pow(10, precision);
                return method switch
                {
                    "ceil"  => Math.Ceiling(number * scale) / scale,
                    "floor" => Math.Floor(number * scale) / scale,
                    _       => Math.Round(number, precision, MidpointRounding.ToEven),   // Python's round
                };
            }
            case "sum":
            {
                object? total = A(1, "start", 0L);
                foreach (var item in JinjaValues.Items(value, line))
                    total = JinjaValues.Arithmetic("+", total, A(0, "attribute") is { } attribute ? Path(item, attribute, line) : item, line);
                return total;
            }
            case "min" or "max":
            {
                var items = JinjaValues.Items(value, line);
                if (items.Count == 0) return new JinjaUndefined(name);
                var sorted = Sorted(items, reverse: name == "max", JinjaValues.IsTrue(A(0, "case_sensitive", false)), A(1, "attribute"), line);
                return sorted[0];
            }
            case "indent":
                return Indent(JinjaValues.Str(value), A(0, "width", 4L), JinjaValues.IsTrue(A(1, "first", false)), JinjaValues.IsTrue(A(2, "blank", false)));
            case "center":
            {
                string subject = JinjaValues.Str(value);
                int width = (int)Integer(A(0, "width", 80L));
                if (width <= subject.Length) return subject;
                int left = (width - subject.Length) / 2 + ((width - subject.Length) % 2 & width % 2);
                return new string(' ', left) + subject + new string(' ', width - subject.Length - left);
            }
            case "wordcount":
                return (long)JinjaValues.Str(value).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            case "truncate":
            {
                string subject = JinjaValues.Str(value), end = JinjaValues.Str(A(2, "end", "..."));
                int length = (int)Integer(A(0, "length", 255L)), leeway = (int)Integer(A(3, "leeway", 5L));
                if (subject.Length <= length + leeway) return subject;
                if (JinjaValues.IsTrue(A(1, "killwords", false))) return subject[..Math.Max(0, length - end.Length)] + end;
                string cut = subject[..Math.Max(0, length - end.Length)];
                int space = cut.LastIndexOf(' ');
                return (space > 0 ? cut[..space] : cut) + end;
            }
            case "format":
                return JinjaValues.Percent(JinjaValues.Str(value), args.Count == 1 ? args[0] : args.ToList(), line);
            case "batch":
            {
                int size = (int)Integer(A(0, "linecount"));
                if (size <= 0) throw new JinjaException("batch needs a positive size.", line);
                var fill = A(1, "fill_with");
                var rows = JinjaValues.Items(value, line).Chunk(size).Select(chunk => (object?)chunk.ToList()).ToList();
                if (fill is not null && rows.Count > 0 && rows[^1] is List<object?> lastRow)
                    while (lastRow.Count < size) lastRow.Add(fill);
                return rows;
            }
            default:
                throw new JinjaException($"Filter '{name}' is not one chat templates can use.", line);
        }
    }

    private static object? Map(object? value, IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?> named, JinjaRenderer renderer, int line)
    {
        var items = JinjaValues.Items(value, line);
        if (named.TryGetValue("attribute", out var attribute))
            return items.Select(item => Path(item, attribute, line) is JinjaUndefined && named.TryGetValue("default", out var fallback)
                                            ? fallback : Path(item, attribute, line)).ToList();
        if (args.Count == 0) throw new JinjaException("map needs a filter or an attribute.", line);
        string filter = JinjaValues.Str(args[0]);
        var rest = args.Skip(1).ToList();
        return items.Select(item => Filter(filter, item, rest, named, renderer, line)).ToList();
    }

    /// <summary>An attribute path — <c>"a.b"</c>, with a number for a list's item — as <c>map</c>,
    /// <c>selectattr</c> and the sorting filters take one.</summary>
    private static object? Path(object? value, object? path, int line)
    {
        if (path is long index) return Item(value, index, line);
        foreach (var part in JinjaValues.Str(path).Split('.'))
            value = long.TryParse(part, out long i) && value is IList<object?> ? Item(value, i, line) : Item(value, part, line);
        return value;
    }

    private static object? Folded(object? value, bool caseSensitive) => !caseSensitive && value is string s ? s.ToLowerInvariant() : value;

    private static List<object?> DictSort(object? value, bool caseSensitive, string by, bool reverse, int line)
    {
        if (value is not IDictionary<string, object?> map)
        {
            if (value is JinjaUndefined) return [];
            throw new JinjaException($"dictsort needs a mapping, not {JinjaValues.TypeName(value)}.", line);
        }
        int position = by switch { "key" => 0, "value" => 1, _ => throw new JinjaException("dictsort sorts by 'key' or 'value'.", line) };
        var pairs = map.Select(p => new JinjaTuple([p.Key, p.Value])).ToList();
        var sorted = pairs.Order(Comparer<JinjaTuple>.Create((a, b) =>
            JinjaValues.Compare(Folded(a[position], caseSensitive), Folded(b[position], caseSensitive), line))).ToList();
        if (reverse) sorted.Reverse();
        return [.. sorted];
    }

    private static List<object?> Sorted(IReadOnlyList<object?> items, bool reverse, bool caseSensitive, object? attribute, int line)
    {
        object? Key(object? item) => Folded(attribute is null ? item : Path(item, attribute, line), caseSensitive);
        var comparer = Comparer<object?>.Create((a, b) => JinjaValues.Compare(Key(a), Key(b), line));
        // Both orderings are stable: equal items keep their order, as with Python's sort, reversed or not.
        return [.. reverse ? items.OrderDescending(comparer) : items.Order(comparer)];
    }

    private static string Indent(string text, object? width, bool first, bool blank)
    {
        string pad = width is string s ? s : new string(' ', (int)Integer(width));
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 0 && !first) continue;
            if (!blank && lines[i].Trim().Length == 0) continue;
            lines[i] = pad + lines[i];
        }
        return string.Join('\n', lines);
    }

    private static object? ToInteger(object? value, object? fallback, object? numberBase) => value switch
    {
        long l   => l,
        bool b   => b ? 1L : 0L,
        double d => (long)d,
        string s when long.TryParse(s.Trim().Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)
                    && Integer(numberBase) == 10 => l,
        string s when Integer(numberBase) == 16 && long.TryParse(s.Trim().Replace("0x", "").Replace("0X", ""), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long h) => h,
        string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => (long)d,
        _        => fallback,
    };

    private static object? ToFloat(object? value, object? fallback) => value switch
    {
        double d => d,
        long l   => (double)l,
        bool b   => b ? 1.0 : 0.0,
        string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
        _        => fallback,
    };

    // ---- tests -------------------------------------------------------------------------------------------------

    public static bool Test(string name, object? value, IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?> named,
                            JinjaRenderer renderer, int line)
    {
        object? A(int index) => index < args.Count ? args[index] : named.Values.ElementAtOrDefault(index - args.Count);
        return name switch
        {
            "defined"               => value is not JinjaUndefined,
            "undefined"             => value is JinjaUndefined,
            "none"                  => value is null,
            "boolean"               => value is bool,
            "true"                  => value is true,
            "false"                 => value is false,
            "integer"               => value is long,
            "float"                 => value is double,
            "number"                => value is long or double or bool,
            "string"                => value is string,
            "mapping"               => value is IDictionary<string, object?>,
            "sequence"              => value is string or IList<object?> or IDictionary<string, object?>,
            "iterable"              => value is string or IList<object?> or IDictionary<string, object?>,
            "callable"              => value is JinjaCallable or JinjaMacro,
            "lower"                 => value is string lower && lower == lower.ToLowerInvariant(),
            "upper"                 => value is string upper && upper == upper.ToUpperInvariant(),
            "odd"                   => Integer(value) % 2 != 0,
            "even"                  => Integer(value) % 2 == 0,
            "divisibleby"           => Integer(A(0)) != 0 && Integer(value) % Integer(A(0)) == 0,
            "eq" or "equalto" or "==" => JinjaValues.Equal(value, A(0)),
            "ne" or "!="            => !JinjaValues.Equal(value, A(0)),
            "lt" or "lessthan" or "<" => JinjaValues.Compare(value, A(0), line) < 0,
            "le" or "<="            => JinjaValues.Compare(value, A(0), line) <= 0,
            "gt" or "greaterthan" or ">" => JinjaValues.Compare(value, A(0), line) > 0,
            "ge" or ">="            => JinjaValues.Compare(value, A(0), line) >= 0,
            "in"                    => JinjaValues.Contains(A(0), value, line),
            "sameas"                => value is null ? A(0) is null : value is bool or long ? JinjaValues.Equal(value, A(0)) && value.GetType() == A(0)?.GetType() : ReferenceEquals(value, A(0)),
            "escaped"               => false,
            "filter"                => value is string f && FilterExists(f),
            "test"                  => value is string t && TestExists(t),
            _                       => throw new JinjaException($"Test '{name}' is not one chat templates can use.", line),
        };
    }

    /// <summary>Every filter <see cref="Filter"/> answers to — what the <c>filter</c> test asks after.</summary>
    internal static readonly IReadOnlySet<string> FilterNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "default", "d", "upper", "lower", "capitalize", "title", "trim", "string", "safe", "escape", "e", "forceescape",
        "length", "count", "list", "first", "last", "reverse", "join", "replace", "items", "dictsort", "sort", "unique",
        "map", "select", "reject", "selectattr", "rejectattr", "attr", "tojson", "int", "float", "abs", "round", "sum",
        "min", "max", "indent", "center", "wordcount", "truncate", "format", "batch",
    };

    /// <summary>Every test <see cref="Test"/> answers to — what the <c>test</c> test asks after.</summary>
    internal static readonly IReadOnlySet<string> TestNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "defined", "undefined", "none", "boolean", "true", "false", "integer", "float", "number", "string", "mapping",
        "sequence", "iterable", "callable", "lower", "upper", "odd", "even", "divisibleby", "eq", "equalto", "==", "ne",
        "!=", "lt", "lessthan", "<", "le", "<=", "gt", "greaterthan", ">", "ge", ">=", "in", "sameas", "escaped",
        "filter", "test",
    };

    private static bool FilterExists(string name) => FilterNames.Contains(name);

    private static bool TestExists(string name) => TestNames.Contains(name);

    /// <summary>Python's <c>splitlines</c>: lines without their endings, and no empty line after a final newline.</summary>
    private static List<object?> SplitLines(string s)
    {
        var lines = s.Replace("\r\n", "\n").Split('\n', '\r').Select(l => (object?)l).ToList();
        if (lines.Count > 0 && (string)lines[^1]! == "") lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>MarkupSafe's escaping: <c>&amp; &lt; &gt; " '</c>.</summary>
    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&#34;").Replace("'", "&#39;");

    // ---- strftime ----------------------------------------------------------------------------------------------

    /// <summary>Python's <c>strftime</c> for the directives a template puts a date in a system prompt with.</summary>
    private static string Strftime(DateTime time, string format)
    {
        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                text.Append(format[i]);
                continue;
            }
            char d = format[++i];
            if (d == '-' && i + 1 < format.Length && format[i + 1] == 'd')
            {
                text.Append(time.Day.ToString(invariant));
                i++;
                continue;
            }
            text.Append(d switch
            {
                'Y' => time.ToString("yyyy", invariant),
                'y' => time.ToString("yy", invariant),
                'm' => time.ToString("MM", invariant),
                'd' => time.ToString("dd", invariant),
                'H' => time.ToString("HH", invariant),
                'I' => time.ToString("hh", invariant),
                'M' => time.ToString("mm", invariant),
                'S' => time.ToString("ss", invariant),
                'p' => time.ToString("tt", invariant),
                'B' => time.ToString("MMMM", invariant),
                'b' => time.ToString("MMM", invariant),
                'A' => time.ToString("dddd", invariant),
                'a' => time.ToString("ddd", invariant),
                'j' => time.DayOfYear.ToString("000", invariant),
                '%' => "%",
                _   => "%" + d,
            });
        }
        return text.ToString();
    }
}
