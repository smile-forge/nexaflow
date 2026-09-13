using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Latex;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Parsers;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex;
using TexEnvironment = Nexaflow.Visuals.Text.Markdown.Latex.Tex.TexEnvironment;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// A formula read and set exactly as the app does it, and the handful of things TeX's rules are checked against:
/// how big it is, what it draws, and what it could not.
/// <para>
/// Measured rather than inspected. What a construct is built as is the builder's own business and changes as the
/// builder does; how wide it comes out, where its ink lands and which face a letter is drawn from are what TeX's
/// rules actually say, and they stay true however the setting is done.
/// </para>
/// </summary>
internal static class Typeset
{
    /// <summary>The fonts and style a formula is set in, made once per thread and style — they belong to the thread.</summary>
    [ThreadStatic]
    private static Dictionary<TexStyle, TexEnvironment>? _environments;

    private static TexFormulaParser Knowledge => WpfTeXFormulaParser.Instance;

    public static TexEnvironment Environment(TexStyle style = TexStyle.Display)
    {
        _environments ??= new Dictionary<TexStyle, TexEnvironment>();
        if (!_environments.TryGetValue(style, out var environment))
            _environments[style] = environment = WpfTeXEnvironment.Create(style);

        return environment;
    }

    /// <summary>The reading, what was set from it, and what in it had no drawing.</summary>
    public static (ContentReading Reading, Set? Set, IReadOnlyList<ContentPart> Ignored) Read(
        string markup, TexStyle style = TexStyle.Display)
    {
        var reading = ContentReading.Of(TexPipeline.Read(markup, name => LatexBuilder.Draws(name, Knowledge)));
        var (set, ignored) = LatexBuilder.Formula(reading.Root, Environment(style), Knowledge);
        return (reading, set, ignored);
    }

    /// <summary>The formula, set. Fails only where nothing in it could be set as maths.</summary>
    public static Set Formula(string markup, TexStyle style = TexStyle.Display) =>
        Read(markup, style).Set ?? throw new AssertFailedException($"nothing in '{markup}' could be set as maths");

    public static double Width(string markup, TexStyle style = TexStyle.Display) => Formula(markup, style).Width;

    public static double Height(string markup, TexStyle style = TexStyle.Display) => Formula(markup, style).Height;

    public static double Depth(string markup, TexStyle style = TexStyle.Display) => Formula(markup, style).Depth;

    public static double TotalHeight(string markup, TexStyle style = TexStyle.Display)
    {
        var set = Formula(markup, style);
        return set.Height + set.Depth;
    }

    /// <summary>Which font the last character came out of — what a width cannot tell apart.</summary>
    public static int FontOf(string markup) => Formula(markup).LastFontId;

    /// <summary>
    /// That the whole formula is set as maths: it builds, nothing in it is shown as the characters it was written
    /// with, and nothing in it went unread.
    /// </summary>
    public static void Renders(string markup)
    {
        var (_, set, ignored) = Read(markup);
        Assert.IsNotNull(set, $"nothing in '{markup}' could be set as maths");
        Assert.AreEqual(0, ignored.Count, $"'{markup}' set as characters: {string.Join(", ", ignored.Select(part => part.Print()))}");
        CollectionAssert.AreEqual(Array.Empty<string>(), Unreadable(markup).ToList(), $"'{markup}' has stretches nothing could read");
    }

    /// <summary>The stretches the reader could not read at all — a command nobody has heard of, and the like.</summary>
    public static IReadOnlyList<string> Unreadable(string markup) =>
        Read(markup).Reading.Root.SelfAndDescendants()
            .Where(part => part.Trouble is not null)
            .Select(part => part.Print())
            .Distinct()
            .ToList();

    /// <summary>What the reading says about each of those — what the reader is shown on hovering one.</summary>
    public static IReadOnlyList<string> Reasons(string markup) =>
        Read(markup).Reading.Root.SelfAndDescendants()
            .Where(part => part.Trouble is not null)
            .Select(part => part.Trouble!)
            .Distinct()
            .ToList();

    /// <summary>The stretches that were read but have no drawing, and so are set as the characters written.</summary>
    public static IReadOnlyList<string> Undrawn(string markup)
    {
        var (_, set, ignored) = Read(markup);
        return set is null ? [markup] : ignored.Select(part => part.Print()).Distinct().ToList();
    }

    /// <summary>
    /// The formula laid into the layout tree at unit scale, which is settled onto its ink. A formula that draws nothing — a
    /// space, a phantom — lays no tree at all.
    /// </summary>
    public static LatexCapture Laid(string markup)
    {
        var (reading, set, _) = Read(markup);
        Assert.IsNotNull(set, $"nothing in '{markup}' could be set as maths");

        var capture = new LatexCapture(1.0, reading);
        capture.Lay(set);
        return capture;
    }

    /// <summary>Every mark the formula draws, with the x its piece lands at measured from the formula's left edge.</summary>
    public static IReadOnlyList<(LayoutMark Mark, double X)> Marks(string markup)
    {
        var marks = new List<(LayoutMark, double)>();
        if (Laid(markup).Tree is not { } tree) return marks;

        var left = tree.AnchorOf(0).X;
        for (var at = 0; at < tree.Count; at++)
            foreach (var mark in tree.MarksOf(at).ToArray())
                marks.Add((mark, tree.AnchorOf(at).X - left));

        return marks;
    }

    /// <summary>How far above the top of its box the formula's ink reaches — the settle moves the box down by that.</summary>
    public static double InkTop(string markup) => -Laid(markup).Tree!.AnchorOf(0).Y - Formula(markup).Height;
}
