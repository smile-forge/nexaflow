using System;
using System.Collections.Generic;

using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// Which grammar reads a fence calling itself something.
///
/// <para>
/// Most of the answer already exists: a fence word is usually the file extension, and
/// <see cref="TreeSitterLanguages"/> already says which grammar reads a file. So that is asked first and
/// what is here is only the names a writer uses that no file is called — <c>csharp</c>, <c>c++</c>,
/// <c>javascript</c> — plus the two the engine spells differently from everybody else.
/// </para>
/// </summary>
internal static class CodeGrammars
{
    private static readonly Dictionary<string, string> Called = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = "c-sharp",
        ["c#"] = "c-sharp",
        ["cs"] = "c-sharp",
        ["dotnet"] = "c-sharp",
        ["c++"] = "cpp",
        ["cplusplus"] = "cpp",
        ["javascript"] = "javascript",
        ["js"] = "javascript",
        ["jsx"] = "javascript",
        ["node"] = "javascript",
        ["typescript"] = "typescript",
        ["ts"] = "typescript",
        ["python"] = "python",
        ["py"] = "python",
        ["ipython"] = "python",
        ["ruby"] = "ruby",
        ["rb"] = "ruby",
        ["rust"] = "rust",
        ["rs"] = "rust",
        ["java"] = "java",
        ["json"] = "json",
        ["html"] = "html",
        ["htm"] = "html",
        ["css"] = "css",
        ["php"] = "php",
        ["xml"] = "xml",
        ["xaml"] = "xaml",
        ["razor"] = "razor",
        ["jinja"] = "jinja",
        ["c"] = "c",
        ["cpp"] = "cpp",
    };

    /// <summary>What reads <paramref name="language"/>, or null where nothing does.</summary>
    public static string? For(string? language)
    {
        var word = language?.Trim();
        if (string.IsNullOrEmpty(word)) return null;

        return Called.TryGetValue(word, out var named) ? named : TreeSitterLanguages.ForFile("code." + word);
    }
}
