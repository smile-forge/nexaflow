using System.Text;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.EventModeling;

/// <summary>
/// What an <c>eventmodeling</c> block's front matter and directives set, by the path of keys that sets each —
/// <c>title</c>, <c>config.eventmodeling.padding</c>, <c>config.themeVariables.emEventFill</c>.
///
/// <para>
/// The front matter is the rows its parser read, nested by how far each is indented, as YAML nests them. A directive —
/// <c>%%{init: { … }}%%</c> — is read as Mermaid reads one, an object whose keys may go bare and whose strings may be in single
/// quotes, and what its <c>init</c> sets is read as the front matter's <c>config</c>, a directive written later setting over what was
/// set before.
/// </para>
/// </summary>
internal static class EventModelingSettings
{
    /// <summary>What a key is set to, and the piece of the front matter it is written in — null for a directive's.</summary>
    internal readonly record struct Setting(string Value, ContentNode? Written);

    /// <summary>Everything <paramref name="tree"/>'s front matter and directives set, by path.</summary>
    public static IReadOnlyDictionary<string, Setting> Of(ContentNode tree)
    {
        var set = new Dictionary<string, Setting>(StringComparer.Ordinal);

        if (tree.Children.FirstOrDefault(node => node.Kind == EventModelingKinds.FrontMatter) is { } front) Front(front, set);

        foreach (var directive in tree.SelfAndDescendants().Where(node => node.Kind == EventModelingKinds.Directive))
            Directive(directive.Text, set);

        return set;
    }

    /// <summary>The front matter's rows, each key under the nearest key before it indented less.</summary>
    private static void Front(ContentNode front, Dictionary<string, Setting> set)
    {
        var open = new Stack<(int Indent, string Key)>();
        var indent = 0;
        var fresh = true;

        foreach (var piece in front.Children)
        {
            if (piece.Kind == Kinds.Space)
            {
                // Space opening a row is how far the row is indented; space after what a row says is only the row's.
                if (piece.Text.Contains('\n')) (indent, fresh) = (0, true);
                else if (fresh) (indent, fresh) = (piece.Text.Length, false);

                continue;
            }

            fresh = false;
            if (piece.Kind != EventModelingKinds.Setting || piece.Part(Roles.Name)?.Text is not { Length: > 0 } key) continue;

            while (open.Count > 0 && open.Peek().Indent >= indent) open.Pop();

            var path = string.Join('.', open.Reverse().Select(parent => parent.Key).Append(key));
            if (piece.Part(EventModelingRoles.Setting) is { } value) set[path] = new Setting(Unquoted(value.Text), value);

            open.Push((indent, key));
        }
    }

    /// <summary>What a directive's <c>init</c> sets, as the front matter's <c>config</c>.</summary>
    private static void Directive(string text, Dictionary<string, Setting> set)
    {
        if (text.Length < 6) return;

        var written = "{" + text[3..^3] + "}";
        var at = 0;
        var read = new List<(string Path, string Value)>();

        if (!Value(written, ref at, string.Empty, read)) return;

        foreach (var (path, value) in read)
        {
            var dot = path.IndexOf('.');
            if (dot > 0 && path[..dot] is "init" or "initialize") set["config." + path[(dot + 1)..]] = new Setting(value, null);
        }
    }

    /// <summary>
    /// A value of an object written as a directive writes one — an object, a list, or a scalar in either quote or bare — each scalar
    /// read under the path of keys holding it. False where it is not one.
    /// </summary>
    private static bool Value(string text, ref int at, string path, List<(string Path, string Value)> read)
    {
        Skip(text, ref at);
        if (at >= text.Length) return false;

        if (text[at] == '[')
        {
            // A list sets nothing an event model reads; it is stepped over whole.
            var depth = 0;
            for (; at < text.Length; at++)
            {
                if (text[at] is '"' or '\'') Scalar(text, ref at);
                if (at >= text.Length) return false;
                if (text[at] == '[') depth++;
                else if (text[at] == ']' && --depth == 0) { at++; return true; }
            }

            return false;
        }

        if (text[at] != '{')
        {
            if (Scalar(text, ref at) is not { } scalar) return false;
            read.Add((path, scalar));
            return true;
        }

        for (at++; ;)
        {
            Skip(text, ref at);
            if (at >= text.Length) return false;
            if (text[at] == '}') { at++; return true; }
            if (text[at] == ',') { at++; continue; }

            if (Scalar(text, ref at) is not { Length: > 0 } key) return false;

            Skip(text, ref at);
            if (at >= text.Length || text[at] != ':') return false;
            at++;

            if (!Value(text, ref at, path.Length == 0 ? key : path + "." + key, read)) return false;
        }
    }

    /// <summary>A scalar: in either quote, a backslash keeping what follows it, or bare to a comma, a colon, a bracket or space. Null where none starts here.</summary>
    private static string? Scalar(string text, ref int at)
    {
        if (at >= text.Length) return null;

        if (text[at] is '"' or '\'')
        {
            var quote = text[at];
            var said = new StringBuilder();

            for (at++; at < text.Length; at++)
            {
                if (text[at] == '\\' && at + 1 < text.Length) said.Append(text[++at]);
                else if (text[at] == quote) { at++; return said.ToString(); }
                else said.Append(text[at]);
            }

            return null;
        }

        var start = at;
        while (at < text.Length && text[at] is not (',' or ':' or '{' or '}' or '[' or ']') && !char.IsWhiteSpace(text[at])) at++;
        return at > start ? text[start..at] : null;
    }

    private static void Skip(string text, ref int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
    }

    /// <summary>A front-matter value as it reads: its quotes taken off, where it is written in a pair of them.</summary>
    private static string Unquoted(string value) =>
        value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0] ? value[1..^1] : value;
}
