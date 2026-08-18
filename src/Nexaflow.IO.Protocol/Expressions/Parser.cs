using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Expressions;

/// <summary>
/// Precedence-climbing parser for the expression language.
///
/// <para>
/// Precedence, tightest first: postfix, unary, <c>* / %</c>, <c>+ -</c>, shifts, <c>&amp;</c>, <c>^</c>,
/// <c>|</c>, <b>pipeline <c>|&gt;</c></b>, relational, equality, <c>&amp;&amp;</c>, <c>||</c>, <c>??</c>,
/// <c>?:</c>. See <see cref="ParsePipeline"/> for why the pipeline sits where it does rather than where
/// the specification's table puts it.
/// </para>
///
/// <para>
/// <c>^</c> is bitwise xor here, not exponentiation. Documents wanting a power must write
/// <c>pow(a, b)</c>: <c>2 ^ 6</c> is 4, and silently treating it as 64 is precisely the class of bug that
/// makes a protocol "work" against one device and fail against the next.
/// </para>
/// </summary>
internal sealed class Parser(string source)
{
    private readonly List<Token> _tokens = new Lexer(source).Tokenise();
    private int _pos;

    private Token Current => _tokens[_pos];
    private Token Advance() => _tokens[_pos++];
    private bool Match(TokenKind kind)
    {
        if (Current.Kind != kind) return false;
        _pos++;
        return true;
    }

    private Token Expect(TokenKind kind, string what)
        => Current.Kind == kind
            ? Advance()
            : throw new ProtoSyntaxException($"expected {what}, found '{Current.Textual}'", Current.Position);

    public Expr ParseExpression()
    {
        var e = ParseTop();
        if (Current.Kind != TokenKind.End)
            throw new ProtoSyntaxException($"unexpected trailing '{Current.Textual}'", Current.Position);
        return e;
    }

    /// <summary>The whole grammar. Every nested position — call arguments, index expressions, parenthesised
    /// groups — enters here, so a pipeline and a binding are legal anywhere a value is.</summary>
    private Expr ParseTop()
    {
        if (Current.Kind == TokenKind.Ident && Current.Textual == "let") return ParseLet();
        if (LambdaAhead()) return ParseLambda();
        return ParseConditional();
    }

    /// <summary><c>let x = value in body</c>. Immutable, and there is no assignment operator anywhere
    /// else — a transform that wants to mutate has found a missing graph edge, not a missing feature.</summary>
    private Expr ParseLet()
    {
        Advance();                                                  // 'let'
        var name = Expect(TokenKind.Ident, "a binding name after 'let'");
        Expect(TokenKind.Assign, "'=' after the binding name");
        var value = ParseTop();

        if (Current.Kind != TokenKind.Ident || Current.Textual != "in")
            throw new ProtoSyntaxException($"expected 'in' after the bound value, found '{Current.Textual}'",
                                           Current.Position);
        Advance();                                                  // 'in'

        return new Expr.Let(name.Textual, value, ParseTop());
    }

    /// <summary>
    /// Is a lambda starting here? Either <c>x -&gt;</c> or <c>(a, b) -&gt;</c>. The parenthesised form
    /// needs a scan to the matching close, because <c>(a)</c> is otherwise just a parenthesised value.
    /// </summary>
    private bool LambdaAhead()
    {
        if (Current.Kind == TokenKind.Ident && _tokens[_pos + 1].Kind == TokenKind.Arrow) return true;
        if (Current.Kind != TokenKind.LParen) return false;

        int depth = 0;
        for (int i = _pos; i < _tokens.Count; i++)
        {
            if (_tokens[i].Kind == TokenKind.LParen) depth++;
            else if (_tokens[i].Kind == TokenKind.RParen && --depth == 0)
                return _tokens[i + 1].Kind == TokenKind.Arrow;
        }
        return false;
    }

    private Expr ParseLambda()
    {
        List<string> parameters = [];

        if (Match(TokenKind.LParen))
        {
            if (!Match(TokenKind.RParen))
            {
                do { parameters.Add(Expect(TokenKind.Ident, "a lambda parameter").Textual); }
                while (Match(TokenKind.Comma));
                Expect(TokenKind.RParen, "')'");
            }
        }
        else parameters.Add(Expect(TokenKind.Ident, "a lambda parameter").Textual);

        Expect(TokenKind.Arrow, "'->'");
        return new Expr.Lambda(parameters, ParseTop());
    }

    /// <summary>
    /// Pipeline, left-associative, sitting between the bitwise tier and the relational tier.
    ///
    /// <para>
    /// The specification's precedence table lists <c>|&gt;</c> last and calls it loosest, but its own
    /// worked example — <c>capture.fc |&gt; band(0x80) != 0</c> meaning <c>(fc &amp; 0x80) != 0</c> —
    /// requires it to bind <i>tighter</i> than <c>!=</c>. Read literally as loosest, that example is a
    /// syntax error, because the right operand of a pipeline must be a call and <c>band(0x80) != 0</c> is
    /// not one. The example is the intent, so the pipeline binds looser than arithmetic and bitwise
    /// (<c>a + b |&gt; f()</c> pipes the sum) but tighter than any comparison.
    /// </para>
    /// </summary>
    private Expr ParsePipeline()
    {
        var left = ParseBitOr();

        while (Match(TokenKind.PipeGt))
        {
            // The right operand must name a converter; `a |> b` with a bare path is the old bitwise-or
            // confusion and is rejected rather than silently reinterpreted.
            var name = Expect(TokenKind.Ident, "a converter name after '|>'");
            List<Expr> args = [];

            if (Match(TokenKind.LParen) && !Match(TokenKind.RParen))
            {
                do { args.Add(ParseTop()); } while (Match(TokenKind.Comma));
                Expect(TokenKind.RParen, "')'");
            }

            left = new Expr.Pipeline(left, new Expr.Call(name.Textual, args));
        }
        return left;
    }

    // 14 — ?: right-associative
    private Expr ParseConditional()
    {
        var cond = ParseCoalesce();
        if (!Match(TokenKind.Question)) return cond;

        var whenTrue = ParseConditional();
        Expect(TokenKind.Colon, "':'");
        var whenFalse = ParseConditional();
        return new Expr.Conditional(cond, whenTrue, whenFalse);
    }

    // 13 — ?? right-associative
    private Expr ParseCoalesce()
    {
        var left = ParseOrOr();
        if (!Match(TokenKind.Coalesce)) return left;
        return new Expr.Binary("??", left, ParseCoalesce());
    }

    // 12..3 — the left-associative binary tiers, innermost last
    private Expr ParseOrOr() => ParseLeft(ParseAndAnd, ("||", TokenKind.OrOr));
    private Expr ParseAndAnd() => ParseLeft(ParseEquality, ("&&", TokenKind.AndAnd));
    private Expr ParseEquality() => ParseLeft(ParseRelational, ("==", TokenKind.EqEq), ("!=", TokenKind.NotEq));
    private Expr ParseRelational() => ParseLeft(ParsePipeline,
        ("<", TokenKind.Lt), ("<=", TokenKind.Le), (">", TokenKind.Gt), (">=", TokenKind.Ge));
    // The word forms are matched here, not in the lexer, because `band` must ALSO be usable as a pipeline
    // converter (`fc |> band(0x80)`) — rewriting it to an operator token would make that a syntax error.
    private Expr ParseBitOr() => ParseLeft(ParseBitXor, ("|", TokenKind.Pipe), ("bor", TokenKind.Ident));
    private Expr ParseBitXor() => ParseLeft(ParseBitAnd, ("^", TokenKind.Caret), ("bxor", TokenKind.Ident));
    private Expr ParseBitAnd() => ParseLeft(ParseShift, ("&", TokenKind.Amp), ("band", TokenKind.Ident));
    private Expr ParseShift() => ParseLeft(ParseAdditive, ("<<", TokenKind.Shl), (">>", TokenKind.Shr));
    private Expr ParseAdditive() => ParseLeft(ParseMultiplicative, ("+", TokenKind.Plus), ("-", TokenKind.Minus));
    private Expr ParseMultiplicative() => ParseLeft(ParseUnary,
        ("*", TokenKind.Star), ("/", TokenKind.Slash), ("%", TokenKind.Percent));

    private Expr ParseLeft(Func<Expr> next, params (string Op, TokenKind Kind)[] ops)
    {
        var left = next();
        while (true)
        {
            // An Ident-kind entry is a word operator (`band`), which must match on its text too — otherwise
            // every identifier at this tier would be swallowed as an operator.
            var op = ops.FirstOrDefault(o => o.Kind == Current.Kind
                                             && (o.Kind != TokenKind.Ident
                                                 || string.Equals(o.Op, Current.Textual, StringComparison.Ordinal)));
            if (op.Op is null) return left;

            Advance();
            left = new Expr.Binary(WordOperators.GetValueOrDefault(op.Op, op.Op), left, next());
        }
    }

    /// <summary>Word operator to its symbolic equivalent, so the evaluator sees one spelling.</summary>
    private static readonly Dictionary<string, string> WordOperators = new(StringComparer.Ordinal)
    {
        ["band"] = "&", ["bxor"] = "^", ["bor"] = "|",
    };

    // 2 — unary, right-associative
    private Expr ParseUnary()
    {
        if (Match(TokenKind.Bang)) return new Expr.Unary("!", ParseUnary());
        if (Match(TokenKind.Tilde)) return new Expr.Unary("~", ParseUnary());
        if (Match(TokenKind.Minus)) return new Expr.Unary("-", ParseUnary());
        return ParsePostfix();
    }

    // 1 — postfix: member access, indexing, calls
    private Expr ParsePostfix()
    {
        var e = ParsePrimary();

        while (true)
        {
            if (Match(TokenKind.Dot))
            {
                var name = Expect(TokenKind.Ident, "a member name after '.'");
                e = new Expr.Member(e, name.Textual);
            }
            else if (Match(TokenKind.LBracket))
            {
                var index = ParseTop();
                Expect(TokenKind.RBracket, "']'");
                e = new Expr.Index(e, index);
            }
            else return e;
        }
    }

    private Expr ParsePrimary()
    {
        var t = Current;

        if (t.Kind is TokenKind.Int or TokenKind.Number or TokenKind.Text)
        {
            Advance();
            return new Expr.Literal(t.Literal!);
        }

        if (t.Kind == TokenKind.LParen)
        {
            Advance();
            var inner = ParseTop();
            Expect(TokenKind.RParen, "')'");
            return inner;
        }

        // List literal — the only way to build a list other than from an input or `range`.
        if (t.Kind == TokenKind.LBracket)
        {
            Advance();
            List<Expr> items = [];
            if (!Match(TokenKind.RBracket))
            {
                do { items.Add(ParseTop()); } while (Match(TokenKind.Comma));
                Expect(TokenKind.RBracket, "']'");
            }
            return new Expr.Call("list", items);
        }

        if (t.Kind == TokenKind.Ident)
        {
            Advance();
            if (t.Literal is not null) return new Expr.Literal(t.Literal);   // true / false / null

            if (Current.Kind == TokenKind.LParen)
            {
                Advance();
                List<Expr> args = [];
                if (!Match(TokenKind.RParen))
                {
                    do { args.Add(ParseTop()); } while (Match(TokenKind.Comma));
                    Expect(TokenKind.RParen, "')'");
                }
                return new Expr.Call(t.Textual, args);
            }

            return new Expr.Root(t.Textual);
        }

        throw new ProtoSyntaxException($"expected a value, found '{t.Textual}'", t.Position);
    }
}
