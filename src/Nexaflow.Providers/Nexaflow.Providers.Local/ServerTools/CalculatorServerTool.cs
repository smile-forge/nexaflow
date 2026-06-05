using System.Globalization;

namespace Nexaflow.Providers.Local.ServerTools;

/// <summary>
/// Built-in server-side calculator so the model can do exact arithmetic instead of guessing.
/// Evaluates an expression with <c>+ - * / %</c>, <c>^</c> (power), parentheses and unary +/-.
/// </summary>
public sealed class CalculatorServerTool : IServerTool
{
    public string Name        => "calculator";
    public string Description => "Evaluate an arithmetic expression exactly. Supports + - * / % ^ and parentheses. Always use this for arithmetic rather than computing in your head.";

    public IReadOnlyList<ServerToolParam> Parameters { get; } =
    [
        new("expression", "string", "The arithmetic expression to evaluate, e.g. \"18432 * 977 + 5\".")
    ];

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        var expr = arguments.TryGetValue("expression", out var v) ? v?.ToString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(expr))
            return Task.FromResult("Error: no 'expression' provided.");

        try
        {
            double result = new Evaluator(expr).Evaluate();
            return Task.FromResult($"{expr.Trim()} = {Format(result)}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error evaluating \"{expr.Trim()}\": {ex.Message}");
        }
    }

    private static string Format(double d)
        => d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 9.2e18
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("G15", CultureInfo.InvariantCulture);

    /// <summary>Tiny recursive-descent arithmetic evaluator (invariant culture).</summary>
    private sealed class Evaluator(string text)
    {
        private readonly string _s = text;
        private int _pos;

        public double Evaluate()
        {
            double v = ParseExpr();
            SkipWs();
            if (_pos != _s.Length) throw new FormatException($"unexpected '{_s[_pos]}'");
            return v;
        }

        // expr = term (('+'|'-') term)*
        private double ParseExpr()
        {
            double v = ParseTerm();
            while (true)
            {
                if      (Match('+')) v += ParseTerm();
                else if (Match('-')) v -= ParseTerm();
                else return v;
            }
        }

        // term = factor (('*'|'/'|'%') factor)*
        private double ParseTerm()
        {
            double v = ParseFactor();
            while (true)
            {
                if      (Match('*')) v *= ParseFactor();
                else if (Match('/')) v /= ParseFactor();
                else if (Match('%')) v %= ParseFactor();
                else return v;
            }
        }

        // factor = unary ('^' factor)?   (right-associative)
        private double ParseFactor()
        {
            double b = ParseUnary();
            if (Match('^')) return Math.Pow(b, ParseFactor());
            return b;
        }

        private double ParseUnary()
        {
            if (Match('-')) return -ParseUnary();
            if (Match('+')) return  ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (Match('('))
            {
                double v = ParseExpr();
                if (!Match(')')) throw new FormatException("missing ')'");
                return v;
            }

            SkipWs();
            int start = _pos;
            while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
            if (_pos == start) throw new FormatException("number expected");
            return double.Parse(_s[start.._pos], CultureInfo.InvariantCulture);
        }

        private void SkipWs() { while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++; }

        private bool Match(char c)
        {
            SkipWs();
            if (_pos < _s.Length && _s[_pos] == c) { _pos++; return true; }
            return false;
        }
    }
}
