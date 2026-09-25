using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Editor.Highlighting;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>What a piece of code is — to the tree it is read into, and to the layout it is set in.</summary>
public static class CodeKinds
{
    /// <summary>A whole stretch of code.</summary>
    public const string Code = "code";

    /// <summary>Where one line of it ends and the next begins: a line feed, and the return before it where there is one.</summary>
    public const string LineEnd = "line-end";
}

/// <summary>
/// A stretch of code, set line for line in a monospaced face, each token in the colour its kind is given.
///
/// <para>
/// It colours by kind and nothing else: a piece called <c>keyword</c> is drawn in whatever the theme says a
/// keyword is (<see cref="SyntaxTokenMap"/>), and a piece the grammar had no name for — or every piece, when
/// nothing has read it yet — is drawn in the body colour. So the uncoloured case is not a second path, it is
/// the same path with nothing named.
/// </para>
/// <para>
/// Lines are kept and nothing wraps. Code that reflowed would be code that lied about where a statement
/// ended, and a reader counting columns is the whole reason it is set in a monospaced face.
/// </para>
/// </summary>
public sealed class CodeBuilder : ContentBuilder
{
    private readonly Dictionary<string, Brush?> _inks = [];

    internal CodeBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
        : base(reading, state, style, isReadOnly)
    {
    }

    /// <summary>
    /// Lays <paramref name="source"/> out, coloured where <paramref name="grammar"/> has already been read
    /// against it and plain where it has not.
    /// </summary>
    public static Laid Lay(string? source, string? grammar, StyleFormat style, double room, int at)
    {
        var text = source ?? string.Empty;
        var tree = ContentNode.Branch(CodeKinds.Code, [ContentNode.Leaf(Kinds.Verbatim, text, Roles.Body)]);

        var tokens = grammar is { Length: > 0 } reads && CodeSpans.For(reads, text) is { } spans ? new WithTokens(spans) : null;
        tree = new AstPipeline().Then(tokens).Then(new CodeLines()).Run(tree);

        return new CodeBuilder(ContentReading.Of(tree, at), EditState.For(text), style, isReadOnly: true).Lay(room);
    }

    /// <inheritdoc/>
    protected override Laid? Build()
    {
        if (Reading.Root.Length == 0) return null;

        var into = new LayoutBuilder();
        var (x, y, wide) = (0.0, 0.0, 0.0);

        into.Open(CodeKinds.Code, Reading.Root);

        foreach (var token in Tokens())
        {
            if (token.Kind == CodeKinds.LineEnd)
            {
                y += Height;
                x = 0;
                continue;
            }

            var glyphs = Glyphs(token.Text, Ink(token.Kind));

            // The piece is called what the grammar called it, so what a reader pressed and what the
            // theme coloured are the same question asked of the same piece.
            LayoutText.Words(into, glyphs, new Point(x, y), glyphs.Width + 1, TextAlignment.Left,
                             token, token.Kind, maps: true, ink: Ink(token.Kind));

            x += glyphs.Width;
            wide = Math.Max(wide, x);
        }

        into.Close();

        return new Laid(into.Seal(), new Size(Math.Max(wide, 1), Math.Max(y + Height, Height)), []);
    }

    /// <inheritdoc/>
    protected override FormattedText Characters(string text) => Glyphs(text, Style.Text);

    /// <summary>
    /// The pieces to set, a line at a time with a line end between (<see cref="CodeLines"/>): what a grammar found, where one
    /// has been read against this — and otherwise the body held as written, which is the uncoloured case and the same path.
    /// </summary>
    private IEnumerable<ContentPart> Tokens()
    {
        if (Reading.Root.Part(Roles.Body) is not { } body) yield break;

        if (body.Children.Count == 0)
        {
            yield return body;

            yield break;
        }

        foreach (var token in body.Children) yield return token;
    }

    /// <summary>How tall one line is.</summary>
    private double Height => Glyphs(" ", Style.Text).Height;

    /// <summary>
    /// What a kind is drawn in. A kind the theme says nothing about is body text — which is every kind when
    /// the grammar has not been read against this yet, and is the whole of the uncoloured case.
    /// </summary>
    private Brush Ink(string kind)
    {
        if (_inks.TryGetValue(kind, out var known)) return known ?? Style.Text;

        var found = SyntaxTokenMap.ResourceKey(kind) is { } key
            ? Application.Current?.TryFindResource(key) as Brush
            : null;

        _inks[kind] = found;

        return found ?? Style.Text;
    }

    private FormattedText Glyphs(string text, Brush ink) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Style.Face(Style.MonoFont),
            Math.Max(1, Style.TextSize * 0.94), ink, LayoutText.Density);
}
