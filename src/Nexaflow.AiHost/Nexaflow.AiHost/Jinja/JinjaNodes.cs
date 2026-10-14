namespace Nexaflow.AiHost.Jinja;

/// <summary>A statement of a template: text, an output, or a tag with a body.</summary>
internal abstract record JinjaNode(int Line);

internal sealed record TextNode(int Line, string Text) : JinjaNode(Line);

internal sealed record OutputNode(int Line, JinjaExpr Value) : JinjaNode(Line);

internal sealed record IfNode(int Line, IReadOnlyList<(JinjaExpr Test, IReadOnlyList<JinjaNode> Body)> Branches, IReadOnlyList<JinjaNode>? Else)
    : JinjaNode(Line);

/// <param name="Targets">The names each item is bound to — several when the items are unpacked.</param>
/// <param name="Filter">The <c>if</c> that drops items before the loop counts them.</param>
internal sealed record ForNode(int Line, IReadOnlyList<string> Targets, JinjaExpr Items, JinjaExpr? Filter, IReadOnlyList<JinjaNode> Body,
                               IReadOnlyList<JinjaNode>? Else) : JinjaNode(Line);

/// <param name="Target">A name, a namespace's attribute, or a tuple of names.</param>
internal sealed record SetNode(int Line, JinjaExpr Target, JinjaExpr Value) : JinjaNode(Line);

/// <summary><c>{% set x %}…{% endset %}</c>: the body rendered, through any filters, into the target.</summary>
internal sealed record SetBlockNode(int Line, JinjaExpr Target, IReadOnlyList<FilterCall> Filters, IReadOnlyList<JinjaNode> Body) : JinjaNode(Line);

internal sealed record MacroNode(int Line, string Name, IReadOnlyList<MacroParameter> Parameters, IReadOnlyList<JinjaNode> Body) : JinjaNode(Line);

internal sealed record MacroParameter(string Name, JinjaExpr? Default);

/// <summary>A block whose body is rendered as it stands — transformers' <c>generation</c> tag, which only marks what
/// the assistant wrote.</summary>
internal sealed record BodyNode(int Line, IReadOnlyList<JinjaNode> Body) : JinjaNode(Line);

internal sealed record BreakNode(int Line) : JinjaNode(Line);

internal sealed record ContinueNode(int Line) : JinjaNode(Line);

/// <summary>An expression of a template.</summary>
internal abstract record JinjaExpr(int Line);

internal sealed record ConstExpr(int Line, object? Value) : JinjaExpr(Line);

internal sealed record NameExpr(int Line, string Name) : JinjaExpr(Line);

internal sealed record ListExpr(int Line, IReadOnlyList<JinjaExpr> Items) : JinjaExpr(Line);

internal sealed record TupleExpr(int Line, IReadOnlyList<JinjaExpr> Items) : JinjaExpr(Line);

internal sealed record DictExpr(int Line, IReadOnlyList<(JinjaExpr Key, JinjaExpr Value)> Pairs) : JinjaExpr(Line);

internal sealed record AttrExpr(int Line, JinjaExpr Target, string Name) : JinjaExpr(Line);

internal sealed record ItemExpr(int Line, JinjaExpr Target, JinjaExpr Key) : JinjaExpr(Line);

internal sealed record SliceExpr(int Line, JinjaExpr Target, JinjaExpr? Start, JinjaExpr? Stop, JinjaExpr? Step) : JinjaExpr(Line);

internal sealed record CallExpr(int Line, JinjaExpr Target, CallArguments Arguments) : JinjaExpr(Line);

internal sealed record FilterExpr(int Line, JinjaExpr Target, FilterCall Filter) : JinjaExpr(Line);

internal sealed record TestExpr(int Line, JinjaExpr Target, string Name, CallArguments Arguments, bool Negated) : JinjaExpr(Line);

/// <param name="Op"><c>not</c>, <c>-</c> or <c>+</c>.</param>
internal sealed record UnaryExpr(int Line, string Op, JinjaExpr Operand) : JinjaExpr(Line);

/// <param name="Op">An arithmetic operator, <c>~</c>, <c>and</c> or <c>or</c>.</param>
internal sealed record BinaryExpr(int Line, string Op, JinjaExpr Left, JinjaExpr Right) : JinjaExpr(Line);

/// <summary>A chain of comparisons, Python's way: <c>a &lt; b &lt;= c</c> is <c>a &lt; b and b &lt;= c</c>.</summary>
/// <param name="Operations">Each operator — <c>==</c>, <c>!=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>,
/// <c>in</c> or <c>not in</c> — and what is on its right.</param>
internal sealed record CompareExpr(int Line, JinjaExpr First, IReadOnlyList<(string Op, JinjaExpr Right)> Operations) : JinjaExpr(Line);

/// <summary><c>then if test else otherwise</c>; with no <c>else</c>, undefined when the test fails.</summary>
internal sealed record ConditionalExpr(int Line, JinjaExpr Test, JinjaExpr Then, JinjaExpr? Otherwise) : JinjaExpr(Line);

internal sealed record CallArguments(IReadOnlyList<JinjaExpr> Positional, IReadOnlyList<(string Name, JinjaExpr Value)> Named)
{
    public static CallArguments None { get; } = new([], []);
}

internal sealed record FilterCall(string Name, CallArguments Arguments, int Line);
