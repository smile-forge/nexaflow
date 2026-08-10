using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Expressions;

/// <summary>
/// A parsed expression. Kept as a tree rather than a compiled delegate so the validator can walk it —
/// which is what lets "this encode-side expression reads a decode-only layout function" be a validation
/// error instead of a runtime one, and what makes an AI-authored document reviewable before it runs.
/// </summary>
public abstract record Expr
{
    /// <summary>A literal.</summary>
    public sealed record Literal(ProtoValue Value) : Expr;

    /// <summary>A dotted path root — <c>inputs</c>, <c>capture</c>, <c>device</c>, <c>item</c>, …</summary>
    public sealed record Root(string Name) : Expr;

    /// <summary>Member access: <c>a.b</c>.</summary>
    public sealed record Member(Expr Target, string Name) : Expr;

    /// <summary>Index: <c>a[i]</c>. Negative indexes count from the end; out of range yields Null.</summary>
    public sealed record Index(Expr Target, Expr Position) : Expr;

    /// <summary>A call — a function, or a converter reached through the pipeline.</summary>
    public sealed record Call(string Name, IReadOnlyList<Expr> Args) : Expr;

    public sealed record Unary(string Op, Expr Operand) : Expr;

    public sealed record Binary(string Op, Expr Left, Expr Right) : Expr;

    /// <summary><c>c ? a : b</c>.</summary>
    public sealed record Conditional(Expr Condition, Expr WhenTrue, Expr WhenFalse) : Expr;

    /// <summary>
    /// <c>a |&gt; f(b)</c>, which is exactly <c>f(a, b)</c>. Kept as its own node rather than desugared at
    /// parse time so a validator error can quote the pipeline the author actually wrote.
    /// </summary>
    public sealed record Pipeline(Expr Source, Call Target) : Expr;

    /// <summary>
    /// <c>let x = value in body</c> — an immutable binding.
    ///
    /// <para>
    /// Deliberately a binding and not a variable. If a transform wants to <i>mutate</i> something, that is
    /// the signal it is reaching for state the graph should be supplying, and the right response is a
    /// missing derivation edge rather than an assignment operator. Keeping it immutable is what stops the
    /// transform layer from growing into a second, overlapping way to express what the graph already does.
    /// </para>
    /// </summary>
    public sealed record Let(string Name, Expr Value, Expr Body) : Expr;

    /// <summary>
    /// <c>x -&gt; body</c> or <c>(a, b) -&gt; body</c>. Legal <b>only</b> in the argument position of a
    /// higher-order call, so there are no first-class functions and no way to build a recursive one.
    /// </summary>
    public sealed record Lambda(IReadOnlyList<string> Parameters, Expr Body) : Expr;

    /// <summary>Parses <paramref name="source"/>, or throws <see cref="ProtoSyntaxException"/>.</summary>
    public static Expr Parse(string source) => new Parser(source).ParseExpression();

    /// <summary>The immediate sub-expressions, in evaluation order.</summary>
    public IReadOnlyList<Expr> Children => this switch
    {
        Member m => [m.Target],
        Index x => [x.Target, x.Position],
        Call c => [.. c.Args],
        Unary u => [u.Operand],
        Binary b => [b.Left, b.Right],
        Conditional c => [c.Condition, c.WhenTrue, c.WhenFalse],
        Pipeline p => [p.Source, p.Target],
        Let l => [l.Value, l.Body],
        Lambda l => [l.Body],
        _ => [],
    };

    /// <summary>Every descendant including this node — the validator's walk.</summary>
    public IEnumerable<Expr> Descendants()
    {
        yield return this;

        foreach (var child in Children)
            foreach (var d in child.Descendants())
                yield return d;
    }

    /// <summary>Every function/converter name this expression invokes. Backs the closed-set check and the
    /// "no decode-only function on the encode side" rule.</summary>
    public IEnumerable<string> CalledNames()
        => Descendants().OfType<Call>().Select(c => c.Name);

    /// <summary>Every path root this expression reads, e.g. <c>capture</c>, <c>inputs</c>. Backs scope
    /// checks — a message's encode side may not read <c>capture.*</c>, for instance.</summary>
    public IEnumerable<string> RootNames()
        => Descendants().OfType<Root>().Select(r => r.Name);

    /// <summary>
    /// Path roots this expression reads that it does not itself bind — i.e. genuinely free names, with
    /// <c>let</c> bindings and lambda parameters discounted.
    ///
    /// <para>
    /// This is the mechanically-checked containment rule for a transform body: <b>free roots must be a
    /// subset of the declared parameters</b>. No <c>capture</c>, no <c>offset</c>, no positional
    /// vocabulary of any kind. If a transform needs to know <i>where</i> it is, that is the graph's job,
    /// and letting it ask directly is how two mechanisms end up able to express the same thing — the
    /// failure mode that made the equivalent layer in DFDL its worst area.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> FreeRootNames()
    {
        HashSet<string> free = [];
        Collect(this, new HashSet<string>(StringComparer.Ordinal), free);
        return free;

        static void Collect(Expr e, IReadOnlySet<string> bound, HashSet<string> free)
        {
            switch (e)
            {
                case Root r when !bound.Contains(r.Name):
                    free.Add(r.Name);
                    return;

                case Root:
                    return;

                case Let l:
                    Collect(l.Value, bound, free);                       // the value sees the OUTER scope
                    Collect(l.Body, Extend(bound, [l.Name]), free);
                    return;

                case Lambda lam:
                    Collect(lam.Body, Extend(bound, lam.Parameters), free);
                    return;

                default:
                    foreach (var child in e.Children) Collect(child, bound, free);
                    return;
            }
        }

        static HashSet<string> Extend(IReadOnlySet<string> bound, IEnumerable<string> names)
            => [.. bound, .. names];
    }
}
