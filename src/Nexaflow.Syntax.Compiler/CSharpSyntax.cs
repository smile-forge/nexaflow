using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Nexaflow.Syntax.Compiler;

/// <summary>
/// Whether text is valid C#, asked of the compiler's own parser.
/// <para>
/// This is the cheap half of what this project does: no project, no references, no design-time build — just
/// Roslyn's parse, which is the definition of the language rather than an approximation of it. It exists
/// because an edit is refused when the result will not parse, and a grammar that lags C# refuses code that
/// compiles. <see cref="CompileHost.Check"/> still answers the expensive question, which a parse cannot:
/// whether the result builds.
/// </para>
/// </summary>
public static class CSharpSyntax
{
    // Preview, not a pinned version: the whole point is that this does not lag the language. Doc comments are
    // not parsed because their contents are a warning at most, and never this question's business.
    private static readonly CSharpParseOptions Options =
        new(LanguageVersion.Preview, DocumentationMode.None);

    /// <summary>
    /// Where C# first stops parsing, as a 1-based line and column, or null when the whole of it parses.
    /// Syntax only — an unknown type or a call with the wrong arguments parses perfectly well.
    /// </summary>
    public static (int Line, int Column)? FirstError(string text)
    {
        var first = CSharpSyntaxTree.ParseText(text ?? "", Options).GetDiagnostics()
                                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                                    .OrderBy(d => d.Location.SourceSpan.Start)
                                    .FirstOrDefault();
        if (first is null) return null;

        var at = first.Location.GetLineSpan().StartLinePosition;
        return (at.Line + 1, at.Character + 1);
    }
}
