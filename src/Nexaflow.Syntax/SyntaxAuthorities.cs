namespace Nexaflow.Syntax;

/// <summary>
/// Where the first syntax error in <paramref name="text"/> is, as a 1-based line and column, or null when the
/// text is valid.
/// </summary>
public delegate (int Line, int Column)? SyntaxVerdict(string text);

/// <summary>
/// The real language front-ends, by grammar id. Where one is registered it is the authority on whether text is
/// valid, and the grammar is not asked.
/// <para>
/// A tree-sitter grammar is an approximation of a language, maintained elsewhere, and it lags. C# 9 gave
/// <c>with</c> a meaning in expressions and the grammar has read it as a keyword ever since, so
/// <c>var with = at;</c> — legal, because <c>with</c> is contextual — comes back a parse error. An edit refused
/// on that is a caller told to rewrite working code, and the next lag will be a different word. Where Nexaflow
/// already carries a compiler for the language, the compiler answers and the question stops depending on how
/// current the grammar is.
/// </para>
/// <para>
/// Nothing registers itself. <c>nfi</c> wires Roslyn in because it is the process that edits code; the app's
/// editor surfaces keep to the parse, so their behaviour is the grammar's as before. Registration is
/// per-process and lasts its life.
/// </para>
/// </summary>
public static class SyntaxAuthorities
{
    private static readonly Dictionary<string, SyntaxVerdict> ByGrammar = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Puts <paramref name="verdict"/> in charge of <paramref name="grammarId"/>, in place of any before it.</summary>
    public static void Register(string grammarId, SyntaxVerdict verdict)
    {
        if (string.IsNullOrEmpty(grammarId)) return;
        lock (ByGrammar) ByGrammar[grammarId] = verdict;
    }

    /// <summary>The front-end that answers for this grammar, or null when only the grammar can.</summary>
    public static SyntaxVerdict? For(string? grammarId)
    {
        if (string.IsNullOrEmpty(grammarId)) return null;
        lock (ByGrammar) return ByGrammar.GetValueOrDefault(grammarId);
    }
}
