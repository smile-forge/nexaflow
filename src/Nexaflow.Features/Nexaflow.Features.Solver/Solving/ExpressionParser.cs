using System.Text;
using System.Text.RegularExpressions;
using AngouriMath;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Features.Solver.Solving;

/// <summary>How angles in the definition should be read.</summary>
public enum AngleUnit
{
    /// <summary>The engine's native unit.</summary>
    Radians,

    /// <summary>What most people mean when they type <c>sin(45)</c>.</summary>
    Degrees,
}

/// <summary>A definition that parsed, and what was parsed to get there.</summary>
/// <param name="Entity">The parsed expression, as the engine should compute it.</param>
/// <param name="Source">The infix text handed to the engine, after LaTeX and angle rewriting.</param>
/// <param name="Variables">Free variables, in the order the engine reports them.</param>
/// <param name="Display">
/// What to show the user as "the input". Differs from <paramref name="Entity"/> only in degrees
/// mode, where the computed form carries the <c>× π/180</c> conversions — showing those back would
/// answer a question nobody asked, since the user typed <c>sin(45)</c>.
/// </param>
public sealed record ParsedExpression(
    Entity Entity,
    string Source,
    IReadOnlyList<Entity.Variable> Variables,
    Entity? Display = null)
{
    /// <summary>The expression as the user wrote it.</summary>
    public Entity ForDisplay => Display ?? Entity;

    /// <summary>A pure number: every solver that needs a value rather than a formula gates on this.</summary>
    public bool IsConstant => Variables.Count == 0;
}

/// <summary>
/// The one place this feature talks to the algebra engine. Everything else works in terms of
/// <see cref="ParsedExpression"/> and markdown.
/// <para>
/// Keeping the seam single is what makes the engine replaceable and the solvers testable, but the
/// more immediate reason is failure handling: the parser throws a parse exception whose message is
/// the entire grammar — a hundred-odd token names — which must never reach a result cell. It is
/// caught here, once.
/// </para>
/// </summary>
public static class ExpressionParser
{
    /// <summary>Math spans in markdown prose: <c>$$…$$</c> preferred, then <c>$…$</c>.</summary>
    private static readonly Regex BlockMath = new(@"\$\$(.+?)\$\$", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex InlineMath = new(@"(?<!\$)\$(?!\$)(.+?)(?<!\$)\$(?!\$)", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Parses the definition, or returns false when it is not an expression this engine can read.
    /// Never throws.
    /// </summary>
    public static bool TryParse(SolverInput input, AngleUnit angleUnit, out ParsedExpression parsed)
    {
        parsed = null!;
        if (input.IsEmpty) return false;
        if (IsStillBeingWritten(input)) return false;

        var written = ToInfix(input);
        if (string.IsNullOrWhiteSpace(written)) return false;

        var infix = angleUnit == AngleUnit.Degrees ? TrigDegreeRewriter.ToRadians(written) : written;

        try
        {
            Entity entity = infix;
            var vars = entity.Vars.ToArray();

            // Only pay for the second parse when the rewrite actually changed something.
            Entity? display = null;
            if (!ReferenceEquals(infix, written) && infix != written)
            {
                try { display = written; }
                catch (Exception) { display = null; }
            }

            parsed = new ParsedExpression(entity, infix, vars, display);
            return true;
        }
        catch (Exception)
        {
            // Every failure here is "the user is still typing" or "that isn't maths". Both are
            // ordinary, and neither is worth a message — the chips simply don't appear.
            return false;
        }
    }

    /// <summary>
    /// Whether the definition is still being written, and so has nothing to solve yet.
    /// <para>
    /// Asked of the same parser that typesets it, rather than answered here. The definition is
    /// re-read on every keystroke, so nearly everything the engine would be handed is half-written —
    /// <c>\sqrt{x^2+1</c> on the way to <c>\sqrt{x^2+1}</c>, and again on the way back when a
    /// character is deleted — and an unfinished expression cannot be evaluated, simplified or solved.
    /// </para>
    /// <para>
    /// Deliberately not a check of our own. Brackets and dangling operators would be a second, poorer
    /// grammar sitting beside the real one, disagreeing with it the moment either learned anything —
    /// and a formula the reader can see is fine, quietly losing its chips, is a far worse bug than the
    /// parse it saved. What is on screen and what the solvers were told now come from one reading.
    /// </para>
    /// </summary>
    public static bool IsStillBeingWritten(SolverInput input) =>
        input.Mode == DefinitionMode.Latex && !LatexSyntax.IsWellFormed(input.Trimmed);

    /// <summary>The definition as infix text, whichever editor it came from.</summary>
    public static string ToInfix(SolverInput input) => input.Mode switch
    {
        DefinitionMode.Calc => input.Trimmed,
        DefinitionMode.Latex => LatexNormalizer.ToInfix(input.Trimmed),
        DefinitionMode.Text => FromProse(input.Trimmed),
        _ => input.Trimmed,
    };

    /// <summary>
    /// Words of two or more letters that a formula may still contain — a couple of Greek names or
    /// spelled-out constants is normal, a sentence is not.
    /// </summary>
    private const int MaxWordsBeforeItIsProse = 2;

    /// <summary>
    /// Markdown prose may still hold a formula. A <c>$$…$$</c> or <c>$…$</c> span is taken as the
    /// expression; otherwise the whole thing is tried, so typing <c>4x + 3x</c> in the Text tab
    /// still offers the algebra chips.
    /// </summary>
    private static string FromProse(string text)
    {
        var block = BlockMath.Match(text);
        if (block.Success) return LatexNormalizer.ToInfix(block.Groups[1].Value);

        var inline = InlineMath.Match(text);
        if (inline.Success) return LatexNormalizer.ToInfix(inline.Groups[1].Value);

        if (text.AsSpan().ContainsAny('\n', '\r')) return string.Empty;
        return LooksLikeProse(text) ? string.Empty : text;
    }

    /// <summary>
    /// Whether a line reads as English rather than as maths.
    /// <para>
    /// This matters more than it looks. Without it the engine happily parses "what is the area of a
    /// circle" as seven variables multiplied together and the strip fills with <c>d/dwhat</c> and
    /// <c>∫ dthe</c> — offers that are absurd on their face and, worse, crowd out the AI chips that
    /// are the right answer for that sentence.
    /// </para>
    /// <para>
    /// Single letters do not count, so <c>x + y + z</c> is still maths; multi-letter names are
    /// allowed up to <see cref="MaxWordsBeforeItIsProse"/>, so <c>alpha + beta</c> survives.
    /// </para>
    /// </summary>
    private static bool LooksLikeProse(string text)
    {
        var words = 0;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var isWord = token.Length > 1;
            foreach (var c in token)
            {
                if (char.IsLetter(c)) continue;
                isWord = false;
                break;
            }

            if (isWord && ++words > MaxWordsBeforeItIsProse) return true;
        }

        return false;
    }

    /// <summary>
    /// The expression as LaTeX, ready to drop between <c>$$</c> fences. Falls back to the engine's
    /// plain form if it cannot be typeset.
    /// </summary>
    public static string Latex(Entity entity)
    {
        try { return entity.Latexize(); }
        catch (Exception) { return Plain(entity); }
    }

    /// <summary>The expression as flat text. Never throws.</summary>
    public static string Plain(Entity entity)
    {
        try { return entity.Stringize(); }
        catch (Exception) { return entity.ToString() ?? string.Empty; }
    }

    /// <summary>
    /// A <c>$$…$$</c> block showing <paramref name="from"/> becoming <paramref name="to"/>.
    /// </summary>
    public static string EquationBlock(Entity from, Entity to)
        => $"$$\n{Latex(from)} = {Latex(to)}\n$$";

    /// <summary>A <c>$$…$$</c> block of one expression.</summary>
    public static string Block(Entity entity) => $"$$\n{Latex(entity)}\n$$";

    /// <summary>
    /// Warms the engine up. The first parse loads a generated grammar, which is slow enough to be
    /// felt as lag on the first keystroke in a fresh tab; doing it off the UI thread when the tab
    /// opens means nobody ever sees it.
    /// </summary>
    public static void Warmup()
    {
        try
        {
            Entity seed = "1 + x";
            _ = seed.Vars.Count();
            _ = seed.Latexize();
        }
        catch (Exception)
        {
            // A warm-up that fails costs nothing — the real parse will report for itself.
        }
    }

    /// <summary>
    /// The numeric value rounded to <paramref name="decimals"/> places, or null when it is not a
    /// finite number.
    /// <para>
    /// Always use this rather than typesetting the evaluated entity: the engine evaluates to about
    /// a hundred significant digits, so <c>sin(π/4)</c> renders as a paragraph of digits where the
    /// answer wanted is <c>0.707107</c>.
    /// </para>
    /// </summary>
    public static string? DecimalLatex(Entity numeric, int decimals)
    {
        try
        {
            if (numeric is not Entity.Number.Complex c) return null;
            if (!c.IsFinite) return null;

            var real = (double)c.RealPart;
            if (c.ImaginaryPart.IsZero)
                return Trim(real.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture));

            var imag = (double)c.ImaginaryPart;
            var sign = imag < 0 ? "-" : "+";
            var i = Trim(Math.Abs(imag).ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture));
            return $"{Trim(real.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture))} {sign} {i}i";
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Drops the trailing zeros a fixed-point format leaves behind.</summary>
    private static string Trim(string s)
    {
        if (!s.Contains('.')) return s;
        s = s.TrimEnd('0').TrimEnd('.');
        return s.Length == 0 || s == "-" ? "0" : s;
    }

    /// <summary>
    /// Appends the domain condition the engine sometimes attaches to a result (<c>provided not
    /// cos(x) = 0</c>), as a note under the maths rather than inside it.
    /// </summary>
    public static string WithProviso(string markdown, Entity result)
    {
        var plain = Plain(result);
        var at = plain.IndexOf(" provided ", StringComparison.Ordinal);
        if (at < 0) return markdown;

        var sb = new StringBuilder(markdown);
        sb.Append("\n\n*").Append(char.ToUpperInvariant(plain[at + 1])).Append(plain.AsSpan(at + 2)).Append('*');
        return sb.ToString();
    }
}
