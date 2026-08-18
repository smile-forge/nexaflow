using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Expressions;

/// <summary>
/// Evaluates a parsed <see cref="Expr"/> against an <see cref="EvalScope"/>.
///
/// <para>
/// Arithmetic follows the specification's two coercion rules and no others: <c>Bool</c> becomes
/// <c>Int</c> under arithmetic and bitwise operators (so a tag-depth fold can write
/// <c>depth + (lvt == 6) - (lvt == 7)</c>), and mixing <c>Int</c> with <c>Number</c> promotes to
/// <c>Number</c>. There is no implicit <c>Int → Bool</c>: a document must write <c>x != 0</c>, because
/// silent truthiness is how a comparison ends up used as a quantity.
/// </para>
/// </summary>
public sealed class Evaluator(ConverterTable? converters = null, long fuel = ProtoLimits.DefaultFuel)
{
    private readonly ConverterTable _converters = converters ?? ConverterTable.Default;
    private readonly long _fuel = fuel;
    private long _remaining;

    /// <summary>Higher-order calls take a lambda in one argument position; the index is where.</summary>
    private static readonly Dictionary<string, int> LambdaArgumentOf = new(StringComparer.Ordinal)
    {
        ["map"] = 1, ["filter"] = 1, ["findFirst"] = 1, ["takeWhile"] = 1, ["any"] = 1, ["all"] = 1,
        ["fold"] = 2, ["scan"] = 2,
    };

    public ProtoValue Eval(Expr e, EvalScope scope)
    {
        _remaining = _fuel;
        return Evaluate(e, scope);
    }

    private ProtoValue Evaluate(Expr e, EvalScope scope)
    {
        if (--_remaining < 0)
            throw new ProtoTypeException(
                $"evaluation exceeded {_fuel:N0} steps. Every loop bound in this language is a literal, so "
              + "this should be unreachable — reaching it means the totality analysis has a bug.");

        return EvaluateCore(e, scope);
    }

    private ProtoValue EvaluateCore(Expr e, EvalScope scope) => e switch
    {
        Expr.Let l => Evaluate(l.Body, scope.Child().Set(l.Name, Evaluate(l.Value, scope))),
        Expr.Lambda => throw new ProtoTypeException(
            "a lambda is legal only as an argument to map/filter/fold/scan/findFirst/takeWhile/any/all — "
          + "there are no first-class functions, which is what makes recursion unwritable"),
        Expr.Literal l => l.Value,
        Expr.Root r => scope.Root(r.Name),
        Expr.Member m => Member(Evaluate(m.Target, scope), m.Name),
        Expr.Index x => Index(Evaluate(x.Target, scope), Evaluate(x.Position, scope)),
        Expr.Unary u => Unary(u.Op, Evaluate(u.Operand, scope)),
        Expr.Binary b => Binary(b, scope),
        Expr.Conditional c => Evaluate(c.Condition, scope).AsBool() ? Evaluate(c.WhenTrue, scope) : Evaluate(c.WhenFalse, scope),
        Expr.Call c => CallFunction(c, scope),
        Expr.Pipeline p => Pipe(p, scope),
        _ => throw new ProtoTypeException($"cannot evaluate {e.GetType().Name}"),
    };

    /// <summary>Convenience for tests and one-off evaluation.</summary>
    public ProtoValue Eval(string source, EvalScope scope) => Eval(Expr.Parse(source), scope);

    // ── Access ────────────────────────────────────────────────────────────────

    private static ProtoValue Member(ProtoValue target, string name) => target switch
    {
        ProtoValue.Rec r => r.Members.TryGetValue(name, out var v) ? v : ProtoValue.Nothing,
        ProtoValue.Null => ProtoValue.Nothing,   // a missing root's members are missing, not an error
        _ => throw new ProtoTypeException($"cannot read member '{name}' of {target.Kind}"),
    };

    private static ProtoValue Index(ProtoValue target, ProtoValue position)
    {
        if (target is ProtoValue.Null) return ProtoValue.Nothing;

        var items = target switch
        {
            ProtoValue.List l => l.Items,
            ProtoValue.Bytes b => b.Value.Select(x => ProtoValue.Of((long)x)).ToList(),
            _ => throw new ProtoTypeException($"cannot index {target.Kind}"),
        };

        long i = position.AsInt();
        if (i < 0) i += items.Count;                       // x[-1] is the last element
        return i >= 0 && i < items.Count ? items[(int)i] : ProtoValue.Nothing;
    }

    // ── Operators ─────────────────────────────────────────────────────────────

    private static ProtoValue Unary(string op, ProtoValue v) => op switch
    {
        "!" => ProtoValue.Of(!v.AsBool()),
        "~" => ProtoValue.Of(~v.AsInt()),
        "-" => v is ProtoValue.Num n ? ProtoValue.Of(-n.Value) : ProtoValue.Of(-v.AsInt()),
        _ => throw new ProtoTypeException($"unknown unary operator '{op}'"),
    };

    private ProtoValue Binary(Expr.Binary b, EvalScope scope)
    {
        // Short-circuiting and null-coalescing must not evaluate the right operand.
        switch (b.Op)
        {
            case "&&": return ProtoValue.Of(Evaluate(b.Left, scope).AsBool() && Evaluate(b.Right, scope).AsBool());
            case "||": return ProtoValue.Of(Evaluate(b.Left, scope).AsBool() || Evaluate(b.Right, scope).AsBool());
            case "??":
                var lhs = Evaluate(b.Left, scope);
                return lhs.IsNull ? Evaluate(b.Right, scope) : lhs;
        }

        var l = Evaluate(b.Left, scope);
        var r = Evaluate(b.Right, scope);

        switch (b.Op)
        {
            case "==": return ProtoValue.Of(ValueEquals(l, r));
            case "!=": return ProtoValue.Of(!ValueEquals(l, r));
        }

        // Text concatenation is the one non-numeric use of '+', and it is what header assembly needs.
        if (b.Op == "+" && (l is ProtoValue.Text || r is ProtoValue.Text))
            return ProtoValue.Of(l.AsText() + r.AsText());

        // Number contaminates: Int op Number promotes, matching the type table.
        bool real = l is ProtoValue.Num || r is ProtoValue.Num;

        if (real && b.Op is "+" or "-" or "*" or "/" or "<" or "<=" or ">" or ">=")
        {
            double a = l.AsNumber(), c = r.AsNumber();
            return b.Op switch
            {
                "+" => ProtoValue.Of(a + c),
                "-" => ProtoValue.Of(a - c),
                "*" => ProtoValue.Of(a * c),
                "/" => ProtoValue.Of(a / c),
                "<" => ProtoValue.Of(a < c),
                "<=" => ProtoValue.Of(a <= c),
                ">" => ProtoValue.Of(a > c),
                _ => ProtoValue.Of(a >= c),
            };
        }

        long x = l.AsInt(), y = r.AsInt();
        return b.Op switch
        {
            "+" => ProtoValue.Of(checked(x + y)),
            "-" => ProtoValue.Of(checked(x - y)),
            "*" => ProtoValue.Of(checked(x * y)),
            "/" => y == 0 ? throw new ProtoTypeException("division by zero") : ProtoValue.Of(x / y),
            "%" => y == 0 ? throw new ProtoTypeException("modulo by zero") : ProtoValue.Of(x % y),
            "<<" => ProtoValue.Of(x << (int)y),
            ">>" => ProtoValue.Of(x >> (int)y),
            "&" => ProtoValue.Of(x & y),
            "^" => ProtoValue.Of(x ^ y),      // bitwise xor — exponentiation is pow(a, b)
            "|" => ProtoValue.Of(x | y),
            "<" => ProtoValue.Of(x < y),
            "<=" => ProtoValue.Of(x <= y),
            ">" => ProtoValue.Of(x > y),
            ">=" => ProtoValue.Of(x >= y),
            _ => throw new ProtoTypeException($"unknown operator '{b.Op}'"),
        };
    }

    private static bool ValueEquals(ProtoValue a, ProtoValue b)
    {
        if (a is ProtoValue.Null || b is ProtoValue.Null) return a is ProtoValue.Null && b is ProtoValue.Null;

        // Numeric comparison across Int/Number/Bool, so `count == 1` works whatever produced count.
        bool aNum = a is ProtoValue.Int or ProtoValue.Num or ProtoValue.Bool;
        bool bNum = b is ProtoValue.Int or ProtoValue.Num or ProtoValue.Bool;
        if (aNum && bNum)
        {
            if (a is ProtoValue.Bool || b is ProtoValue.Bool)
                return a.AsInt() == b.AsInt();
            return Math.Abs(a.AsNumber() - b.AsNumber()) < double.Epsilon;
        }

        return a.Equals(b);
    }

    // ── Calls ─────────────────────────────────────────────────────────────────

    // a |> f(b) is exactly f(a, b) — the source becomes the first argument, so both forms funnel here.
    private ProtoValue Pipe(Expr.Pipeline p, EvalScope scope)
        => Dispatch(p.Target.Name, [p.Source, .. p.Target.Args], scope);

    private ProtoValue CallFunction(Expr.Call c, EvalScope scope) => Dispatch(c.Name, c.Args, scope);

    private ProtoValue Dispatch(string name, IReadOnlyList<Expr> argExprs, EvalScope scope)
    {
        // `if` is lazy in its branches; everything else is strict in its non-lambda arguments.
        if (name == "if")
        {
            if (argExprs.Count != 3) throw new ProtoTypeException("if(cond, a, b) takes exactly 3 arguments");
            return Evaluate(argExprs[0], scope).AsBool()
                ? Evaluate(argExprs[1], scope)
                : Evaluate(argExprs[2], scope);
        }

        if (LambdaArgumentOf.TryGetValue(name, out var lambdaAt))
            return HigherOrder(name, lambdaAt, argExprs, scope);

        var args = argExprs.Select(a => Evaluate(a, scope)).ToList();

        switch (name)
        {
            case "min": return Numeric2(name, args, Math.Min, Math.Min);
            case "max": return Numeric2(name, args, Math.Max, Math.Max);
            case "pow":
                Require(name, args, 2);
                return ProtoValue.Of(Math.Pow(args[0].AsNumber(), args[1].AsNumber()) is var d
                                     && d == Math.Floor(d) && Math.Abs(d) < long.MaxValue
                                     ? ProtoValue.Of((long)d).AsInt()
                                     : throw new ProtoTypeException("pow overflowed Int"));
            case "abs":
                Require(name, args, 1);
                return args[0] is ProtoValue.Num n
                    ? ProtoValue.Of(Math.Abs(n.Value))
                    : ProtoValue.Of(Math.Abs(args[0].AsInt()));

            // Bit width of a non-negative value — the digit count for any base-N regrouping, and what
            // lets a bounded `range` replace the unbounded while-loop such an encoding would otherwise need.
            case "bitLength":
                Require(name, args, 1);
                long bl = args[0].AsInt();
                if (bl < 0) throw new ProtoTypeException("bitLength expects a non-negative value");
                return ProtoValue.Of((long)(64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)bl)));

            // `range(n, max)` — the ONLY generator, and `max` must be a literal, which is what makes
            // totality syntactic rather than something to be proved. Exceeding it is an error, never a
            // silent truncation.
            case "list": return new ProtoValue.List([.. args]);

            case "range":
                Require(name, args, 2);
                long count = args[0].AsInt(), cap = args[1].AsInt();
                if (count < 0) throw new ProtoTypeException("range() expects a non-negative count");
                if (cap is < 0 or > ProtoLimits.MaxRangeBound)
                    throw new ProtoTypeException(
                        $"a range maximum must be between 0 and {ProtoLimits.MaxRangeBound:N0} — otherwise a "
                      + "document could buy an arbitrarily long loop just by writing a large bound");
                if (count > cap)
                    throw new ProtoTypeException(
                        $"range({count}) exceeds its declared maximum of {cap}. The maximum is what bounds "
                      + "iteration, so it is a hard error rather than a truncation.");
                return new ProtoValue.List([.. Enumerable.Range(0, (int)count).Select(i => ProtoValue.Of((long)i))]);
        }

        // Otherwise it is a converter used in call form: f(a, b) == a |> f(b).
        if (args.Count == 0)
            throw new ProtoTypeException($"unknown function '{name}'");

        return Invoke(name, args[0], args.Skip(1).ToList());
    }

    /// <summary>
    /// The bounded-iteration constructs. Every one of them iterates a list that already exists, so the
    /// bound is the list's length — and the only way to conjure a list from a number is <c>range(n, max)</c>
    /// with a literal maximum. There is no <c>while</c>, no recursion and no first-class function, so
    /// termination is a property of the syntax rather than something to prove.
    /// </summary>
    private ProtoValue HigherOrder(string name, int lambdaAt, IReadOnlyList<Expr> argExprs, EvalScope scope)
    {
        if (argExprs.Count != lambdaAt + 1)
            throw new ProtoTypeException(
                $"{name}() takes {lambdaAt + 1} argument(s), the last being a lambda; got {argExprs.Count}");

        if (argExprs[lambdaAt] is not Expr.Lambda fn)
            throw new ProtoTypeException($"{name}() expects a lambda as its last argument, "
                                       + $"e.g. items |> {name}(x -> x + 1)");

        var items = Evaluate(argExprs[0], scope).AsList();

        // The one non-lambda extra argument, where there is one (fold/scan take a seed).
        ProtoValue seed = lambdaAt == 2 ? Evaluate(argExprs[1], scope) : ProtoValue.Nothing;

        ProtoValue Call1(ProtoValue x)
        {
            if (fn.Parameters.Count != 1)
                throw new ProtoTypeException($"{name}() expects a one-parameter lambda");
            return Evaluate(fn.Body, scope.Child().Set(fn.Parameters[0], x));
        }

        ProtoValue Call2(ProtoValue a, ProtoValue b)
        {
            if (fn.Parameters.Count != 2)
                throw new ProtoTypeException($"{name}() expects a two-parameter lambda, e.g. (acc, x) -> …");
            return Evaluate(fn.Body, scope.Child().Set(fn.Parameters[0], a).Set(fn.Parameters[1], b));
        }

        switch (name)
        {
            case "map":
                return new ProtoValue.List([.. items.Select(Call1)]);

            case "filter":
                return new ProtoValue.List([.. items.Where(x => Call1(x).AsBool())]);

            case "findFirst":
                foreach (var x in items) if (Call1(x).AsBool()) return x;
                return ProtoValue.Nothing;

            case "takeWhile":
            {
                List<ProtoValue> kept = [];
                foreach (var x in items) { if (!Call1(x).AsBool()) break; kept.Add(x); }
                return new ProtoValue.List(kept);
            }

            case "any": return ProtoValue.Of(items.Any(x => Call1(x).AsBool()));
            case "all": return ProtoValue.Of(items.All(x => Call1(x).AsBool()));

            case "fold":
            {
                var acc = seed;
                foreach (var x in items) acc = Call2(acc, x);
                return acc;
            }

            // Like fold, but yielding every intermediate accumulator — which is what an encoding whose
            // element N depends on elements 0..N-1 actually needs, and the reason it is a primitive here
            // rather than something a document has to fake with indexing.
            case "scan":
            {
                var acc = seed;
                List<ProtoValue> outp = [];
                foreach (var x in items) { acc = Call2(acc, x); outp.Add(acc); }
                return new ProtoValue.List(outp);
            }

            default:
                throw new ProtoTypeException($"unknown higher-order function '{name}'");
        }
    }

    private ProtoValue Invoke(string name, ProtoValue source, IReadOnlyList<ProtoValue> args)
    {
        if (!_converters.TryGet(name, out var converter))
            throw new ProtoTypeException(
                $"unknown converter '{name}'. The converter set is closed — adding one is a deliberate "
              + "code change, not something a document can do.");

        return converter!.Apply(source, args);
    }

    private static ProtoValue Numeric2(string name, IReadOnlyList<ProtoValue> args,
                                       Func<long, long, long> ints, Func<double, double, double> reals)
    {
        Require(name, args, 2);
        return args[0] is ProtoValue.Num || args[1] is ProtoValue.Num
            ? ProtoValue.Of(reals(args[0].AsNumber(), args[1].AsNumber()))
            : ProtoValue.Of(ints(args[0].AsInt(), args[1].AsInt()));
    }

    private static void Require(string name, IReadOnlyList<ProtoValue> args, int count)
    {
        if (args.Count != count)
            throw new ProtoTypeException($"{name}() takes exactly {count} argument(s), got {args.Count}");
    }
}
