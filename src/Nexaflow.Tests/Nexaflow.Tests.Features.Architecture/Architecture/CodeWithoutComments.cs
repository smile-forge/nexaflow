using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// A C# file as the source-scanning rules read it: its code with the comments taken out, so a name a comment mentions is not
/// taken for one the code uses. Each comment becomes the line breaks it spanned, so a line number found after it still counts
/// true; a literal stays, read past whole so a <c>//</c> or <c>/*</c> inside one is not taken for a comment.
/// </summary>
internal static class CodeWithoutComments
{
    /// <summary>A comment, or a literal read past whole.</summary>
    private static readonly Regex CommentsAndLiterals = new(
        @"@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+'|/\*.*?\*/|//[^\n]*",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>The code of <paramref name="file"/>, its comments taken out.</summary>
    public static string Read(string file) => CommentsAndLiterals.Replace(File.ReadAllText(file), WithoutComment);

    /// <summary>The line of <paramref name="code"/> that <paramref name="index"/> falls on, counting from one.</summary>
    public static int LineOf(string code, int index) => 1 + code.AsSpan(0, index).Count('\n');

    /// <summary>A file the build wrote rather than one somebody did.</summary>
    public static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    private static string WithoutComment(Match match) =>
        match.Value.StartsWith('/') ? new string('\n', match.Value.Count(c => c == '\n')) : match.Value;
}
