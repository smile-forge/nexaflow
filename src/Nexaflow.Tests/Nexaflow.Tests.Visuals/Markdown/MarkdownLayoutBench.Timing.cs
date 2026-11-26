using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Documents;
using System.Windows.Input;

using ICSharpCode.AvalonEdit;
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What each half costs in time, on the same text — AvalonEdit's box against a surface of the engine's, set as the page sets each:
/// how long the first screen takes, a keystroke at the top, the middle and the end, a page down, and Ctrl+End.
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

    /// <summary>What each half costs in time over <paramref name="text"/>.</summary>
    private static Dictionary<string, object> TimedHalves(string name, string text) => new()
    {
        ["name"] = name,
        ["kb"] = Math.Round(text.Length / 1024.0, 1),
        ["avalonEdit"] = TimedAvalon(text),
        ["engine"] = TimedEngine(text),
    };

    private static Dictionary<string, object> TimedAvalon(string text)
    {
        var editor = new TextEditor
        {
            WordWrap = true,
            ShowLineNumbers = false,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"),
            FontSize = 14,
            Padding = new System.Windows.Thickness(40, 24, 40, 48),
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
        };

        var window = Hosted(editor);
        window.Show();
        Settle();

        var open = Stopwatch.StartNew();
        editor.Document.Text = text;
        editor.UpdateLayout();
        var first = open.Elapsed.TotalMilliseconds;
        Settle();
        var settled = open.Elapsed.TotalMilliseconds;

        var area = editor.TextArea;
        var row = new Dictionary<string, object>
        {
            ["openMs"] = Math.Round(first, 1),
            ["openSettledMs"] = Math.Round(settled, 1),
            ["keyTop"] = Keys(editor, () => area.Caret.Offset = 0, () => area.PerformTextInput("x")),
            ["keyMiddle"] = Keys(editor, () => area.Caret.Offset = editor.Document.TextLength / 2, () => area.PerformTextInput("x")),
            ["keyEnd"] = Keys(editor, () => area.Caret.Offset = editor.Document.TextLength, () => area.PerformTextInput("x")),
            ["pageDownMs"] = PagesDown(editor, () => area.Caret.Offset = 0, () => EditingCommands.MoveDownByPage.Execute(null, area)),
            ["toEndMs"] = Once(editor, () => area.Caret.Offset = 0, () => EditingCommands.MoveToDocumentEnd.Execute(null, area)),
        };

        window.Close();
        Settle();

        return row;
    }

    private static Dictionary<string, object> TimedEngine(string text)
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

        var document = new EditedDocument(text);

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
