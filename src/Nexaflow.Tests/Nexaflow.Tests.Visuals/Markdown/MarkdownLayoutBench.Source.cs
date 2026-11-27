using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What a surface of the engine's holds beside the document it shows, two ways: over the text held as a string (<c>engine</c>), as a
/// page that was handed it holds it, and over the same text read from a file on disk a block at a time (<c>onDisk</c>), as the Text
/// viewer, the log viewer and a code file past the editable ceiling hold it.
///
/// <para>
/// Each is put in a window, hidden as the rendered view leaves it and shown as the source view shows it, and what it holds is read from
/// the heap once it has settled, then again after <see cref="SourceKeys"/> keystrokes. The document itself is made before each measure
/// and counted in none: the page holds it whichever way it is read.
/// </para>
/// <para>
/// The history the page keeps of those keystrokes is measured on its own (<c>historyKb</c>); each half writes it, so its <c>typedKb</c>
/// includes it.
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

            row[$"engine {showing}"] = Held(new EditedDocument(text), document => EngineHalf(document, shown), surface => TypedAtTheEnd(surface, text.Length));

            var (onDisk, file) = OnDisk(text);
            row[$"onDisk {showing}"] = Held(onDisk, document => EngineHalf(document, shown), surface => TypedAtTheEnd(surface, text.Length));
            file.Dispose();
            File.Delete(file.Path);
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

    /// <summary>
    /// <paramref name="text"/> written to a file and opened as a document over its blocks on disk — as the Text viewer, the log viewer and a
    /// code file past the editable ceiling open one — with the file, to let go of and delete once the half is done with it.
    /// </summary>
    private static (EditedDocument Document, ContentFile File) OnDisk(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexaflow-bench-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));

        var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.Editable);
        file.Scan();

        return (new EditedDocument(ContentDocument.Of(file)), file);
    }

    /// <summary><see cref="SourceKeys"/> keystrokes at the end of what <paramref name="surface"/> shows, <paramref name="length"/> characters of it, every line of it then read.</summary>
    private static void TypedAtTheEnd(NexaTextSurface surface, int length)
    {
        var engine = surface.Engine;
        engine.Uncover(length);
        Settle();
        engine.MoveCaretTo(length);
        for (var key = 0; key < SourceKeys; key++) engine.Input(new ContentText("x"));
        ReadAll(surface);
    }

    /// <summary>A surface of the engine's holding <paramref name="document"/> as written, once it has read all of it.</summary>
    private static (NexaTextSurface Half, Window Window) EngineHalf(EditedDocument document, bool shown)
    {
        var surface = new NexaTextSurface
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
    private static void ReadAll(NexaTextSurface surface)
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
