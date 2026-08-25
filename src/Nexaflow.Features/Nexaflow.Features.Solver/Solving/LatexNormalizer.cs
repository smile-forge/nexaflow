using System.Text;

namespace Nexaflow.Features.Solver.Solving;

/// <summary>
/// Rewrites the common LaTeX a person actually types into the infix syntax the algebra engine
/// parses — <c>\frac{x^2}{2}</c> into <c>((x^(2))/(2))</c>, and so on.
/// <para>
/// Deliberately a subset, not a LaTeX implementation. It covers fractions, roots, the standard
/// function names, Greek letters, grouping and the multiplication/division commands, which is what
/// the definition area is used for. Anything it does not recognise is passed through as an
/// identifier rather than rejected, so an unknown command degrades into "the engine could not read
/// that" — a normal, visible outcome — instead of a parse crash.
/// </para>
/// </summary>
public static class LatexNormalizer
{
    /// <summary>Commands that are simply a name the engine already knows.</summary>
    private static readonly HashSet<string> PassThrough =
    [
        "sin", "cos", "tan", "cot", "cotan", "sec", "csc", "cosec",
        "sinh", "cosh", "tanh", "coth", "sech", "csch",
        "arcsin", "arccos", "arctan", "arccot", "arcsec", "arccsc",
        "ln", "log", "exp", "min", "max", "gcd", "lcm", "det",
        "pi", "alpha", "beta", "gamma", "delta", "epsilon", "varepsilon", "zeta", "eta",
        "theta", "vartheta", "iota", "kappa", "lambda", "mu", "nu", "xi", "rho", "varrho",
        "sigma", "varsigma", "tau", "upsilon", "phi", "varphi", "chi", "psi", "omega",
        "Gamma", "Delta", "Theta", "Lambda", "Xi", "Pi", "Sigma", "Upsilon", "Phi", "Psi", "Omega",
        "infty",
    ];

    /// <summary>Commands that stand for an operator.</summary>
    private static readonly Dictionary<string, string> Operators = new()
    {
        ["cdot"] = "*", ["times"] = "*", ["ast"] = "*",
        ["div"] = "/", ["over"] = "/",
        ["pm"] = "+", ["mp"] = "-",
        ["le"] = "<=", ["leq"] = "<=", ["ge"] = ">=", ["geq"] = ">=", ["neq"] = "!=", ["ne"] = "!=",
        ["equiv"] = "=",
    };

    /// <summary>
    /// Delimiter modifiers, which must vanish without leaving a gap: the engine's lexer reads a
    /// function name and its opening bracket as one token, so <c>\ln\left(x\right)</c> becoming
    /// <c>ln (x)</c> rather than <c>ln(x)</c> is the difference between parsing and not.
    /// </summary>
    private static readonly HashSet<string> Invisible = ["left", "right", "displaystyle", "textstyle", "limits", "nolimits"];

    /// <summary>Commands that exist only for spacing and are safe to render as one.</summary>
    private static readonly HashSet<string> Ignored = ["quad", "qquad", "!", ",", ";", ":", " "];

    /// <summary>The LaTeX source rewritten as infix. Never throws.</summary>
    public static string ToInfix(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return string.Empty;
        var sb = new StringBuilder(latex.Length + 16);
        var i = 0;
        Convert(latex, ref i, sb, stopAtBrace: false);
        return sb.ToString().Trim();
    }

    private static void Convert(string src, ref int i, StringBuilder sb, bool stopAtBrace)
    {
        while (i < src.Length)
        {
            var c = src[i];

            if (c == '}' && stopAtBrace) return;

            switch (c)
            {
                case '\\':
                    ConvertCommand(src, ref i, sb);
                    break;

                case '{':
                    i++;                                    // past '{'
                    sb.Append('(');
                    Convert(src, ref i, sb, stopAtBrace: true);
                    if (i < src.Length && src[i] == '}') i++;
                    sb.Append(')');
                    break;

                case '}':                                   // unbalanced — drop it
                    i++;
                    break;

                case '^':
                    i++;
                    sb.Append("^(");
                    AppendAtom(src, ref i, sb);
                    sb.Append(')');
                    break;

                case '_':
                    // A subscript is part of the name — x_1 stays one variable, not x times 1.
                    i++;
                    sb.Append('_');
                    AppendAtom(src, ref i, sb);
                    break;

                default:
                    sb.Append(c);
                    i++;
                    break;
            }
        }
    }

    /// <summary>The next single token after <c>^</c> or <c>_</c>: a braced group, a command, or one character.</summary>
    private static void AppendAtom(string src, ref int i, StringBuilder sb)
    {
        while (i < src.Length && src[i] == ' ') i++;
        if (i >= src.Length) return;

        if (src[i] == '{')
        {
            i++;                                            // past '{'
            Convert(src, ref i, sb, stopAtBrace: true);
            if (i < src.Length && src[i] == '}') i++;
            return;
        }

        if (src[i] == '\\')
        {
            ConvertCommand(src, ref i, sb);
            return;
        }

        sb.Append(src[i]);
        i++;
    }

    private static void ConvertCommand(string src, ref int i, StringBuilder sb)
    {
        i++;                                                // past '\'
        if (i >= src.Length) return;

        // Escaped punctuation: \{ \} \\ \% and friends.
        if (!char.IsLetter(src[i]))
        {
            var p = src[i];
            i++;
            switch (p)
            {
                case '{': sb.Append('('); return;
                case '}': sb.Append(')'); return;
                case '\\': sb.Append(' '); return;          // row break — nothing to compute
                case '!': case ',': case ';': case ':': case ' ': sb.Append(' '); return;
                default: sb.Append(p); return;
            }
        }

        var start = i;
        while (i < src.Length && char.IsLetter(src[i])) i++;
        var name = src[start..i];

        switch (name)
        {
            case "frac" or "dfrac" or "tfrac":
            {
                var num = ReadGroup(src, ref i);
                var den = ReadGroup(src, ref i);
                sb.Append("((").Append(num).Append(")/(").Append(den).Append("))");
                return;
            }

            case "sqrt":
            {
                var degree = ReadOptional(src, ref i);
                var body = ReadGroup(src, ref i);
                if (degree is null) sb.Append("sqrt(").Append(body).Append(')');
                else sb.Append("((").Append(body).Append(")^(1/(").Append(degree).Append(")))");
                return;
            }

            case "text" or "mathrm" or "mathit" or "mathbf" or "operatorname":
                sb.Append(ReadGroup(src, ref i));
                return;

            case "abs" or "lvert" or "rvert" or "vert":
                sb.Append("abs");
                return;
        }

        if (Operators.TryGetValue(name, out var op)) { sb.Append(op); return; }
        if (Invisible.Contains(name)) return;
        if (Ignored.Contains(name)) { sb.Append(' '); return; }

        // Known name, or an unknown one passed through as an identifier — either way the engine
        // gets something it can either use or refuse cleanly.
        sb.Append(name);
        if (!PassThrough.Contains(name)) sb.Append(' ');
    }

    /// <summary>Reads the next <c>{…}</c> and returns it already converted. Empty when there isn't one.</summary>
    private static string ReadGroup(string src, ref int i)
    {
        while (i < src.Length && src[i] == ' ') i++;
        var sb = new StringBuilder();

        if (i < src.Length && src[i] == '{')
        {
            i++;
            Convert(src, ref i, sb, stopAtBrace: true);
            if (i < src.Length && src[i] == '}') i++;
            return sb.ToString();
        }

        // \frac12 is legal LaTeX: the next single token is the whole argument.
        AppendAtom(src, ref i, sb);
        return sb.ToString();
    }

    /// <summary>Reads a <c>[…]</c> option — the root degree in <c>\sqrt[3]{x}</c>. Null when absent.</summary>
    private static string? ReadOptional(string src, ref int i)
    {
        var save = i;
        while (i < src.Length && src[i] == ' ') i++;
        if (i >= src.Length || src[i] != '[') { i = save; return null; }

        i++;                                                // past '['
        var sb = new StringBuilder();
        var depth = 0;
        while (i < src.Length)
        {
            if (src[i] == '[') depth++;
            else if (src[i] == ']')
            {
                if (depth == 0) { i++; break; }
                depth--;
            }
            sb.Append(src[i]);
            i++;
        }

        var inner = sb.ToString();
        var j = 0;
        var converted = new StringBuilder();
        Convert(inner, ref j, converted, stopAtBrace: false);
        return converted.ToString();
    }
}
