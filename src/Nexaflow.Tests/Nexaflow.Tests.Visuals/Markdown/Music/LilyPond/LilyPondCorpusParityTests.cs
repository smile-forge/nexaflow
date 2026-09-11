using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Music.LilyPond;

/// <summary>
/// The corpus read twice — each tune as its ABC says, and as abc2ly writes it in LilyPond — both engraved by the
/// one engraver, and the two pictures compared note by note and bar by bar.
///
/// <para>
/// abc2ly is somebody else's reading of the same tune. Where the two engravings agree, both of our readings are
/// probably right; where they part, one of three things is wrong — our ABC reading, our LilyPond reading, or
/// abc2ly, which its own authors do not promise is perfect. So this ranks rather than judges: it says which
/// tunes part and where they first do, and is never asked for a verdict.
/// </para>
/// <para>
/// A note is compared by where its heads sit on its own staff, which is what a wrong octave, a wrong pitch or
/// a wrong clef all move. Stems, beams and spacing are left out on purpose: those are the engraver's, and the
/// engraver is the one thing both readings share.
/// </para>
/// <para>
/// Opt-in and local. <c>NEXAFLOW_LY_PARITY</c> names a folder of abc2ly output (<c>tune_000001.ly</c> …); each
/// is paired with the tune of the same name under <c>NEXAFLOW_ABC_CORPUS</c>, and the report is written beside
/// them as <c>parity.txt</c>. <c>NEXAFLOW_LY_PARITY_SHOW</c> writes a picture of both engravings for the first
/// that many tunes that part, or for the tunes it names.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("opt-in sweep of an external corpus through abc2ly")]
public class LilyPondCorpusParityTests
{
    private const string DefaultCorpus = @"D:\Datasets\abcmusic\zenoob";

    /// <summary>How wide both readings are engraved, so a line break can never be the difference.</summary>
    private const double Width = 900;

    /// <summary>A picture is cut off here: a four-part hymn is thousands of pixels tall, and its first page says why it parted.</summary>
    private const double Tallest = 3000;

    [TestMethod]
    public void TheCorpusReadBothWaysEngravesTheSame() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_LY_PARITY");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Assert.Inconclusive("set NEXAFLOW_LY_PARITY to a folder of abc2ly output");
            return;
        }

        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_CORPUS") is { Length: > 0 } named ? named : DefaultCorpus;
        var tunes = Directory.EnumerateFiles(corpus, "*.abc", SearchOption.AllDirectories)
                             .GroupBy(Path.GetFileNameWithoutExtension)
                             .ToDictionary(g => g.Key!, g => g.First());

        // A number pictures that many of the tunes that part; names picture those tunes.
        var show = Environment.GetEnvironmentVariable("NEXAFLOW_LY_PARITY_SHOW") ?? "";
        var first = int.TryParse(show, out var count) ? count : 0;
        var shown = show.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

        var parted = new List<string>();
        int same = 0, total = 0, repaired = 0;

        foreach (var ly in Directory.EnumerateFiles(folder, "*.ly").Order())
        {
            var name = Path.GetFileNameWithoutExtension(ly);
            if (!tunes.TryGetValue(name, out var abcFile)) continue;

            total++;
            var abc = File.ReadAllText(abcFile);
            var written = File.ReadAllText(ly);
            var read = Repaired(written);
            if (read != written) repaired++;

            if (Compare(abc, read) is not { } where)
            {
                same++;
                continue;
            }

            parted.Add($"{name}\t{where}");
            if (parted.Count <= first || shown.Contains(name)) Picture(abc, read, Path.Combine(folder, $"{name}.png"));
        }

        var report = Path.Combine(folder, "parity.txt");
        File.WriteAllLines(report, [$"{same} of {total} engrave the same (abc2ly's voice names repaired in {repaired})", .. parted]);
        Console.WriteLine($"{same} of {total} engrave the same — {report}");
    });

    /// <summary>Where the two engravings first part company, or null where they never do.</summary>
    private static string? Compare(string abc, string ly)
    {
        Laid one, other;
        try { one = AbcBuilder.Build(abc, Width, Brushes.Black, 1.0); }
        catch (Exception e) { return $"abc threw {e.GetType().Name}: {e.Message}"; }
        try { other = LilyPondBuilder.Build(ly, Width, Brushes.Black, 1.0); }
        catch (Exception e) { return $"ly threw {e.GetType().Name}: {e.Message}"; }

        var (abcNotes, abcBars) = Read(one);
        var (lyNotes, lyBars) = Read(other);
        var trouble = other.Trouble.Count > 0 ? $"\tly marked {other.Trouble.Count}: {other.Trouble[0].Message}" : "";

        for (var at = 0; at < Math.Min(abcNotes.Count, lyNotes.Count); at++)
            if (abcNotes[at] != lyNotes[at])
                return $"note {at + 1} of {abcNotes.Count}: abc [{abcNotes[at]}] ly [{lyNotes[at]}]{trouble}";

        if (abcNotes.Count != lyNotes.Count) return $"notes: abc {abcNotes.Count}, ly {lyNotes.Count}{trouble}";
        if (abcBars != lyBars) return $"bars: abc {abcBars}, ly {lyBars}{trouble}";
        return null;
    }

    /// <summary>
    /// Every note, voice by voice, as where its heads sit below the top of its own staff — and how many bars there
    /// are. A bracketed system is one staff per voice, the same voice in the same place in every system, so the
    /// voices are read out one at a time: two engravings that break their lines in different places, which a part
    /// song written a bar to a line always will, still read the same.
    /// </summary>
    private static (List<string> Notes, int Bars) Read(Laid layout)
    {
        var systems = All(layout.Root, "system");
        var voices = Voices(layout.Root, systems);

        var notes = new List<string>[voices];
        for (var voice = 0; voice < voices; voice++) notes[voice] = [];
        var bars = 0;

        for (var at = 0; at < systems.Count; at++)
        {
            var top = Top(systems[at]);
            bars += All(systems[at], "measure").Count;

            foreach (var note in All(systems[at], "note"))
                notes[at % voices].Add(string.Join(",", All(note, "head").Select(head => (int)Math.Round(head.Bounds.Top - top)).Order()));
        }

        return ([.. notes.SelectMany(voice => voice)], bars);
    }

    /// <summary>How many staves the first bracket joins — one per voice — or one where nothing is bracketed.</summary>
    private static int Voices(Piece root, List<Piece> systems) =>
        All(root, "bracket").FirstOrDefault() is { } bracket
            ? Math.Max(1, systems.Count(system => Top(system) >= bracket.Bounds.Top - 1 && Top(system) <= bracket.Bounds.Bottom + 1))
            : 1;

    /// <summary>Where a system's staff starts: its top line.</summary>
    private static double Top(Piece system) => All(system, "staff-line").Select(line => line.Bounds.Top).DefaultIfEmpty(0).Min();

    /// <summary>
    /// abc2ly's one systematic slip, undone so the sweep can see past it: a tune whose ABC names its voice
    /// (<c>V:1</c>) has that voice defined by letter (<c>"voiceB" = { … }</c>) and used by number
    /// (<c>\"voice1"</c>), and LilyPond itself refuses the file. Each voice used and never defined is pointed at
    /// the voice defined and never used, in the order both appear; a file without the slip comes back as it was.
    /// </summary>
    private static string Repaired(string ly)
    {
        var defined = Regex.Matches(ly, "^\"(voice[^\"]+)\" =", RegexOptions.Multiline)
                           .Select(m => m.Groups[1].Value)
                           .Where(name => name != "voicedefault")
                           .ToList();
        var used = Regex.Matches(ly, "\\\\\"(voice[^\"]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();

        var unused = defined.Where(name => !used.Contains(name)).ToList();
        var missing = used.Where(name => !defined.Contains(name)).ToList();
        if (missing.Count == 0 || missing.Count != unused.Count) return ly;

        for (var at = 0; at < missing.Count; at++)
            ly = ly.Replace($"\\\"{missing[at]}\"", $"\\\"{unused[at]}\"");

        return ly;
    }

    private static List<Piece> All(Piece root, string kind) => [.. root.SelfAndDescendants().Where(p => p.Kind == kind)];

    /// <summary>
    /// One tune's two engravings, the ABC above the LilyPond, written beside the report. A line in the report
    /// says where two readings part; the picture says why, and is how the parting is actually read.
    /// </summary>
    private static void Picture(string abc, string ly, string file)
    {
        var one = Engraved(abc, (source, room, ppd) => AbcBuilder.Build(source, room, Brushes.Black, ppd));
        var other = Engraved(ly, (source, room, ppd) => LilyPondBuilder.Build(source, room, Brushes.Black, ppd));

        var height = one.PixelHeight + other.PixelHeight + 24;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Width, height));
            dc.DrawImage(one, new Rect(0, 0, one.PixelWidth, one.PixelHeight));
            dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, one.PixelHeight + 11, Width, 2));
            dc.DrawImage(other, new Rect(0, one.PixelHeight + 24, other.PixelWidth, other.PixelHeight));
        }

        var both = new RenderTargetBitmap((int)Width, height, 96, 96, PixelFormats.Pbgra32);
        both.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(both));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    /// <summary>A source engraved as the page shows it, at the sweep's width.</summary>
    private static RenderTargetBitmap Engraved(string source, Func<string, double, double, Laid> build)
    {
        var element = new Nexaflow.Visuals.Text.Editing.ContentElement(source, MarkdownPalette.Light, (state, room, ppd) => build(state.Source, room, ppd));
        element.Measure(new Size(Width, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(Math.Min(Width, element.DesiredSize.Width))),
            Math.Max(1, (int)Math.Ceiling(Math.Min(Tallest, element.DesiredSize.Height))),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }
}
