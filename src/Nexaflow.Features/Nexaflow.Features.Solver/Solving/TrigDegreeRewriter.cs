using System.Text;

namespace Nexaflow.Features.Solver.Solving;

/// <summary>
/// Rewrites an expression so trigonometry reads in degrees, for a definition entered while the
/// palette is set to DEG.
/// <para>
/// The engine only knows radians, so the conversion happens in the expression rather than in the
/// engine: an angle going in is multiplied by <c>pi/180</c>, and an angle coming back out of an
/// inverse function is multiplied by <c>180/pi</c>. Both are needed — converting only the input
/// would leave <c>arcsin(0.5)</c> answering <c>0.5236</c> in a calculator claiming degrees, which is
/// the kind of wrong that looks right.
/// </para>
/// <para>
/// Hyperbolic functions are left alone on purpose: their argument is a real number, not an angle.
/// </para>
/// </summary>
public static class TrigDegreeRewriter
{
    /// <summary>Take an angle: the argument is converted going in.</summary>
    private static readonly HashSet<string> TakesAngle =
    [
        "sin", "cos", "tan", "cot", "cotan", "sec", "csc", "cosec",
    ];

    /// <summary>Return an angle: the result is converted coming out.</summary>
    private static readonly HashSet<string> ReturnsAngle =
    [
        "arcsin", "arccos", "arctan", "arccot", "arccotan", "arcsec", "arccsc", "arccosec",
        "asin", "acos", "atan", "acot", "acotan", "asec", "acsc", "acosec",
    ];

    /// <summary>
    /// <paramref name="expr"/> with every degree-taking angle converted to radians. Returns the
    /// input unchanged when it contains no trigonometry. Never throws.
    /// </summary>
    public static string ToRadians(string expr)
    {
        if (string.IsNullOrEmpty(expr)) return expr ?? string.Empty;
        var sb = new StringBuilder(expr.Length + 16);
        Rewrite(expr, sb);
        return sb.ToString();
    }

    private static void Rewrite(string src, StringBuilder sb)
    {
        var i = 0;
        while (i < src.Length)
        {
            if (!char.IsLetter(src[i]) && src[i] != '_')
            {
                sb.Append(src[i]);
                i++;
                continue;
            }

            var start = i;
            while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
            var name = src[start..i];

            var afterName = i;
            while (afterName < src.Length && src[afterName] == ' ') afterName++;

            if (afterName >= src.Length || src[afterName] != '(')
            {
                sb.Append(name);
                continue;                                   // a variable or constant, not a call
            }

            if (!TryReadCall(src, afterName, out var inner, out var afterCall))
            {
                sb.Append(name);
                continue;                                   // unbalanced — leave it for the parser to reject
            }

            var converted = new StringBuilder();
            Rewrite(inner, converted);                      // nested calls convert too

            if (TakesAngle.Contains(name))
                sb.Append(name).Append("((").Append(converted).Append(") * pi / 180)");
            else if (ReturnsAngle.Contains(name))
                sb.Append("((").Append(name).Append('(').Append(converted).Append(")) * 180 / pi)");
            else
                sb.Append(name).Append('(').Append(converted).Append(')');

            i = afterCall;
        }
    }

    /// <summary>Reads the balanced <c>(…)</c> starting at <paramref name="open"/>.</summary>
    private static bool TryReadCall(string src, int open, out string inner, out int afterCall)
    {
        inner = string.Empty;
        afterCall = open;

        var depth = 0;
        for (var i = open; i < src.Length; i++)
        {
            if (src[i] == '(') depth++;
            else if (src[i] == ')')
            {
                depth--;
                if (depth != 0) continue;
                inner = src[(open + 1)..i];
                afterCall = i + 1;
                return true;
            }
        }

        return false;
    }
}
