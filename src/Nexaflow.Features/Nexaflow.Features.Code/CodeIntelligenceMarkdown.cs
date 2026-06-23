using System;
using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.Syntax;

namespace Nexaflow.Features.Code;

/// <summary>
/// Builds the "As Code" side-panel markdown from a <see cref="CodeOutline"/>:
///   • a <b>Dependencies</b> list — imports that resolve to a local file become <c>file:</c> links (clicking
///     opens that file As Code), others stay plain text;
///   • a <b>Structure</b> mermaid <c>classDiagram</c> — one box per type. Each member row carries a hidden
///     <c>… @@&lt;url&gt;</c> token so the renderer makes it a clickable link to the member's definition;
///   • a <b>Members</b> list for any top-level (free) functions.
///
/// Member/link URLs are <c>file:///&lt;path&gt;#ast=&lt;url-encoded ast-path&gt;</c>. The host intercepts them:
/// same-file + ast ⇒ scroll to the resolved line; other file ⇒ open it As Code. The AST-path fragment (not a
/// line number) keeps a link valid across edits — see <see cref="CodeStructureExtractor"/>.
/// </summary>
public static class CodeIntelligenceMarkdown
{
    public static string Build(string filePath, CodeOutline outline)
    {
        var fileUri = SafeFileUri(filePath);
        var sb = new StringBuilder();

        if (outline.Imports.Count > 0)
        {
            sb.AppendLine("## Dependencies").AppendLine();
            foreach (var imp in outline.Imports)
            {
                if (imp.ResolvedPath is { } rp)
                    sb.AppendLine($"- [{LinkLabel(imp.Text)}]({SafeFileUri(rp)})");
                else
                    sb.AppendLine($"- {imp.Text}");
            }
            sb.AppendLine();
        }

        if (outline.Types.Count > 0 && fileUri is not null)
        {
            sb.AppendLine("## Structure").AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine("classDiagram");
            foreach (var t in outline.Types)
            {
                var cls = SafeId(t.Name);
                if (t.Members.Count == 0)
                {
                    sb.AppendLine($"  class {cls}");
                    continue;
                }
                sb.AppendLine($"  class {cls} {{");
                foreach (var m in t.Members)
                    sb.AppendLine($"    {MemberLabel(m)} @@{fileUri}#ast={Uri.EscapeDataString(m.AstPath)}");
                sb.AppendLine("  }");
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (outline.TopLevel.Count > 0 && fileUri is not null)
        {
            sb.AppendLine("## Members").AppendLine();
            foreach (var m in outline.TopLevel)
                sb.AppendLine($"- [{LinkLabel(MemberLabel(m))}]({fileUri}#ast={Uri.EscapeDataString(m.AstPath)})");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>The diagram/list label for a member: a leading <c>+</c> then the name, with <c>()</c> for
    /// callables (Mermaid needs the parens to file it under methods).</summary>
    private static string MemberLabel(OutlineMember m) => "+" + m.Signature;

    /// <summary>A Mermaid-safe class id (identifier characters only).</summary>
    private static string SafeId(string name) => Regex.Replace(name, "[^A-Za-z0-9_]", "_");

    /// <summary>Escapes a markdown link label so brackets in import text can't break the link.</summary>
    private static string LinkLabel(string s) => s.Replace("[", "(").Replace("]", ")");

    private static string? SafeFileUri(string path)
    {
        try { return new Uri(path).AbsoluteUri; }
        catch { return null; }
    }
}
