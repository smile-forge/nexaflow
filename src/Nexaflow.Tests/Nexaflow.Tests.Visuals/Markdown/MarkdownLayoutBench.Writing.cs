using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What writing in a document costs as a reader pays for it — the baseline an engine that owns its document is measured against.
///
/// <para>
/// Every step goes in as the window's keys go in (<see cref="ContentEngine.Input"/>), to a surface in a window, and is timed until
/// the window has nothing left to do: the edit made, what is kept so it can be taken back, the host told, laying out and
/// painting. An edit laid as a changed string, as <see cref="WhereLayingADocumentSpendsItsTime"/> times one, pays only the last two.
/// </para>
/// <para>
/// Over generated documents — prose and code at three sizes, and prose holding a large code fence — it times a keystroke at
/// each of a few places, taking back the last step, taking back fifty steps one at a time, pasting a megabyte and carrying a
/// word down the screen, and says how much fifty steps of history hold.
/// </para>
/// <para>
/// <strong>Bounded, because today's costs are what it measures.</strong> A size whose opening, scaled from the size before it,
/// would take longer than <see cref="OpenBudget"/> is skipped; a step whose repeats have taken longer than <see cref="StepBudget"/>
/// is not repeated; fifty steps that would hold more than <see cref="HistoryBudget"/> are not written. Each goes into the run with
/// why, rather than being left out. The run is written again after every document, and every step is said in a log beside it as
/// it starts, so a run stopped part-way keeps what it finished and a long one says where it is.
/// </para>
/// </summary>
public partial class MarkdownLayoutBench
{
    /// <summary>The sizes prose and code are generated at, in characters.</summary>
    private static readonly int[] WritingSizes = [50 * 1024, 1024 * 1024, 50 * 1024 * 1024];

    /// <summary>The sizes of the code fence the nested documents hold, in characters.</summary>
    private static readonly int[] FenceSizes = [1024 * 1024, 5 * 1024 * 1024];

    /// <summary>How many keystrokes are timed at each place.</summary>
    private const int KeysPerPlace = 10;

    /// <summary>How many steps are written, one at a time, and then taken back one at a time.</summary>
    private const int UndoSteps = 50;

    /// <summary>How many pastes are timed, each taken back before the next.</summary>
    private const int Pastes = 3;

    /// <summary>What a paste brings in, in characters.</summary>
    private const int PasteSize = 1024 * 1024;

    /// <summary>How long opening a size, or laying a document down to a place in it, may take before it is skipped.</summary>
    private static readonly TimeSpan OpenBudget = TimeSpan.FromSeconds(60);

    /// <summary>How long the rest of a long document is waited for, read in the background, before writing in it starts anyway.</summary>
    private static readonly TimeSpan ReadBudget = TimeSpan.FromMinutes(5);

    /// <summary>The most fifty steps of history may hold, kept as whole documents, before taking fifty steps back is skipped.</summary>
    private const long HistoryBudget = 2L * 1024 * 1024 * 1024;

    /// <summary>How long a step's repeats may take, all told, before it is repeated no more.</summary>
    private static readonly TimeSpan StepBudget = TimeSpan.FromSeconds(60);

    /// <summary>Names part of a document's name, to measure only the documents whose names hold it.</summary>
    private const string WritingOnly = "NEXAFLOW_WRITING_BENCH_ONLY";

    [TestMethod]
    public void WhatWritingInADocumentCosts() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        Directory.CreateDirectory(folder!);
        var file = Path.Combine(folder!, $"markdown-writing-bench-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var log = Path.ChangeExtension(file, ".log");

        var documents = new List<Dictionary<string, object>>();
        var skipped = new List<Dictionary<string, object>>();
        var pace = Pace();

        // Part of a name, to measure only the documents whose names hold it — for following one cost up without the rest of the run.
        var only = Environment.GetEnvironmentVariable(WritingOnly);
        bool Asked(string name) => string.IsNullOrEmpty(only) || name.Contains(only, StringComparison.OrdinalIgnoreCase);

        Sized("markdown", null, GeneratedProse, Asked, Said, Kept, Skipped);
        Sized("code", "csharp", GeneratedCode, Asked, Said, Kept, Skipped);

        foreach (var fence in FenceSizes)
        {
            if (!Asked($"prose-with-{fence / (1024 * 1024)}-mb-fence")) continue;

        var (text, start, end) = FencedCode(fence);

            Kept(WritingIn($"prose-with-{fence / (1024 * 1024)}-mb-fence", "nested", text, null,
                           [("in the fence", WordStartNear(text, (start + end) / 2)), ("after the fence", WordStartNear(text, (end + text.Length) / 2))],
                           null, Said, out _, out _));
        }

        Said("done");
        Console.WriteLine(file);

        void Said(string what) => File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss}  {what}{Environment.NewLine}");

        void Kept(Dictionary<string, object> row)
        {
            documents.Add(row);
            Written();
        }

        void Skipped(string name, string why)
        {
            skipped.Add(new() { ["name"] = name, ["why"] = why });
            Said($"{name}: skipped - {why}");
            Written();
        }

        void Written() => File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["label"] = Environment.GetEnvironmentVariable("NEXAFLOW_BENCH_LABEL") ?? "",
            ["commit"] = Environment.GetEnvironmentVariable("NEXAFLOW_BENCH_COMMIT") ?? "",
    #if DEBUG
            ["configuration"] = "Debug",
    #else
            ["configuration"] = "Release",
    #endif
            ["machine"] = Environment.MachineName,
            ["pace"] = pace,
            ["when"] = DateTimeOffset.Now.ToString("o"),
            ["keysPerPlace"] = KeysPerPlace,
            ["undoSteps"] = UndoSteps,
    ["pasteKb"] = PasteSize / 1024,
            ["only"] = only ?? "",
            ["documents"] = documents,
            ["skipped"] = skipped,
        }, new JsonSerializerOptions { WriteIndented = true }));
    });

    /// <summary>
    /// One kind of document at every size, smallest first, written in at its top, middle and end — skipping a size whose opening,
    /// scaled in proportion from the size before it, would take longer than <see cref="OpenBudget"/>, and every size after it; and,
    /// within a size, a place whose laying down to, scaled from the farthest place reached in the size before, would.
    /// </summary>
    private static void Sized(string kind, string? writtenIn, Func<int, string> make, Func<string, bool> asked, Action<string> said,
                              Action<Dictionary<string, object>> kept, Action<string, string> skipped)
    {
        (int Size, TimeSpan Opened)? last = null;
        (int Offset, double Ms)? reached = null;

        foreach (var size in WritingSizes)
        {
            var name = $"generated-{kind}-{size / 1024}-kb";
            if (!asked(name)) continue;

            if (last is { } before && before.Opened * ((double)size / before.Size) is var expected && expected > OpenBudget)
            {
                skipped(name, $"opening it would take about {expected.TotalSeconds:0} s, scaled from {before.Opened.TotalSeconds:0.0} s for "
                              + $"{before.Size / 1024} KB, past the {OpenBudget.TotalSeconds:0} s budget");
                continue;
            }

            var text = make(size);
            kept(WritingIn(name, kind, text, writtenIn,
                           [("top", WordStartNear(text, text.Length / 50)), ("middle", WordStartNear(text, text.Length / 2)),
                            ("end", WordStartNear(text, text.Length - (text.Length / 50)))],
                           reached, said, out var opened, out var farthest));

            last = (size, opened);
            reached = farthest ?? reached;
        }
    }

    /// <summary>
    /// One document, opened on a surface in a window and written in: each step timed until the window has nothing left to do, with
    /// what it allocated. <paramref name="places"/> are where keystrokes are timed, in the order they come in the document.
    /// </summary>
    private static Dictionary<string, object> WritingIn(string name, string kind, string text, string? writtenIn,
                                                        IReadOnlyList<(string Name, int At)> places, (int Offset, double Ms)? reached,
                                                                                                            Action<string> said, out TimeSpan opened, out (int Offset, double Ms)? farthest)
    {
        var surface = new MarkdownSurface
        {
            IsReadOnly = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Palette = WpfStyles.Dark,
            WrittenIn = writtenIn,
        };

        var window = Hosted(surface);
        window.Show();

        try
        {
            Settle();
            said($"{name}: opening {text.Length / 1024} KB");

            var clock = Stopwatch.StartNew();
            surface.Markdown = text;
            Settle();
            opened = clock.Elapsed;

            said($"{name}: opened in {opened.TotalSeconds:0.0} s; reading the rest");

            var reading = Stopwatch.StartNew();
            while (!surface.Engine.ReadingRest.IsCompleted && reading.Elapsed < ReadBudget)
            {
                Settle();
                Thread.Sleep(10);
            }

            object readAll = surface.Engine.ReadingRest.IsCompleted
                ? Round((opened + reading.Elapsed).TotalMilliseconds)
                : $"not read after {ReadBudget.TotalMinutes:0} min";

            Settle();

            var engine = surface.Engine;
            var row = new Dictionary<string, object>
            {
                ["name"] = name,
                ["kind"] = kind,
                ["kb"] = Math.Round(text.Length / 1024.0, 1),
                ["opened"] = Round(opened.TotalMilliseconds),
                ["readAll"] = readAll,
            };

// A letter at a time at each place, the caret put there first — the content laid out down to it first, as a search finding
// something further down lays it, since a caret is not put past what is laid. Getting there is timed on its own (reachMs),
// and a place is skipped where getting there, scaled from the farthest place reached so far — in the size before, or
// earlier in this one, which is all there is when only this size is measured — would take longer than the budget: scaled
// as the square of the distance, since laying further lays again what is laid. What was typed at one place moves every
// place after it along.
var keystrokes = new Dictionary<string, object>();
var typed = 0;
farthest = null;

foreach (var (place, at) in places)
{
    var wanted = at + typed;

    if (reached is { Offset: > 0 } before && before.Ms * Math.Pow((double)wanted / before.Offset, 2) is var expected
        && expected > OpenBudget.TotalMilliseconds)
    {
        keystrokes[place] = $"skipped: laying down to offset {wanted} would take about {expected / 1000:0} s, scaled as the square of "
                            + $"the distance from {before.Ms / 1000:0.0} s to reach {before.Offset}, past the {OpenBudget.TotalSeconds:0} s budget";
        said($"{name}: keystrokes - {place}: skipped");
        continue;
    }

    said($"{name}: keystrokes - {place}");

    var getting = Stopwatch.StartNew();
    engine.Uncover(wanted);
    Settle();
    engine.MoveCaretTo(wanted);
    Settle();
    var reach = getting.Elapsed.TotalMilliseconds;

    if (wanted > (farthest?.Offset ?? 0)) farthest = (wanted, reach);
    if (wanted > (reached?.Offset ?? 0)) reached = (wanted, reach);

    // Where the caret was put and where it went, and what typing there laid: a caret that went somewhere else times a
    // keystroke somewhere else.
    var landed = engine.State.Caret;
    var timed = Taken(KeysPerPlace, null, () => engine.Input(new ContentText("x")), Source);

    timed["reachMs"] = Round(reach);
    timed["wanted"] = wanted;
    timed["caret"] = landed;
    timed["laidPieces"] = engine.Laid.Tree.Count;
    keystrokes[place] = timed;
    typed += KeysPerPlace;
}

            row["keystroke"] = keystrokes;

    // The last step taken back: a letter typed as a step of its own, untimed, then Ctrl+Z.
            said($"{name}: taking back the last step");
            row["undoLatest"] = Taken(5, () => { engine.History.Break(); engine.Input(new ContentText("x")); }, Undo, Source);

            // Fifty steps, each a letter of its own, then taken back one at a time — and what keeping them held.
            var historyBytes = (long)engine.State.Source.Length * sizeof(char) * UndoSteps;

            if (historyBytes > HistoryBudget)
            {
                row["undoSteps"] = $"skipped: {UndoSteps} steps of a {text.Length / 1024} KB document hold {historyBytes / (1024.0 * 1024 * 1024):0.0} GB "
                                   + $"as whole documents, past the {HistoryBudget / (1024 * 1024 * 1024)} GB budget";
            }
            else
            {
    said($"{name}: writing {UndoSteps} steps and taking them back");
                var before = GC.GetTotalMemory(forceFullCollection: true);

                for (var step = 0; step < UndoSteps; step++)
                {
                    engine.History.Break();
                    engine.Input(new ContentText("x"));
                    Settle();
                }

                row["historyKb"] = Math.Round(Math.Max(0, GC.GetTotalMemory(forceFullCollection: true) - before) / 1024.0, 1);
                row["undoSteps"] = Taken(UndoSteps, null, Undo, Source);
            }

            // A megabyte pasted where the caret is, from the clipboard as the page hands it over, and taken back after each.
            var pasted = GeneratedProse(PasteSize);
    void Supplied(object? sender, ContentPastingEventArgs args)
            {
                (args.Words, args.Markdown) = (pasted, pasted);
                args.Handled = true;
            }

    said($"{name}: pasting {PasteSize / 1024} KB");
            surface.Pasting += Supplied;
            row["paste"] = Taken(Pastes, () => engine.History.Break(), () => engine.Input(new ContentKey(Key.V, ModifierKeys.Control)), Source, Undo);
            surface.Pasting -= Supplied;

    said($"{name}: carrying a word");
            row["drag"] = Carried(surface);
            said($"{name}: done");

            return row;

    void Undo() => engine.Input(new ContentKey(Key.Z, ModifierKeys.Control));

            object Source() => engine.State.Source;
        }
        finally
        {
            window.Close();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// A word on the first screen carried down it and let go: each move timed, since each lays the content out as it would read with
    /// the word let go there — then taken back. On the first screen because that is laid from opening, so what is timed is carrying,
    /// not laying the document down to wherever the word is; a jump down a long document is the reading bench's to time. Says whether
    /// the press picked the word up, since a press that does not drags out a selection instead, and what that costs is not this.
    /// </summary>
    private static object Carried(MarkdownSurface surface)
    {
        var engine = surface.Engine;

        To(surface.Scroller, 0);

        var source = engine.State.Source;
        var middle = engine.Laid.Root.OffsetAt(new Point(120, surface.Scroller.VerticalOffset + (Screen.Height / 3)).ToLayout());

        var start = middle;
        while (start > 0 && !char.IsWhiteSpace(source[start - 1])) start--;

        var end = middle;
        while (end < source.Length && !char.IsWhiteSpace(source[end])) end++;

        if (end <= start) return "nothing to carry on the first screen";

        engine.Select(start, end - start);
        Settle();

        if (engine.Laid.Root.RangeRects(start, end - start) is not [var word, ..]) return "the word on the first screen was not laid";

        var from = new LayoutPoint(word.X + (word.Size.Width / 2), word.Y + (word.Size.Height / 2));
        engine.Input(new ContentPress(from));
        Settle();

        var moved = 0;
        var carrying = Taken(Moves, null, () =>
        {
            moved++;
            engine.Input(new ContentDrag(new LayoutPoint(from.X + (moved * 13 % 300), from.Y + (moved * 18))));
        }, () => engine.State.Source);

        engine.Input(new ContentRelease());
        Settle();

        var picked = !ReferenceEquals(engine.State.Source, source);
        if (picked) engine.Input(new ContentKey(Key.Z, ModifierKeys.Control));

        Settle();

        carrying["picked"] = picked;
        return carrying;
    }

    /// <summary>
    /// <paramref name="step"/> taken up to <paramref name="count"/> times, each timed until the window has nothing left to do, with
    /// <paramref name="prepare"/> before it and <paramref name="after"/> after it untimed — the median, the step nine in ten come
    /// under, the worst, and the median allocated. Repeated no more once the steps timed have taken <see cref="StepBudget"/>, and
    /// then says how many were. Where <paramref name="source"/> says what is written, it also says how many steps changed it, so a
    /// step that did nothing is never read as a step that was quick.
    /// </summary>
    private static Dictionary<string, object> Taken(int count, Action? prepare, Action step, Func<object>? source = null, Action? after = null)
    {
        var times = new List<double>(count);
        var allocated = new List<double>(count);
        var changed = 0;

        for (var at = 0; at < count && times.Sum() < StepBudget.TotalMilliseconds; at++)
        {
            prepare?.Invoke();
            Settle();

            var was = source?.Invoke();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();

            step();
            Settle();

            times.Add(clock.Elapsed.TotalMilliseconds);
            allocated.Add((GC.GetAllocatedBytesForCurrentThread() - bytes) / 1024.0);
            if (source is not null && !ReferenceEquals(was, source())) changed++;

            after?.Invoke();
            Settle();
        }

        var summary = Summary(times);
        summary["kb"] = Math.Round(allocated.Order().ElementAt(allocated.Count / 2), 1);
        if (source is not null) summary["changed"] = changed;
        if (times.Count < count) summary["taken"] = $"{times.Count} of {count}, past the {StepBudget.TotalSeconds:0} s budget";

        return summary;
    }

    /// <summary>Where the word nearest <paramref name="at"/> starts, so a letter typed there lands at the front of a word.</summary>
    private static int WordStartNear(string text, int at)
    {
        at = Math.Clamp(at, 0, text.Length);
        while (at > 0 && !char.IsWhiteSpace(text[at - 1])) at--;

        return at;
    }

    /// <summary>The words generated prose is written in.</summary>
    private static readonly string[] Vocabulary =
    [
        "the", "engine", "reads", "every", "block", "once", "and", "lays", "what", "changed", "while", "a", "reader", "scrolls",
        "through", "notes", "written", "over", "several", "terms", "with", "lists", "tables", "and", "examples",
    ];

    /// <summary>
    /// Prose about <paramref name="size"/> characters long, written as long notes are: numbered sections of paragraphs with a word
    /// stressed or set as code here and there, a list every fifth section and a short code fence every seventh. The same every run.
    /// </summary>
    internal static string GeneratedProse(int size)
    {
        var text = new StringBuilder(size + 4096).Append("# Generated notes\n\n");
        var next = 0;

        for (var section = 1; text.Length < size; section++)
        {
            text.Append("## Section ").Append(section).Append("\n\n");

            for (var paragraph = 0; paragraph < 3; paragraph++)
            {
                for (var word = 0; word < 80; word++)
                {
                    var said = Vocabulary[next++ % Vocabulary.Length];

                    text.Append(word % 19 == 4 ? $"*{said}*" : word % 23 == 9 ? $"`{said}`" : said).Append(word == 79 ? ".\n\n" : " ");
                }
            }

            if (section % 5 == 0)
            {
                for (var item = 1; item <= 4; item++) text.Append("- item ").Append(item).Append(" of section ").Append(section).Append('\n');
                text.Append('\n');
            }

            if (section % 7 == 0) text.Append("```csharp\nvar total = 0;\nfor (var at = 0; at < ").Append(section).Append("; at++) total += at;\n```\n\n");
        }

        return text.ToString();
    }

    /// <summary>C# about <paramref name="size"/> characters long: classes of eight small methods each. The same every run.</summary>
    internal static string GeneratedCode(int size)
    {
        var text = new StringBuilder(size + 4096).Append("namespace Generated;\n\n");

        for (var type = 1; text.Length < size; type++)
        {
            text.Append("public sealed class Type").Append(type).Append("\n{\n    private int _value").Append(type).Append(";\n\n");

            for (var method = 1; method <= 8; method++)
            {
                text.Append("    public int Method").Append(method).Append("(int input)\n    {\n")
                    .Append("        var total = input * ").Append(method).Append(";\n")
                    .Append("        for (var at = 0; at < ").Append((type % 7) + 3).Append("; at++) total += at;\n")
                    .Append("        return total + _value").Append(type).Append(";\n    }\n\n");
            }

            text.Append("}\n\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// A short document of prose with a C# fence about <paramref name="fence"/> characters long halfway down it, and where the
    /// fence's code starts and ends — the prose has short fences of its own, so the long one is said rather than searched for.
    /// </summary>
    internal static (string Text, int Start, int End) FencedCode(int fence)
    {
        var prose = GeneratedProse(128 * 1024);
        var half = prose.IndexOf("\n## ", prose.Length / 2, StringComparison.Ordinal) + 1;
        var code = GeneratedCode(fence);
        var start = half + "```csharp\n".Length;

        return (string.Concat(prose.AsSpan(0, half), "```csharp\n".AsSpan(), (code + "```\n\n").AsSpan(), prose.AsSpan(half)), start, start + code.Length);
    }
}
