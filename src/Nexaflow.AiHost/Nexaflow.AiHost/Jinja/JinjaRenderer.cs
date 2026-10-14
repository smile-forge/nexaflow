using System.Text;

namespace Nexaflow.AiHost.Jinja;

/// <summary>The variables one block sees: its own, then its parent's. A loop and a macro call each have their own —
/// what is set in them does not outlive them — while an <c>if</c> shares its parent's.</summary>
internal sealed class JinjaScope(JinjaScope? parent)
{
    private readonly Dictionary<string, object?> _variables = new(StringComparer.Ordinal);

    public bool TryGet(string name, out object? value)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
            if (scope._variables.TryGetValue(name, out value)) return true;
        value = null;
        return false;
    }

    public void Set(string name, object? value) => _variables[name] = value;

    public JinjaScope? Parent { get; } = parent;
}

/// <summary>Renders a parsed template: statements write text, expressions are evaluated by Python's rules
/// (<see cref="JinjaValues"/>), and filters, tests and methods come from <see cref="JinjaBuiltins"/>.</summary>
internal sealed class JinjaRenderer
{
    private enum Flow { Normal, Break, Continue }

    /// <summary>How deep macros may call themselves before the template is taken to never end.</summary>
    private const int MaxCallDepth = 200;

    private int _depth;

    public string Render(IReadOnlyList<JinjaNode> nodes, JinjaScope scope)
    {
        var output = new StringBuilder();
        Execute(nodes, scope, output);
        return output.ToString();
    }

    private Flow Execute(IReadOnlyList<JinjaNode> nodes, JinjaScope scope, StringBuilder output)
    {
        foreach (var node in nodes)
        {
            var flow = Execute(node, scope, output);
            if (flow != Flow.Normal) return flow;
        }
        return Flow.Normal;
    }

    private Flow Execute(JinjaNode node, JinjaScope scope, StringBuilder output)
    {
        switch (node)
        {
            case TextNode text:
                output.Append(text.Text);
                return Flow.Normal;
            case OutputNode print:
                output.Append(JinjaValues.Str(Evaluate(print.Value, scope)));
                return Flow.Normal;
            case IfNode branch:
                foreach (var (test, body) in branch.Branches)
                    if (JinjaValues.IsTrue(Evaluate(test, scope))) return Execute(body, scope, output);
                return branch.Else is { } otherwise ? Execute(otherwise, scope, output) : Flow.Normal;
            case ForNode loop:
                return Loop(loop, scope, output);
            case SetNode set:
                Assign(set.Target, Evaluate(set.Value, scope), scope, set.Line);
                return Flow.Normal;
            case SetBlockNode block:
                var captured = new StringBuilder();
                Execute(block.Body, scope, captured);
                object? value = captured.ToString();
                foreach (var filter in block.Filters) value = ApplyFilter(filter, value, scope);
                Assign(block.Target, value, scope, block.Line);
                return Flow.Normal;
            case MacroNode macro:
                scope.Set(macro.Name, new JinjaMacro(macro, scope));
                return Flow.Normal;
            case BodyNode body:
                return Execute(body.Body, scope, output);
            case BreakNode:
                return Flow.Break;
            case ContinueNode:
                return Flow.Continue;
            default:
                throw new JinjaException($"Unknown statement {node.GetType().Name}.", node.Line);
        }
    }

    private Flow Loop(ForNode loop, JinjaScope scope, StringBuilder output)
    {
        var inner = new JinjaScope(scope);
        var all = JinjaValues.Items(Evaluate(loop.Items, scope), loop.Line);
        IReadOnlyList<object?> items = all;
        if (loop.Filter is { } filter)
        {
            var kept = new List<object?>();
            foreach (var item in all)
            {
                Bind(loop, item, inner);
                if (JinjaValues.IsTrue(Evaluate(filter, inner))) kept.Add(item);
            }
            items = kept;
        }

        if (items.Count == 0)
            return loop.Else is { } otherwise ? Execute(otherwise, scope, output) : Flow.Normal;

        var state = new JinjaLoop(items);
        inner.Set("loop", state);
        for (int i = 0; i < items.Count; i++)
        {
            state.Index0 = i;
            Bind(loop, items[i], inner);
            if (Execute(loop.Body, inner, output) == Flow.Break) break;
        }
        return Flow.Normal;
    }

    private static void Bind(ForNode loop, object? item, JinjaScope scope)
    {
        if (loop.Targets.Count == 1)
        {
            scope.Set(loop.Targets[0], item);
            return;
        }
        if (item is not IList<object?> parts || parts.Count != loop.Targets.Count)
            throw new JinjaException($"Each item must unpack into {loop.Targets.Count} values.", loop.Line);
        for (int i = 0; i < parts.Count; i++) scope.Set(loop.Targets[i], parts[i]);
    }

    private void Assign(JinjaExpr target, object? value, JinjaScope scope, int line)
    {
        switch (target)
        {
            case NameExpr name:
                scope.Set(name.Name, value);
                return;
            case AttrExpr { Target: NameExpr owner } attribute:
                if (Evaluate(owner, scope) is not JinjaNamespace ns)
                    throw new JinjaException($"Only a namespace's attributes can be set, not '{owner.Name}.{attribute.Name}'.", line);
                ns.Attributes[attribute.Name] = value;
                return;
            case TupleExpr tuple:
                if (value is not IList<object?> parts || parts.Count != tuple.Items.Count)
                    throw new JinjaException($"The value must unpack into {tuple.Items.Count} values.", line);
                for (int i = 0; i < parts.Count; i++) Assign(tuple.Items[i], parts[i], scope, line);
                return;
            default:
                throw new JinjaException("Cannot assign to this.", line);
        }
    }

    public object? Evaluate(JinjaExpr expr, JinjaScope scope)
    {
        switch (expr)
        {
            case ConstExpr constant:
                return constant.Value;
            case NameExpr name:
                if (scope.TryGet(name.Name, out var value)) return value;
                return JinjaBuiltins.Global(name.Name) ?? new JinjaUndefined(name.Name);
            case ListExpr list:
                return list.Items.Select(i => Evaluate(i, scope)).ToList();
            case TupleExpr tuple:
                return new JinjaTuple(tuple.Items.Select(i => Evaluate(i, scope)));
            case DictExpr dict:
                return JinjaValues.Mapping(dict.Pairs.Select(p => (JinjaValues.Str(Evaluate(p.Key, scope)), Evaluate(p.Value, scope))));
            case AttrExpr attribute:
                return JinjaBuiltins.Attribute(Evaluate(attribute.Target, scope), attribute.Name, attribute.Line);
            case ItemExpr item:
                return JinjaBuiltins.Item(Evaluate(item.Target, scope), Evaluate(item.Key, scope), item.Line);
            case SliceExpr slice:
                return JinjaBuiltins.Slice(Evaluate(slice.Target, scope), Optional(slice.Start, scope), Optional(slice.Stop, scope),
                                           Optional(slice.Step, scope), slice.Line);
            case CallExpr call:
                return Call(Evaluate(call.Target, scope), call.Arguments, scope, call.Line);
            case FilterExpr filter:
                return ApplyFilter(filter.Filter, Evaluate(filter.Target, scope), scope);
            case TestExpr test:
                var (positional, named) = Arguments(test.Arguments, scope);
                bool passed = JinjaBuiltins.Test(test.Name, Evaluate(test.Target, scope), positional, named, this, test.Line);
                return passed != test.Negated;
            case UnaryExpr unary:
                var operand = Evaluate(unary.Operand, scope);
                return unary.Op switch
                {
                    "not" => !JinjaValues.IsTrue(operand),
                    "-"   => operand switch
                    {
                        long l   => (object?)-l,
                        double d => -d,
                        bool b   => b ? -1L : 0L,
                        _        => throw new JinjaException($"Cannot negate {JinjaValues.TypeName(operand)}.", unary.Line),
                    },
                    _     => JinjaValues.IsNumber(operand) ? operand : throw new JinjaException($"Cannot apply '+' to {JinjaValues.TypeName(operand)}.", unary.Line),
                };
            case BinaryExpr binary:
                if (binary.Op == "and")
                {
                    var left = Evaluate(binary.Left, scope);
                    return JinjaValues.IsTrue(left) ? Evaluate(binary.Right, scope) : left;
                }
                if (binary.Op == "or")
                {
                    var left = Evaluate(binary.Left, scope);
                    return JinjaValues.IsTrue(left) ? left : Evaluate(binary.Right, scope);
                }
                return JinjaValues.Arithmetic(binary.Op, Evaluate(binary.Left, scope), Evaluate(binary.Right, scope), binary.Line);
            case CompareExpr compare:
                var current = Evaluate(compare.First, scope);
                foreach (var (op, rightExpr) in compare.Operations)
                {
                    var right = Evaluate(rightExpr, scope);
                    bool holds = op switch
                    {
                        "=="     => JinjaValues.Equal(current, right),
                        "!="     => !JinjaValues.Equal(current, right),
                        "<"      => JinjaValues.Compare(current, right, compare.Line) < 0,
                        "<="     => JinjaValues.Compare(current, right, compare.Line) <= 0,
                        ">"      => JinjaValues.Compare(current, right, compare.Line) > 0,
                        ">="     => JinjaValues.Compare(current, right, compare.Line) >= 0,
                        "in"     => JinjaValues.Contains(right, current, compare.Line),
                        _        => !JinjaValues.Contains(right, current, compare.Line),
                    };
                    if (!holds) return false;
                    current = right;
                }
                return true;
            case ConditionalExpr conditional:
                if (JinjaValues.IsTrue(Evaluate(conditional.Test, scope))) return Evaluate(conditional.Then, scope);
                return conditional.Otherwise is { } otherwise ? Evaluate(otherwise, scope) : new JinjaUndefined("the conditional's else");
            default:
                throw new JinjaException($"Unknown expression {expr.GetType().Name}.", expr.Line);
        }
    }

    private object? Optional(JinjaExpr? expr, JinjaScope scope) => expr is null ? null : Evaluate(expr, scope);

    private (List<object?> Positional, Dictionary<string, object?> Named) Arguments(CallArguments arguments, JinjaScope scope)
        => (arguments.Positional.Select(a => Evaluate(a, scope)).ToList(),
            arguments.Named.ToDictionary(a => a.Name, a => Evaluate(a.Value, scope), StringComparer.Ordinal));

    private object? ApplyFilter(FilterCall filter, object? value, JinjaScope scope)
    {
        var (positional, named) = Arguments(filter.Arguments, scope);
        return JinjaBuiltins.Filter(filter.Name, value, positional, named, this, filter.Line);
    }

    private object? Call(object? target, CallArguments arguments, JinjaScope scope, int line)
    {
        var (positional, named) = Arguments(arguments, scope);
        return Invoke(target, positional, named, line);
    }

    /// <summary>Calls a macro or a function with arguments already evaluated.</summary>
    public object? Invoke(object? target, IReadOnlyList<object?> positional, IReadOnlyDictionary<string, object?> named, int line)
    {
        switch (target)
        {
            case JinjaCallable function:
                return function.Invoke(positional, named);
            case JinjaMacro macro:
                return CallMacro(macro, positional, named, line);
            case JinjaUndefined undefined:
                throw new JinjaException($"'{undefined.Name}' is undefined and cannot be called.", line);
            default:
                throw new JinjaException($"{JinjaValues.TypeName(target)} cannot be called.", line);
        }
    }

    private string CallMacro(JinjaMacro macro, IReadOnlyList<object?> positional, IReadOnlyDictionary<string, object?> named, int line)
    {
        var parameters = macro.Node.Parameters;
        if (positional.Count > parameters.Count)
            throw new JinjaException($"Macro '{macro.Node.Name}' takes {parameters.Count} arguments, not {positional.Count}.", line);
        foreach (var name in named.Keys)
            if (!parameters.Any(p => p.Name == name))
                throw new JinjaException($"Macro '{macro.Node.Name}' has no argument '{name}'.", line);

        if (++_depth > MaxCallDepth) throw new JinjaException($"Macro '{macro.Node.Name}' calls itself too deeply.", line);
        try
        {
            var scope = new JinjaScope(macro.Scope);
            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                object? value = i < positional.Count ? positional[i]
                              : named.TryGetValue(parameter.Name, out var given) ? given
                              : parameter.Default is { } fallback ? Evaluate(fallback, scope)
                              : new JinjaUndefined(parameter.Name);
                scope.Set(parameter.Name, value);
            }
            var output = new StringBuilder();
            Execute(macro.Node.Body, scope, output);
            return output.ToString();
        }
        finally
        {
            _depth--;
        }
    }
}
