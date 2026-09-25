using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Every sample document lays out exactly as it did when the snapshots were written: every piece, where it stands, what it
/// stands for, and every mark it draws with its colour — read-only and being written in.
///
/// <para>
/// For work that must change how the layout is reached and nothing about what it is. Opt-in, because what text measures to
/// depends on the fonts a machine has: point <c>NEXAFLOW_LAYOUT_SNAPSHOTS</c> at a folder, write it once with
/// <c>NEXAFLOW_LAYOUT_SNAPSHOTS_WRITE=1</c> before the change, and run it after every step of it.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("whole-corpus layout guard; maps to no single product node")]
public class LayoutSnapshotTests
{
    private const string Folder = "NEXAFLOW_LAYOUT_SNAPSHOTS";
    private const string Write = "NEXAFLOW_LAYOUT_SNAPSHOTS_WRITE";
    private const double Room = 700;

    [TestMethod]
    public void EverySampleLaysAsItDidBefore() => UiThread.Run(() =>
    {
        if (Environment.GetEnvironmentVariable(Folder) is not { Length: > 0 } folder)
        {
            Assert.Inconclusive($"Set {Folder} to a folder of layout snapshots to run this.");
            return;
        }

        var writing = Environment.GetEnvironmentVariable(Write) == "1";
        Directory.CreateDirectory(folder);

        var documents = TestSampleData.Files("markdown").ToList();
        documents.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        var moved = new List<string>();
        var compared = 0;

        foreach (var path in documents)
        {
            var text = File.ReadAllText(path);

            foreach (var readOnly in new[] { true, false })
            {
                var name = $"{Path.GetFileNameWithoutExtension(path)}.{(readOnly ? "read" : "written")}.txt";
                var file = Path.Combine(folder, name);
                var now = Dump(Lay(text, readOnly));

                if (writing)
                {
                    File.WriteAllText(file, now);
                    continue;
                }

                if (!File.Exists(file))
                {
                    moved.Add($"{name}: no snapshot");
                    continue;
                }

                compared++;
                if (Differs(File.ReadAllText(file), now) is { } where) moved.Add($"{name}: {where}");
            }
        }

        if (writing)
        {
            Assert.Inconclusive($"Wrote {documents.Count * 2} snapshots to {folder}.");
            return;
        }

        Assert.AreEqual(0, moved.Count, $"{moved.Count} of {compared} laid differently:\n" + string.Join("\n", moved));
    });

    /// <summary>The document laid out the way a surface lays it — the one line to change when that way changes.</summary>
    private static Laid Lay(string text, bool readOnly)
    {
        var style = StyleFormat.Dark;
        var options = new DiagramRenderOptions { Palette = style, ReadOnly = readOnly };

        return MarkdownContent.Of(style, new ContentEngine(options)).Lay(EditState.For(text), Room, readOnly);
    }

    private static string Dump(Laid laid)
    {
        var text = new StringBuilder();
        text.Append("size ").Append(Size(laid.Size)).Append('\n');

        foreach (var piece in laid.Root.SelfAndDescendants())
        {
            text.Append(' ', piece.Depth * 2).Append(piece.Kind);

            // The line at today is drawn from the clock, which moves between the snapshot and the run.
            if (piece.Kind != Nexaflow.Visuals.Text.Markdown.Mermaid.Gantt.GanttPiece.Today)
                text.Append(' ').Append(Rect(piece.Bounds));

            if (piece.Part is { } part) text.Append(" @").Append(part.Start).Append('+').Append(part.Length).Append(' ').Append(part.GetType().Name);
            if (piece.Words is { } words) text.Append(" words ").Append(words.Length);
            if (piece.Turned is { } turned && !turned.Value.IsIdentity) text.Append(" turned ").Append(turned.Value.ToString(CultureInfo.InvariantCulture));
            text.Append('\n');

            if (piece.Kind == Nexaflow.Visuals.Text.Markdown.Mermaid.Gantt.GanttPiece.Today) continue;

            foreach (var mark in piece.Marks)
                text.Append(' ', piece.Depth * 2 + 2).Append(Mark(mark)).Append('\n');
        }

        foreach (var place in laid.Places)
            text.Append("place ").Append(place.Offset).Append(place.Trailing ? " trailing" : "").Append('\n');

        foreach (var trouble in laid.Trouble.OrderBy(trouble => trouble.Start).ThenBy(trouble => trouble.Message, StringComparer.Ordinal))
            text.Append("trouble ").Append(trouble.Start).Append('+').Append(trouble.Length).Append(' ').Append(trouble.Message).Append('\n');

        return text.ToString();
    }

    /// <summary>A mark as what it is, where it covers, and what it is drawn with.</summary>
    private static string Mark(LayoutMark mark)
    {
        var said = new StringBuilder(mark.GetType().Name).Append(' ').Append(Rect(mark.Covers));

        foreach (var property in mark.GetType().GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property.GetIndexParameters().Length > 0) continue;

            var value = property.PropertyType == typeof(Brush) || property.PropertyType == typeof(Pen)
                        || property.PropertyType == typeof(FormattedText) || property.PropertyType == typeof(string)
                ? property.GetValue(mark)
                : null;

            switch (value)
            {
                case Brush brush: said.Append(' ').Append(property.Name).Append('=').Append(Paint(brush)); break;
                case Pen pen: said.Append(' ').Append(property.Name).Append('=').Append(Paint(pen.Brush)).Append('/').Append(Number(pen.Thickness)); break;
                case FormattedText formatted: said.Append(' ').Append(property.Name).Append("=\"").Append(formatted.Text.Replace("\n", "\\n")).Append('"'); break;
                case string words: said.Append(' ').Append(property.Name).Append("=\"").Append(words.Replace("\n", "\\n")).Append('"'); break;
            }
        }

        return said.ToString();
    }

    private static string Paint(Brush? brush) => brush switch
    {
        null => "none",
        SolidColorBrush solid => solid.Color.ToString(CultureInfo.InvariantCulture) + (solid.Opacity < 1 ? "*" + Number(solid.Opacity) : ""),
        _ => brush.GetType().Name,
    };

    private static string Rect(Rect rect) =>
        rect.IsEmpty ? "empty" : $"[{Number(rect.X)},{Number(rect.Y)} {Number(rect.Width)}x{Number(rect.Height)}]";

    private static string Size(Size size) => $"{Number(size.Width)}x{Number(size.Height)}";

    private static string Number(double value) =>
        double.IsFinite(value) ? Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Where two dumps first part, with the lines either side — or null where they agree.</summary>
    private static string? Differs(string before, string now)
    {
        if (before == now) return null;

        var was = before.Split('\n');
        var is_ = now.Split('\n');
        var at = 0;
        while (at < was.Length && at < is_.Length && was[at] == is_[at]) at++;

        return $"line {at + 1} of {was.Length}: was `{(at < was.Length ? was[at] : "(end)")}`, now `{(at < is_.Length ? is_[at] : "(end)")}`";
    }
}
