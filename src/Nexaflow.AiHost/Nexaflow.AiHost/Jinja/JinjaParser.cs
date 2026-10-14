namespace Nexaflow.AiHost.Jinja;

/// <summary>
/// A template's tokens as statements and expressions, with Jinja's grammar and precedence: a conditional expression
/// above <c>or</c>, <c>and</c> and <c>not</c>; comparisons, <c>in</c> and <c>not in</c>; then <c>+ -</c>, <c>~</c>,
/// <c>* / // %</c>, <c>**</c> and the unary signs; and tightest of all, attributes, subscripts and calls, then filters
/// and tests — so <c>a | upper == 'X'</c> filters before it compares.
/// </summary>
internal sealed class JinjaParser
{
    private readonly List<JinjaToken> _tokens;
    private int _pos;

    private JinjaParser(List<JinjaToken> tokens) => _tokens = tokens;

    public static IReadOnlyList<JinjaNode> Parse(string source)
    {
        // Jinja drops the newline a template file ends with (keep_trailing_newline is off).
        if (source.EndsWith("\r\n", StringComparison.Ordinal)) source = source[..^2];
        else if (source.EndsWith('\n')) source = source[..^1];
        var parser = new JinjaParser(JinjaLexer.Tokenize(source));
        var body = parser.Body(out string? end);
        if (end is not null) throw new JinjaException($"'{end}' has no block to end.", parser.Current.Line);
        return body;
    }

    private JinjaToken Current => _tokens[_pos];

    private JinjaToken Peek(int ahead = 1) => _tokens[Math.Min(_pos + ahead, _tokens.Count - 1)];

    private JinjaToken Next() => _tokens[_pos++];

    private bool SkipOperator(string op)
    {
        if (!Current.IsOperator(op)) return false;
        _pos++;
        return true;
    }

    private bool SkipName(string name)
    {
        if (!Current.IsName(name)) return false;
        _pos++;
        return true;
    }

    private void ExpectOperator(string op)
    {
        if (!SkipOperator(op)) throw Unexpected($"'{op}'");
    }

    private void ExpectName(string name)
    {
        if (!SkipName(name)) throw Unexpected($"'{name}'");
    }

    private string ExpectIdentifier()
    {
        if (Current.Kind != JinjaTokenKind.Name) throw Unexpected("a name");
        return Next().Text;
    }

    private void Expect(JinjaTokenKind kind)
    {
        if (Current.Kind != kind) throw Unexpected(kind.ToString());
        _pos++;
    }

    private JinjaException Unexpected(string expected)
    {
        string found = Current.Kind switch
        {
            JinjaTokenKind.End           => "the end of the template",
            JinjaTokenKind.StatementEnd  => "the end of the tag",
            JinjaTokenKind.OutputEnd     => "the end of the output",
            _                            => $"'{Current.Text}'",
        };
        return new JinjaException($"Expected {expected}, found {found}.", Current.Line);
    }

    // ---- statements --------------------------------------------------------------------------------------------

    /// <summary>Statements up to the end of the template, or up to a tag whose name is one of
    /// <paramref name="ends"/>; that name is returned in <paramref name="end"/>, its tag left to be read.</summary>
    private List<JinjaNode> Body(out string? end, params string[] ends)
    {
        var nodes = new List<JinjaNode>();
        while (true)
        {
            var token = Current;
            switch (token.Kind)
            {
                case JinjaTokenKind.End:
                    if (ends.Length > 0) throw new JinjaException($"Expected '{string.Join("' or '", ends)}' before the end of the template.", token.Line);
                    end = null;
                    return nodes;
                case JinjaTokenKind.Text:
                    nodes.Add(new TextNode(token.Line, Next().Text));
                    break;
                case JinjaTokenKind.OutputBegin:
                    _pos++;
                    nodes.Add(new OutputNode(token.Line, Tuple()));
                    Expect(JinjaTokenKind.OutputEnd);
                    break;
                case JinjaTokenKind.StatementBegin:
                    var name = Peek();
                    if (name.Kind == JinjaTokenKind.Name && ends.Contains(name.Text))
                    {
                        end = name.Text;
                        return nodes;
                    }
                    if (name.Kind == JinjaTokenKind.Name && name.Text.StartsWith("end", StringComparison.Ordinal))
                    {
                        if (ends.Length == 0) { end = name.Text; return nodes; }
                        throw new JinjaException($"'{name.Text}' where '{string.Join("' or '", ends)}' was expected.", name.Line);
                    }
                    _pos++;
                    nodes.Add(Statement());
                    break;
                default:
                    throw Unexpected("text or a tag");
            }
        }
    }

    /// <summary>Reads the closing tag the body ended at: <c>{% name %}</c>.</summary>
    private void EndTag(string name)
    {
        Expect(JinjaTokenKind.StatementBegin);
        ExpectName(name);
        Expect(JinjaTokenKind.StatementEnd);
    }

    private JinjaNode Statement()
    {
        var token = Current;
        if (token.Kind != JinjaTokenKind.Name) throw Unexpected("a statement");
        _pos++;
        switch (token.Text)
        {
            case "if":         return If(token.Line);
            case "for":        return For(token.Line);
            case "set":        return Set(token.Line);
            case "macro":      return Macro(token.Line);
            case "generation": return Generation(token.Line);
            case "break":      Expect(JinjaTokenKind.StatementEnd); return new BreakNode(token.Line);
            case "continue":   Expect(JinjaTokenKind.StatementEnd); return new ContinueNode(token.Line);
            default:           throw new JinjaException($"'{token.Text}' is not a statement chat templates can use.", token.Line);
        }
    }

    private IfNode If(int line)
    {
        var branches = new List<(JinjaExpr, IReadOnlyList<JinjaNode>)>();
        var test = Tuple();
        while (true)
        {
            Expect(JinjaTokenKind.StatementEnd);
            var body = Body(out string? end, "elif", "else", "endif");
            branches.Add((test, body));
            Expect(JinjaTokenKind.StatementBegin);
            Next();
            if (end == "elif")
            {
                test = Tuple();
                continue;
            }
            if (end == "else")
            {
                Expect(JinjaTokenKind.StatementEnd);
                var otherwise = Body(out _, "endif");
                EndTag("endif");
                return new IfNode(line, branches, otherwise);
            }
            Expect(JinjaTokenKind.StatementEnd);
            return new IfNode(line, branches, null);
        }
    }

    private ForNode For(int line)
    {
        var targets = new List<string> { ExpectIdentifier() };
        while (SkipOperator(",")) targets.Add(ExpectIdentifier());
        ExpectName("in");
        var items = Tuple(conditional: false, endName: "recursive");
        JinjaExpr? filter = SkipName("if") ? Expression() : null;
        if (Current.IsName("recursive")) throw new JinjaException("Recursive loops are not supported.", Current.Line);
        Expect(JinjaTokenKind.StatementEnd);
        var body = Body(out string? end, "else", "endfor");
        IReadOnlyList<JinjaNode>? otherwise = null;
        if (end == "else")
        {
            Expect(JinjaTokenKind.StatementBegin);
            Next();
            Expect(JinjaTokenKind.StatementEnd);
            otherwise = Body(out _, "endfor");
        }
        EndTag("endfor");
        return new ForNode(line, targets, items, filter, body, otherwise);
    }

    private JinjaNode Set(int line)
    {
        var target = AssignTarget();
        if (SkipOperator("="))
        {
            var value = Tuple();
            Expect(JinjaTokenKind.StatementEnd);
            return new SetNode(line, target, value);
        }
        var filters = new List<FilterCall>();
        while (SkipOperator("|")) filters.Add(Filter());
        Expect(JinjaTokenKind.StatementEnd);
        var body = Body(out _, "endset");
        EndTag("endset");
        return new SetBlockNode(line, target, filters, body);
    }

    /// <summary>What a <c>set</c> assigns to: a name, a namespace's attribute, or names to unpack into.</summary>
    private JinjaExpr AssignTarget()
    {
        int line = Current.Line;
        string name = ExpectIdentifier();
        if (SkipOperator(".")) return new AttrExpr(line, new NameExpr(line, name), ExpectIdentifier());
        if (!Current.IsOperator(",")) return new NameExpr(line, name);
        var names = new List<JinjaExpr> { new NameExpr(line, name) };
        while (SkipOperator(",")) names.Add(new NameExpr(Current.Line, ExpectIdentifier()));
        return new TupleExpr(line, names);
    }

    private MacroNode Macro(int line)
    {
        string name = ExpectIdentifier();
        var parameters = new List<MacroParameter>();
        ExpectOperator("(");
        while (!SkipOperator(")"))
        {
            if (parameters.Count > 0) ExpectOperator(",");
            if (SkipOperator(")")) break;
            string parameter = ExpectIdentifier();
            parameters.Add(new MacroParameter(parameter, SkipOperator("=") ? Expression() : null));
        }
        Expect(JinjaTokenKind.StatementEnd);
        var body = Body(out _, "endmacro");
        EndTag("endmacro");
        return new MacroNode(line, name, parameters, body);
    }

    private BodyNode Generation(int line)
    {
        Expect(JinjaTokenKind.StatementEnd);
        var body = Body(out _, "endgeneration");
        EndTag("endgeneration");
        return new BodyNode(line, body);
    }

    // ---- expressions -------------------------------------------------------------------------------------------

    /// <summary>An expression, or several separated by commas — a tuple, as Jinja allows wherever one value goes.
    /// <paramref name="endName"/> is a name that ends the expression rather than continuing it.</summary>
    private JinjaExpr Tuple(bool conditional = true, string? endName = null)
    {
        int line = Current.Line;
        var first = conditional ? Expression() : Or();
        if (!Current.IsOperator(",")) return first;
        var items = new List<JinjaExpr> { first };
        while (SkipOperator(","))
        {
            if (AtTupleEnd(endName)) break;
            items.Add(conditional ? Expression() : Or());
        }
        return new TupleExpr(line, items);
    }

    private bool AtTupleEnd(string? endName)
        => Current.Kind is JinjaTokenKind.StatementEnd or JinjaTokenKind.OutputEnd or JinjaTokenKind.End
           || Current.IsOperator(")") || (endName is not null && Current.IsName(endName)) || Current.IsName("if");

    private JinjaExpr Expression()
    {
        var then = Or();
        while (Current.IsName("if"))
        {
            int line = Next().Line;
            var test = Or();
            JinjaExpr? otherwise = SkipName("else") ? Expression() : null;
            then = new ConditionalExpr(line, test, then, otherwise);
        }
        return then;
    }

    private JinjaExpr Or()
    {
        var left = And();
        while (Current.IsName("or"))
        {
            int line = Next().Line;
            left = new BinaryExpr(line, "or", left, And());
        }
        return left;
    }

    private JinjaExpr And()
    {
        var left = Not();
        while (Current.IsName("and"))
        {
            int line = Next().Line;
            left = new BinaryExpr(line, "and", left, Not());
        }
        return left;
    }

    private JinjaExpr Not()
    {
        if (Current.IsName("not"))
        {
            int line = Next().Line;
            return new UnaryExpr(line, "not", Not());
        }
        return Compare();
    }

    private JinjaExpr Compare()
    {
        int line = Current.Line;
        var first = Sum();
        var operations = new List<(string, JinjaExpr)>();
        while (true)
        {
            if (Current.Kind == JinjaTokenKind.Operator && Current.Text is "==" or "!=" or "<" or "<=" or ">" or ">=")
                operations.Add((Next().Text, Sum()));
            else if (Current.IsName("in"))
            {
                _pos++;
                operations.Add(("in", Sum()));
            }
            else if (Current.IsName("not") && Peek().IsName("in"))
            {
                _pos += 2;
                operations.Add(("not in", Sum()));
            }
            else break;
        }
        return operations.Count == 0 ? first : new CompareExpr(line, first, operations);
    }

    private JinjaExpr Sum()
    {
        var left = Concat();
        while (Current.Kind == JinjaTokenKind.Operator && Current.Text is "+" or "-")
        {
            var op = Next();
            left = new BinaryExpr(op.Line, op.Text, left, Concat());
        }
        return left;
    }

    private JinjaExpr Concat()
    {
        var left = Product();
        while (Current.IsOperator("~"))
        {
            int line = Next().Line;
            left = new BinaryExpr(line, "~", left, Product());
        }
        return left;
    }

    private JinjaExpr Product()
    {
        var left = Power();
        while (Current.Kind == JinjaTokenKind.Operator && Current.Text is "*" or "/" or "//" or "%")
        {
            var op = Next();
            left = new BinaryExpr(op.Line, op.Text, left, Power());
        }
        return left;
    }

    private JinjaExpr Power()
    {
        var left = Unary(withFilters: true);
        while (Current.IsOperator("**"))
        {
            int line = Next().Line;
            left = new BinaryExpr(line, "**", left, Unary(withFilters: true));
        }
        return left;
    }

    private JinjaExpr Unary(bool withFilters)
    {
        JinjaExpr node;
        if (Current.Kind == JinjaTokenKind.Operator && Current.Text is "-" or "+")
        {
            var op = Next();
            node = new UnaryExpr(op.Line, op.Text, Unary(withFilters: false));
        }
        else node = Postfix(Primary());
        return withFilters ? FiltersAndTests(node) : node;
    }

    private JinjaExpr Primary()
    {
        var token = Next();
        switch (token.Kind)
        {
            case JinjaTokenKind.Name:
                return token.Text switch
                {
                    "true" or "True"   => new ConstExpr(token.Line, true),
                    "false" or "False" => new ConstExpr(token.Line, false),
                    "none" or "None"   => new ConstExpr(token.Line, null),
                    _                  => new NameExpr(token.Line, token.Text),
                };
            case JinjaTokenKind.String:
                // Adjacent string literals are one string, as in Python.
                string text = token.Text;
                while (Current.Kind == JinjaTokenKind.String) text += Next().Text;
                return new ConstExpr(token.Line, text);
            case JinjaTokenKind.Integer:
            case JinjaTokenKind.Float:
                return new ConstExpr(token.Line, token.Value);
            case JinjaTokenKind.Operator when token.Text == "(":
                if (SkipOperator(")")) return new TupleExpr(token.Line, []);
                var inner = Expression();
                if (Current.IsOperator(","))
                {
                    var items = new List<JinjaExpr> { inner };
                    while (SkipOperator(",") && !Current.IsOperator(")")) items.Add(Expression());
                    inner = new TupleExpr(token.Line, items);
                }
                ExpectOperator(")");
                return inner;
            case JinjaTokenKind.Operator when token.Text == "[":
                var list = new List<JinjaExpr>();
                while (!SkipOperator("]"))
                {
                    if (list.Count > 0) ExpectOperator(",");
                    if (SkipOperator("]")) break;
                    list.Add(Expression());
                }
                return new ListExpr(token.Line, list);
            case JinjaTokenKind.Operator when token.Text == "{":
                var pairs = new List<(JinjaExpr, JinjaExpr)>();
                while (!SkipOperator("}"))
                {
                    if (pairs.Count > 0) ExpectOperator(",");
                    if (SkipOperator("}")) break;
                    var key = Expression();
                    ExpectOperator(":");
                    pairs.Add((key, Expression()));
                }
                return new DictExpr(token.Line, pairs);
            default:
                _pos--;
                throw Unexpected("a value");
        }
    }

    private JinjaExpr Postfix(JinjaExpr node)
    {
        while (true)
        {
            if (Current.IsOperator("."))
            {
                int line = Next().Line;
                var member = Next();
                node = member.Kind switch
                {
                    JinjaTokenKind.Name    => new AttrExpr(line, node, member.Text),
                    JinjaTokenKind.Integer => new ItemExpr(line, node, new ConstExpr(line, member.Value)),
                    _                      => throw new JinjaException($"Expected an attribute after '.', found '{member.Text}'.", member.Line),
                };
            }
            else if (Current.IsOperator("[")) node = Subscript(node);
            else if (Current.IsOperator("(")) node = new CallExpr(Current.Line, node, Arguments());
            else return node;
        }
    }

    private JinjaExpr Subscript(JinjaExpr target)
    {
        int line = Next().Line;
        JinjaExpr? start = null, stop = null, step = null;
        if (!Current.IsOperator(":")) start = Expression();
        if (!Current.IsOperator(":"))
        {
            ExpectOperator("]");
            return new ItemExpr(line, target, start!);
        }
        _pos++;
        if (!Current.IsOperator("]") && !Current.IsOperator(":")) stop = Expression();
        if (SkipOperator(":") && !Current.IsOperator("]")) step = Expression();
        ExpectOperator("]");
        return new SliceExpr(line, target, start, stop, step);
    }

    private CallArguments Arguments()
    {
        ExpectOperator("(");
        var positional = new List<JinjaExpr>();
        var named = new List<(string, JinjaExpr)>();
        while (!SkipOperator(")"))
        {
            if (positional.Count + named.Count > 0) ExpectOperator(",");
            if (SkipOperator(")")) break;
            if (Current.Kind == JinjaTokenKind.Name && Peek().IsOperator("="))
            {
                string name = Next().Text;
                _pos++;
                named.Add((name, Expression()));
            }
            else
            {
                if (named.Count > 0) throw new JinjaException("A positional argument follows a named one.", Current.Line);
                positional.Add(Expression());
            }
        }
        return new CallArguments(positional, named);
    }

    private JinjaExpr FiltersAndTests(JinjaExpr node)
    {
        while (true)
        {
            if (Current.IsOperator("|"))
            {
                int line = Next().Line;
                node = new FilterExpr(line, node, Filter());
            }
            else if (Current.IsName("is"))
            {
                int line = Next().Line;
                bool negated = SkipName("not");
                string name = DottedName();
                CallArguments arguments;
                if (Current.IsOperator("(")) arguments = Arguments();
                else if (TakesBareArgument()) arguments = new([Postfix(Primary())], []);
                else arguments = CallArguments.None;
                node = new TestExpr(line, node, name, arguments, negated);
            }
            else if (Current.IsOperator("(")) node = new CallExpr(Current.Line, node, Arguments());
            else return node;
        }
    }

    /// <summary>Whether a test is followed by its one argument written without parentheses — <c>x is divisibleby 3</c>,
    /// <c>x is sameas none</c> — rather than by what comes after the test.</summary>
    private bool TakesBareArgument()
    {
        var token = Current;
        if (token.Kind is JinjaTokenKind.String or JinjaTokenKind.Integer or JinjaTokenKind.Float) return true;
        if (token.Kind == JinjaTokenKind.Operator) return token.Text is "[" or "{";
        if (token.Kind != JinjaTokenKind.Name) return false;
        if (token.Text == "is") throw new JinjaException("Tests cannot be chained with 'is'.", token.Line);
        return token.Text is not ("else" or "or" or "and" or "if" or "in" or "not" or "recursive");
    }

    private FilterCall Filter()
    {
        int line = Current.Line;
        string name = DottedName();
        return new FilterCall(name, Current.IsOperator("(") ? Arguments() : CallArguments.None, line);
    }

    private string DottedName()
    {
        string name = ExpectIdentifier();
        while (Current.IsOperator(".") && Peek().Kind == JinjaTokenKind.Name)
        {
            _pos++;
            name += "." + Next().Text;
        }
        return name;
    }
}
