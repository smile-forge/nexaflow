using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// A field of a diagram's front matter given a value — what a ribbon control setting how the whole diagram is drawn writes, in
/// every diagram whose settings are read from its front matter. A helper each diagram's own handler is made of, and nobody's
/// handler.
/// </summary>
internal static class FrontMatterWriting
{
    /// <summary>The front matter's field at <paramref name="path"/> given <paramref name="value"/>, the caret kept where it stood.</summary>
    public static ContentChange Configuring(ContentEdit edit, string[] path, string value)
    {
        var write = Configured(edit, path, value);
        var caret = edit.State.Caret;

        return new ContentChange([write], write.End <= caret ? caret + write.Text.Length - write.Length : caret, null);
    }

    /// <summary>
    /// The front matter's field at <paramref name="path"/> given <paramref name="value"/>: its value, where it is written; else the
    /// deepest field of the path that is, written again with the rest of the path under it; else the front matter's closing fence,
    /// with the whole path before it; else — no front matter at all — the diagram's first line, with front matter holding the path
    /// before it.
    /// </summary>
    private static ContentWrite Configured(ContentEdit edit, string[] path, string value)
    {
        var newline = edit.Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        if (edit.Root.Children.FirstOrDefault(part => part.Kind == MermaidKinds.FrontMatter) is not { } front)
        {
            var first = edit.Root.Children[0];
            return new ContentWrite(first, "---" + newline + Nest(path, value, 0, newline) + newline + "---" + newline + first.Node.Print());
        }

        var at = front;
        var depth = 0;

        while (depth < path.Length && Under(at, path[depth]) is { } line)
        {
            at = line;
            depth++;
        }

        if (depth == 0)
        {
            var fence = front.Children[^1].Children.First(part => part.Kind == MermaidKinds.Fence);
            return new ContentWrite(fence, Nest(path, value, 0, newline) + newline + fence.Node.Print());
        }

        var field = Field(at)!;

        if (depth == path.Length)
            return field.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Value) is { } written
                ? new ContentWrite(written, value)
                : new ContentWrite(field, path[^1] + ": " + value);

        return new ContentWrite(field, field.Node.Print() + newline + Nest(path[depth..], value, Indent(at).Length + 2, newline));
    }

    /// <summary>The line directly under <paramref name="within"/> — the front matter, or a line of it — whose field is <paramref name="key"/>.</summary>
    private static ContentPart? Under(ContentPart within, string key) =>
        within.Children.FirstOrDefault(line => line.Kind == MermaidKinds.Line
            && Field(line)?.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Key) is { } named
            && string.Equals(named.Text, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The field a line of front matter says, or null for one that says none.</summary>
    private static ContentPart? Field(ContentPart line) => line.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Field);

    /// <summary>The space a line of front matter starts with.</summary>
    private static string Indent(ContentPart line) =>
        line.Children.FirstOrDefault() is { Kind: Kinds.Space } space && space.Start == line.Start ? space.Text : string.Empty;

    /// <summary><paramref name="keys"/> as lines of fields each under the one before, from <paramref name="indent"/>, the last saying <paramref name="value"/>.</summary>
    private static string Nest(IReadOnlyList<string> keys, string value, int indent, string newline) =>
        string.Join(newline, keys.Select((key, depth) =>
            new string(' ', indent + (2 * depth)) + key + (depth == keys.Count - 1 ? ": " + value : ":")));
}
