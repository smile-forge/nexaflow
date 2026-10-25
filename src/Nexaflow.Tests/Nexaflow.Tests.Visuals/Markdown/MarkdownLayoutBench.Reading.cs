using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What reading a long document costs once it is open — scrolling it, pointing at it, dragging across it, picking all of it
/// out. A layout timed on its own leaves all of that out: painting a screen of it again, laying further as the reader moves,
/// and asking the layout what is under the pointer every time it moves, each of which grows with the document rather than the
/// screen if anything in it walks the whole of what was laid. So these are timed on a surface in a window, as a page shows it.
/// </summary>
public partial class MarkdownLayoutBench
{
    /// <summary>
    /// How many sections the generated lecture script has: about a megabyte, with as many formulas in it as a term's notes —
    /// the shape of document that showed every one of those costs.
    /// </summary>
    internal const int LectureSections = 400;

    /// <summary>How many screens are read on down from the top, a screen at a time.</summary>
    private const int Pages = 60;

    /// <summary>How many turns of the wheel, how many jumps of the thumb, and how many moves of the pointer each is timed over.</summary>
    private const int Turns = 20;
    private const int Jumps = 10;
    private const int Moves = 30;

    /// <summary>How far one turn of the wheel scrolls.</summary>
    private const double Notch = 48;

    /// <summary>The generated lecture script and the largest document in the repository, each read as a page reads it.</summary>
    private static List<Dictionary<string, object>> Readings(string largest) =>
    [
        Reading($"generated-lecture-{LectureSections}-sections.md", LectureScript(LectureSections)),
        Reading(Path.GetFileName(largest), File.ReadAllText(largest)),
    ];

    /// <summary>One document, opened on a surface in a window and read: each step timed until the window has nothing left to do.</summary>
    private static Dictionary<string, object> Reading(string name, string text)
    {
        var surface = new MarkdownSurface
        {
            IsReadOnly = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Palette = WpfStyles.Dark,
        };

        // Off the desktop but shown: what is not shown is neither laid out nor painted.
        var window = new Window
        {
            Width = Room + 40,
            Height = Screen.Height,
            Left = -32000,
            Top = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = surface,
        };

        window.Show();

        try
        {
            Settle();

            var clock = Stopwatch.StartNew();
            surface.Markdown = text;
            Settle();
            var opened = clock.Elapsed.TotalMilliseconds;

            while (!surface.Engine.ReadingRest.IsCompleted)
            {
                Settle();
                Thread.Sleep(10);
            }

            Settle();

            var scroller = surface.Scroller;
            var element = surface.Shown;

            // Down from the top a screen at a time, as a reader goes through it: what laying further on the way costs.
            var paging = Steps(Pages, () => scroller.ScrollToVerticalOffset(scroller.VerticalOffset + (scroller.ViewportHeight * 0.9)));

            // The thumb dragged anywhere, which lays everything above where it lands the first time.
            var random = new Random(7);
            var jumping = Steps(Jumps, () => scroller.ScrollToVerticalOffset(scroller.ScrollableHeight * random.NextDouble()));

            // A turn of the wheel, at three depths, once what is there is laid.
            var wheel = new Dictionary<string, object>();
            foreach (var depth in new[] { 0.1, 0.5, 0.9 })
            {
                To(scroller, depth);
                wheel[$"{depth * 100:0}%"] = Summary(Steps(Turns, () => scroller.ScrollToVerticalOffset(scroller.VerticalOffset + Notch)));
            }

            // Halfway down, the pointer moving over a screen of it: the cursor it shows, the block it is over, and a drag down
            // the screen from its top.
            To(scroller, 0.5);
            var top = new Point(120, scroller.VerticalOffset + 80);
            Point Along(int step, int across) => new(top.X + (step * across % 500), top.Y + (step * 18));

            var pointed = 0;
            var pointing = Steps(Moves, () => element.PointerCursor(Along(++pointed, 13)));

            var hovered = 0;
            var hovering = Steps(Moves, () => surface.Engine.Input(new ContentHover(Along(++hovered, 11).ToLayout())));

            element.BeginPointerSelect(top);
            Settle();

            var dragged = 0;
            var dragging = Steps(Moves, () => element.ExtendPointerSelect(Along(++dragged, 7)));
            var selected = element.SelectionLength;

            element.EndPointerSelect();
            element.ClearSelection();
            Settle();

            // Everything picked out, and the wheel turned with all of it picked out.
            var all = Steps(1, element.SelectAll)[0];
            var wheelSelected = Steps(Turns, () => scroller.ScrollToVerticalOffset(scroller.VerticalOffset + Notch));

            element.ClearSelection();
            Settle();

            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["kb"] = Math.Round(text.Length / 1024.0, 1),
                ["pieces"] = element.Laid.Tree.Count,
                ["trouble"] = element.Laid.Trouble.Count,
                ["opened"] = Round(opened),
                ["pageDown"] = Summary(paging),
                ["jump"] = Summary(jumping),
                ["wheel"] = wheel,
                ["pointer"] = Summary(pointing),
                ["hover"] = Summary(hovering),
                ["drag"] = Summary(dragging),
                ["dragSelected"] = selected,
                ["selectAll"] = Round(all),
                ["wheelAllSelected"] = Summary(wheelSelected),
            };
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Scrolls to <paramref name="depth"/> of the way down and waits for it to be laid and painted there.</summary>
    private static void To(ScrollViewer scroller, double depth)
    {
        scroller.ScrollToVerticalOffset(scroller.ScrollableHeight * depth);
        Settle();
    }

    /// <summary>
    /// <paramref name="step"/> taken <paramref name="count"/> times, each timed until the window has nothing left to do — laying
    /// out, painting and anything they set going — which is how long the reader waited for it.
    /// </summary>
    private static List<double> Steps(int count, Action step)
    {
        var times = new List<double>(count);

        for (var at = 0; at < count; at++)
        {
            var clock = Stopwatch.StartNew();

            step();
            Settle();

            times.Add(clock.Elapsed.TotalMilliseconds);
        }

        return times;
    }

    /// <summary>Waits until the window has done everything it was asked to before going idle.</summary>
    private static void Settle() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    /// <summary>
    /// The middle of a run of timings, the one nine in ten come under, and the worst — a reader notices the worst, and a step that
    /// is usually quick but now and then lays a stretch of the document is told by the last two.
    /// </summary>
    private static Dictionary<string, object> Summary(List<double> times)
    {
        var sorted = times.Order().ToList();

        return new Dictionary<string, object>
        {
            ["median"] = Round(sorted[sorted.Count / 2]),
            ["p90"] = Round(sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.9))]),
            ["max"] = Round(sorted[^1]),
        };
    }

    /// <summary>
    /// A long document written as a lecture script is: numbered sections of prose with formulas set into it, a displayed formula
    /// after every paragraph, a list, and now and then a statement set apart — and, every so often, a formula as somebody mistyped
    /// it, so the waves under what could not be set are in what is measured, as they are in a real script. The same every run,
    /// so runs compare.
    /// </summary>
    internal static string LectureScript(int sections)
    {
        string[] words = ["let", "the", "function", "be", "continuous", "on", "the", "closed", "interval", "and", "differentiable",
                          "in", "its", "interior", "then", "there", "exists", "a", "point", "such", "that", "it", "holds", "for",
                          "every", "sequence", "which", "converges"];

        string[] inline = [@"$f\colon [a,b] \to \mathbb{R}$", @"$\xi \in (a,b)$", @"$\varepsilon > 0$", @"$|x_n - x| < \delta$",
                           @"$\sum_{k=1}^{n} a_k$", @"$\lim_{n \to \infty} x_n = x$", @"$\int_a^b f(t)\,dt$",
                           @"$f'(\xi) = \frac{f(b) - f(a)}{b - a}$"];

        string[] displayed =
        [
            @"\int_a^b f(x)\,dx = \lim_{n \to \infty} \sum_{k=1}^{n} f(\xi_k)\,\Delta x_k",
            @"\begin{aligned} (a+b)^2 &= a^2 + 2ab + b^2 \\ (a-b)^2 &= a^2 - 2ab + b^2 \end{aligned}",
            @"f(x) = \begin{cases} x^2 & x \ge 0 \\ -x & x < 0 \end{cases}",
            @"\det \begin{pmatrix} a_{11} & a_{12} \\ a_{21} & a_{22} \end{pmatrix} = a_{11}a_{22} - a_{12}a_{21}",
            @"\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}",
            @"\frac{d}{dx} \left( \frac{u}{v} \right) = \frac{u'v - uv'}{v^2}",
        ];

        var text = new StringBuilder();
        var (word, formula, shown) = (0, 0, 0);

        string Inline() => inline[formula++ % inline.Length];

        for (var section = 1; section <= sections; section++)
        {
            text.Append("## ").Append(section).Append(". Theorem and proof\n\n");

            for (var paragraph = 0; paragraph < 4; paragraph++)
            {
                for (var at = 0; at < 60; at++)
                {
                    text.Append(words[word++ % words.Length]).Append(' ');
                    if (at % 15 == 7) text.Append(Inline()).Append(' ');
                }

                text.Append("\n\n$$\n").Append(++shown % 41 == 0 ? @"\frac{1}{2" : displayed[shown % displayed.Length]).Append("\n$$\n\n");
            }

            text.Append("1. For ").Append(Inline()).Append(" the limit exists.\n")
                .Append("2. Then ").Append(Inline()).Append(" for every ").Append(Inline()).Append(".\n")
                .Append("3. Hence ").Append(Inline()).Append(".\n\n");

            if (section % 7 == 0) text.Append("> **Theorem.** If ").Append(Inline()).Append(" then ").Append(Inline()).Append(".\n\n");
        }

        return text.ToString();
    }
}
