using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using ICSharpCode.AvalonEdit;
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What the raw half of the Markdown page holds beside the document both halves show — measured before it is replaced, to say whether
/// replacing it gains anything.
///
/// <para>
/// The half the page has is AvalonEdit's box, which keeps the text a second time in a document of its own, its lines in a tree of its
/// own and what was typed in a history of its own. The half it would have is a surface of the engine's holding the page's own
/// document (<see cref="EditedDocument"/>), so no copy of it and one history between the halves. Each is put in a window, hidden as the
/// rendered view leaves it and shown as the source view shows it, and what it holds is read from the heap once it has settled, then
/// again after <see cref="SourceKeys"/> keystrokes. The document itself is made before each measure and counted in none: the page holds
/// it whichever half shows it.
/// </para>
/// <para>
/// The history the page keeps of those keystrokes is measured on its own (<c>historyKb</c>). The engine's half writes it, so its
/// <c>typedKb</c> includes it; AvalonEdit's half pays it too, through the rendered half every keystroke is handed to, on top of its own.
/// </para>
/// <para>
/// The engine's half is laid as code no grammar reads, which is how the raw half draws — uncoloured — except that code does not wrap
/// and the raw half does; what it holds is its reading of the whole document and the lines near the screen, neither of which wrapping
/// changes in kind.
/// </para>
/// </summary>
public partial class MarkdownLayoutBench
{
    /// <summary>The sizes the prose is generated at, in characters.</summary>
    private static readonly int[] SourceSizes = [100 * 1024, 1024 * 1024, 10 * 1024 * 1024, 50 * 1024 * 1024];

    /// <summary>How many keystrokes are typed into each half.</summary>
    private const int SourceKeys = 1000;

    [TestMethod]
    public void WhatTheSourceHalfHolds() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        Directory.CreateDirectory(folder!);
        var file = Path.Combine(folder!, $"markdown-source-half-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        var support = File.ReadAllText(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        // Every type each half uses loaded and every cache it fills filled first, so what is held is what the document costs it.
        SourceHalves("warming up", support);

        var rows = new List<Dictionary<string, object>> { SourceHalves("MarkdownSupport.md", support) };
        foreach (var size in SourceSizes)
        {
            rows.Add(SourceHalves($"prose {size / 1024} KB", GeneratedProse(size)));
            File.WriteAllText(file, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }

        File.WriteAllText(file, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    });

    /// <summary>What each half holds of <paramref name="text"/>, hidden and shown, and what the page's history of typing in it holds.</summary>
    private static Dictionary<string, object> SourceHalves(string name, string text)
    {
        var row = new Dictionary<string, object>
        {
            ["name"] = name,
            ["kb"] = Math.Round(text.Length / 1024.0, 1),
            ["documentKb"] = Math.Round(Retained(() => new EditedDocument(text)), 1),
        };

        foreach (var shown in new[] { false, true })
        {
            var showing = shown ? "shown" : "hidden";

            row[$"avalonEdit {showing}"] = Held(new EditedDocument(text), _ => AvalonHalf(text, shown), editor =>
            {
                editor.TextArea.Caret.Offset = editor.Document.TextLength;
                for (var key = 0; key < SourceKeys; key++) editor.TextArea.PerformTextInput("x");
            });

            row[$"engine {showing}"] = Held(new EditedDocument(text), document => EngineHalf(document, shown), surface =>
            {
                var engine = surface.Engine;
                engine.Uncover(text.Length);
                Settle();
                engine.MoveCaretTo(text.Length);
                for (var key = 0; key < SourceKeys; key++) engine.Input(new ContentText("x"));
                ReadAll(surface);
            });
        }

        row["historyKb"] = Math.Round(Typed(new EditedDocument(text)), 1);

        return row;
    }

    /// <summary>
    /// What a half <paramref name="made"/> over <paramref name="document"/> holds once it has settled, and how much more once
    /// <paramref name="typed"/> has been typed into it — the document made before the first reading, and so in neither.
    /// </summary>
    private static Dictionary<string, object> Held<T>(EditedDocument document, Func<EditedDocument, (T Half, Window Window)> made, Action<T> typed)
    {
        var before = Quiet();
        var (half, window) = made(document);
        var opened = Quiet();

        typed(half);
        var after = Quiet();

        window.Close();
        Settle();
        GC.KeepAlive(half);
        GC.KeepAlive(document);

        return new Dictionary<string, object>
        {
            ["openKb"] = Math.Round((opened - before) / 1024.0, 1),
            ["typedKb"] = Math.Round((after - opened) / 1024.0, 1),
        };
    }

    /// <summary>AvalonEdit's box as the page sets it, given the whole text as the page gives it, with nothing to take back.</summary>
    private static (TextEditor Half, Window Window) AvalonHalf(string text, bool shown)
    {
        var editor = new TextEditor
        {
            WordWrap = true,
            ShowLineNumbers = false,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 14,
            Padding = new Thickness(40, 24, 40, 48),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Visibility = shown ? Visibility.Visible : Visibility.Collapsed,
        };

        var window = Hosted(editor);
        window.Show();
        Settle();

        editor.Document.Text = text;
        editor.Document.UndoStack.ClearAll();
        Settle();

        return (editor, window);
    }

    /// <summary>A surface of the engine's holding <paramref name="document"/> as written, once it has read all of it.</summary>
    private static (MarkdownSurface Half, Window Window) EngineHalf(EditedDocument document, bool shown)
    {
        var surface = new MarkdownSurface
        {
            IsReadOnly = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Palette = WpfStyles.Dark,
            WrittenIn = ContentLanguages.AsTyped,
            WrapsLines = true,
            Visibility = shown ? Visibility.Visible : Visibility.Collapsed,
        };

        var window = Hosted(surface);
        window.Show();
        Settle();

        surface.Document = document;
        Settle();

        ReadAll(surface);
        return (surface, window);
    }

    /// <summary>What the page's history of <see cref="SourceKeys"/> keystrokes at the end of <paramref name="document"/> holds, in KB.</summary>
    private static double Typed(EditedDocument document)
    {
        var before = Quiet();

        var state = new EditState(document.Current, document.Current.Length);
        for (var key = 0; key < SourceKeys; key++)
        {
            var after = state.Made(ContentChange.Write(state.Caret, 0, "x"));
            document.Wrote(state, after);
            state = after;
        }

        var held = Quiet();
        GC.KeepAlive(document);

        return Math.Max(0, held - before) / 1024.0;
    }

    /// <summary>
    /// What the heap holds once the window has nothing left to do and a collection frees nothing more. A closed window lets go of the
    /// last of what it showed over the dispatcher's next turns, so a collection straight after it counts that as still held — by whatever
    /// is measured next.
    /// </summary>
    private static long Quiet()
    {
        var held = GC.GetTotalMemory(forceFullCollection: true);
        for (var pass = 0; pass < 20; pass++)
        {
            Settle();
            Thread.Sleep(50);
            GC.WaitForPendingFinalizers();

            var now = GC.GetTotalMemory(forceFullCollection: true);
            if (Math.Abs(held - now) < 64 * 1024) return now;

            held = now;
        }

        return held;
    }

    /// <summary>Waits while <paramref name="surface"/> reads what it has not read yet, up to <see cref="ReadBudget"/>.</summary>
    private static void ReadAll(MarkdownSurface surface)
    {
        var reading = Stopwatch.StartNew();
        while (!surface.Engine.ReadingRest.IsCompleted && reading.Elapsed < ReadBudget)
        {
            Settle();
            Thread.Sleep(10);
        }

        Settle();
    }
}
