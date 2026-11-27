using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What a surface of the engine's costs in time, on the same text two ways — held as a string (<c>engine</c>), and read from a file on
/// disk a block at a time (<c>onDisk</c>): how long the first screen takes, a keystroke at the top, the middle and the end, a page
/// down, and Ctrl+End.
///
/// <para>
/// A keystroke is timed twice: to when the half has laid out and drawn what it changed (<c>sync</c>, everything a
/// <see cref="System.Windows.UIElement.UpdateLayout"/> runs, drawing included), and to when the dispatcher has nothing left that the
/// keystroke queued (<c>settled</c>). Each is the median and the ninetieth percentile over <see cref="TimedKeys"/> keystrokes, in
/// milliseconds. The window, the font size and the wrapping are each half's as the page sets them.
/// </para>
/// </summary>
public partial class MarkdownLayoutBench
{
    /// <summary>How many keystrokes are timed at each place.</summary>
    private const int TimedKeys = 200;

    /// <summary>How many pages down are timed.</summary>
    private const int TimedPages = 40;

    [TestMethod]
    public void WhatEachHalfCostsInTime() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        Directory.CreateDirectory(folder!);
        var file = Path.Combine(folder!, $"markdown-source-timing-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        // Every type each half uses loaded and every cache it fills filled first, so what is timed is what the text costs it.
        TimedHalves("warming up", GeneratedProse(64 * 1024));

        var rows = new List<Dictionary<string, object>>();
        foreach (var size in SourceSizes)
        {
            rows.Add(TimedHalves($"prose {size / 1024} KB", GeneratedProse(size)));
            File.WriteAllText(file, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
    });

    /// <summary>
    /// What the colour bars cost a keystroke in a code file: the same C# on the code editor's surface, its view with no layer of marks and with
    /// the layer that bars every colour the source writes — which is asked of on every paint, over what is on screen.
    /// </summary>
    [TestMethod]
    public void WhatTheColourBarsCostACodeKeystroke() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        Directory.CreateDirectory(folder!);
        var file = Path.Combine(folder!, $"code-swatch-timing-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var code = GeneratedColouredCode(4000);

        TimedCode(code, barred: false);

        var rows = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "no marks", ["engine"] = TimedCode(code, barred: false) },
            new() { ["name"] = "colour bars", ["engine"] = TimedCode(code, barred: true) },
        };

        File.WriteAllText(file, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    });

    /// <summary>A keystroke in the middle of <paramref name="code"/>, C#, on the code editor's surface — the colours barred where <paramref name="barred"/> says.</summary>
    private static Dictionary<string, object> TimedCode(string code, bool barred)
    {
        var editor = new ContentEditor(new EditedDocument(code));
        if (barred) editor.Marks.Add(new Nexaflow.Visuals.Text.Markdown.Code.ColourSwatchMarks("c-sharp"));

        var surface = new NexaTextSurface
        {
            IsReadOnly = false,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Palette = Nexaflow.Visuals.Text.Painting.WpfStyles.Dark,
            WrittenIn = "c-sharp",
            Editor = editor,
        };

        var window = Hosted(surface);
        window.Show();
        surface.UpdateLayout();
        Settle();

        var engine = surface.Engine;
        var row = new Dictionary<string, object>
        {
            ["keyMiddle"] = Keys(surface, () => engine.MoveCaretTo(engine.State.Document.Length / 2), () => engine.Input(new ContentText("x"))),
        };

        window.Close();
        Settle();
        editor.Dispose();

        return row;
    }

    /// <summary>C# of about <paramref name="lines"/> lines, every few of them writing a colour, as a theme's or a view's code does.</summary>
    private static string GeneratedColouredCode(int lines)
    {
        var code = new System.Text.StringBuilder("namespace Bench;\n\npublic static class Palette\n{\n");
        for (var line = 0; line < lines; line++)
            code.Append(line % 3 == 0
                ? $"    public const string Colour{line} = \"#{line * 2654435761u % 0xFFFFFF:X6}\"; // a colour the bar shows\n"
                : $"    public static int Value{line}(int a, int b) => a * {line} + b - {line % 17};\n");

        return code.Append("}\n").ToString();
    }

    /// <summary>What each half costs in time over <paramref name="text"/>.</summary>
    private static Dictionary<string, object> TimedHalves(string name, string text) => new()
    {
        ["name"] = name,
        ["kb"] = Math.Round(text.Length / 1024.0, 1),
        ["engine"] = TimedEngine(text, onDisk: false),
        ["onDisk"] = TimedEngine(text, onDisk: true),
    };

    /// <summary>A surface of the engine's over <paramref name="text"/> — held as a string, or read from a file on disk where <paramref name="onDisk"/> says — timed.</summary>
    private static Dictionary<string, object> TimedEngine(string text, bool onDisk)
    {
        var surface = new NexaTextSurface
        {
            IsReadOnly = false,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Palette = Nexaflow.Visuals.Text.Painting.WpfStyles.Dark,
            WrittenIn = ContentLanguages.AsTyped,
            WrapsLines = true,
        };

        var window = Hosted(surface);
        window.Show();
        Settle();

        var (document, file) = onDisk ? OnDisk(text) : (new EditedDocument(text), (ContentFile?)null);

        var open = Stopwatch.StartNew();
        surface.Document = document;
        surface.UpdateLayout();
        var first = open.Elapsed.TotalMilliseconds;
        Settle();
        var settled = open.Elapsed.TotalMilliseconds;

        var engine = surface.Engine;
        var row = new Dictionary<string, object>
        {
            ["openMs"] = Math.Round(first, 1),
            ["openSettledMs"] = Math.Round(settled, 1),
            ["keyTop"] = Keys(surface, () => engine.MoveCaretTo(0), () => engine.Input(new ContentText("x"))),
            ["keyMiddle"] = Keys(surface, () => engine.MoveCaretTo(engine.State.Document.Length / 2), () => engine.Input(new ContentText("x"))),
            ["keyEnd"] = Keys(surface, () => engine.MoveCaretTo(engine.State.Document.Length), () => engine.Input(new ContentText("x"))),
            ["pageDownMs"] = PagesDown(surface, () => engine.MoveCaretTo(0), () => engine.Input(new ContentKey(Key.PageDown, ModifierKeys.None))),
            ["toEndMs"] = Once(surface, () => engine.MoveCaretTo(0), () => engine.Input(new ContentKey(Key.End, ModifierKeys.Control))),
        };

        window.Close();
        Settle();
        GC.KeepAlive(document);

        if (file is not null)
        {
            file.Dispose();
            File.Delete(file.Path);
        }

        return row;
    }

    /// <summary>
    /// <see cref="TimedKeys"/> keystrokes made by <paramref name="key"/> after <paramref name="place"/> puts the caret where they go, each timed
    /// to <paramref name="half"/> laid out and drawn, and to the dispatcher settled.
    /// </summary>
    private static Dictionary<string, object> Keys(System.Windows.FrameworkElement half, Action place, Action key)
    {
        place();
        half.UpdateLayout();
        Settle();

        var (sync, settled) = (new double[TimedKeys], new double[TimedKeys]);

        for (var at = 0; at < TimedKeys; at++)
        {
            var timed = Stopwatch.StartNew();
            key();
            half.UpdateLayout();
            sync[at] = timed.Elapsed.TotalMilliseconds;
            Settle();
            settled[at] = timed.Elapsed.TotalMilliseconds;
        }

        return new Dictionary<string, object>
        {
            ["syncP50"] = Percentile(sync, 0.5),
            ["syncP90"] = Percentile(sync, 0.9),
            ["settledP50"] = Percentile(settled, 0.5),
            ["settledP90"] = Percentile(settled, 0.9),
        };
    }

    /// <summary>The median page down of <see cref="TimedPages"/> from where <paramref name="place"/> puts the caret, to settled.</summary>
    private static double PagesDown(System.Windows.FrameworkElement half, Action place, Action page)
    {
        place();
        half.UpdateLayout();
        Settle();

        var pages = new double[TimedPages];
        for (var at = 0; at < TimedPages; at++)
        {
            var timed = Stopwatch.StartNew();
            page();
            half.UpdateLayout();
            Settle();
            pages[at] = timed.Elapsed.TotalMilliseconds;
        }

        return Percentile(pages, 0.5);
    }

    /// <summary>One <paramref name="act"/> from where <paramref name="place"/> puts the caret, to settled.</summary>
    private static double Once(System.Windows.FrameworkElement half, Action place, Action act)
    {
        place();
        half.UpdateLayout();
        Settle();

        var timed = Stopwatch.StartNew();
        act();
        half.UpdateLayout();
        Settle();

        return Math.Round(timed.Elapsed.TotalMilliseconds, 2);
    }

    private static double Percentile(double[] values, double at)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return Math.Round(sorted[Math.Min(sorted.Length - 1, (int)(at * sorted.Length))], 3);
    }
}
