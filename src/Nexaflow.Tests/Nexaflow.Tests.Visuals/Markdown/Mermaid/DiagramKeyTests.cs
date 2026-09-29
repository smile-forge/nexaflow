using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Every key a reader presses, at every place the caret can rest inside a diagram of a document that holds it — through the
/// engine and with the fallback its real caller uses — and afterwards the diagram still reads.
///
/// <para>
/// The measure this area has never had. Typing one character at the end of a drawn run says almost nothing about editing: most
/// of what a reader presses is Enter, a space and backspace, and a caret rests anywhere the layout offers a stop rather than
/// only where words were drawn.
/// </para>
/// <para>
/// <strong>The control is part of the test.</strong> Whether a diagram still reads is asked of the mermaid parser, per
/// diagram, because reading a document does not read what is written inside a block in another language — that happens when a
/// builder asks — so a document's own tree says nothing about whether a diagram in it broke. A measure that cannot see
/// breakage reports none, so one press here bypasses the engine and splices a quote raw, and the count it breaks is asserted
/// to be large. Without it every number below could be zero for the wrong reason.
/// </para>
/// <para>
/// <strong>A floor, not a target.</strong> Each count is what stands today and may only fall. A number that rises is a
/// regression and names the places and the part each landed on; one that falls fails too, so the floor comes down with it.
/// <c>Changed</c> is reported rather than asserted, because the right value is neither zero nor everything: a key pressed
/// where the diagram has nothing written should leave the source alone, and one pressed in a name should not.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("mermaid-diagram-kit")]
public class DiagramKeyTests
{
    private const double Room = 700;

    /// <summary>The press that proves the measure can see a diagram break at all, and how little of it may go unseen.</summary>
    private const string Control = "a quote spliced raw";

    /// <summary>How many stops each key leaves a diagram its own grammar can no longer read. Only ever revised downwards.</summary>
    private static readonly (string Key, int Broke)[] Floor =
    [
        ("type a", 416),
        ("space", 191),
        ("enter", 2487),
        ("backspace", 802),
    ];

    [TestMethod]
    public void NoKeyPressedInADiagramLeavesALineItCannotRead() => UiThread.Run(() =>
    {
        var stops = 0;
        var changed = new Dictionary<string, int>();
        var broke = new Dictionary<string, List<string>>();

        foreach (var path in TestSampleData.Files("markdown")
                     .Where(path => Path.GetFileName(path).StartsWith("mermaid-", StringComparison.Ordinal)))
        {
            var what = Path.GetFileName(path);
            var document = File.ReadAllText(path).Replace("\r\n", "\n");
            var laid = new ContentEngine().Lay(null, EditState.For(document), StyleFormat.Dark, Room, readOnly: false);
            var held = Held(document);

            // Only the stops inside a diagram: what a key does in the document's own prose is a different question.
            var inside = Diagrams(document);

            foreach (var (place, index) in laid.Places.Select((place, index) => (place, index))
                         .Where(one => inside.Any(span => one.place.Offset >= span.Start && one.place.Offset <= span.End)))
            {
                var at = new EditState(document, place.Offset);

                // The place the caret is at, not only the offset it is at: which piece it stands against is what says whether
                // anything was written there, and two pieces can be drawn from the same stretch of source.
                var landing = new Landing(at, laid, index);
                stops++;

                // What the real caller does: the language's answer, and failing that what the key does to the characters.
                Press("type a", () => ContentEngine.Edited(EditKind.Typing, "a", landing) ?? at.Write("a"));
                Press("space", () => ContentEngine.Edited(EditKind.Settling, " ", landing) ?? at.Write(" "));
                Press("enter", () => ContentEngine.Edited(EditKind.Settling, "\n", landing) ?? at.Write("\n"));
                Press("backspace", () => ContentEngine.Edited(EditKind.Erasing, string.Empty, landing) ?? at.Backspace());
                Press(Control, () => at.Write("\""));

                void Press(string key, Func<EditState> press)
                {
                    EditState after;
                    try { after = press(); }
                    catch (Exception trouble) { Blame(key, $"{what} @{place.Offset}: {trouble.GetType().Name}"); return; }

                    if (after.Source != at.Source) changed[key] = changed.GetValueOrDefault(key) + 1;
                    if (Held(after.Source) > held)
                        Blame(key, $"{what} @{place.Offset} {Standing(laid, place.Offset)}: '{Line(after.Source, place.Offset)}'");
                }

                void Blame(string key, string said) =>
                    (broke.TryGetValue(key, out var told) ? told : broke[key] = []).Add(said);
            }
        }

        Assert.IsTrue(stops > 500, $"only {stops} stop(s) were pressed at — are the samples being laid out?");
        Assert.IsTrue(broke.GetValueOrDefault(Control)?.Count > 1000,
            $"{Control} broke only {broke.GetValueOrDefault(Control)?.Count ?? 0} of {stops} stop(s) — the measure has stopped "
            + "seeing a diagram break, so every count below is meaningless");

        var risen = Floor.Where(floor => broke.GetValueOrDefault(floor.Key)?.Count > floor.Broke).ToList();
        var fallen = Floor.Where(floor => (broke.GetValueOrDefault(floor.Key)?.Count ?? 0) < floor.Broke).ToList();

        Assert.AreEqual(0, risen.Count,
            $"across {stops} caret stops inside diagrams, more keys now leave one unreadable:\n"
            + string.Join("\n", risen.Select(floor =>
                $"  {floor.Key}: {broke[floor.Key].Count}, floor says {floor.Broke} — changed the source at {changed.GetValueOrDefault(floor.Key)} stop(s)\n    "
                + string.Join("\n    ", broke[floor.Key].Take(8)))));

        Assert.AreEqual(0, fallen.Count,
            "fewer keys break a diagram than the floor says — bring it down:\n"
            + string.Join("\n", fallen.Select(floor => $"  {floor.Key}: {broke.GetValueOrDefault(floor.Key)?.Count ?? 0}, floor says {floor.Broke}")));
    });

    /// <summary>Where each diagram's own characters stand in <paramref name="document"/>.</summary>
    private static List<(int Start, int End)> Diagrams(string document) =>
        [.. new ContentEngine().Read(null, document).Root.SelfAndDescendants()
                .Where(part => ContentNested.Language(part) == "mermaid" && part.Part(Roles.Body) is not null)
                .Select(part => ContentNested.Own(part.Part(Roles.Body)!))
                .Select(span => (span.Start, span.Start + span.Length))];

    /// <summary>
    /// What the caret at <paramref name="offset"/> stands against, found the way the engine finds it — so a count that has
    /// risen says which parts to look at rather than only which lines broke.
    /// </summary>
    private static string Standing(Laid laid, int offset)
    {
        var piece = laid.Root.WordsAt(offset);
        var words = piece.Exists;

        if (!words)
            piece = laid.Root.SelfAndDescendants()
                .Where(one => one.Part is { } part && part.Start <= offset && offset <= part.End())
                .DefaultIfEmpty(laid.Root)
                .MaxBy(one => one.Depth);

        for (var up = piece; up.Exists; up = up.Parent)
            if (Of(up.Part) is { } part)
                return $"[{part.Kind}/{part.Role} len={part.Length} words={words}]";

        return "[nothing was drawn from anything written]";

        static ContentPart? Of(ISourcePart? part) => part switch
        {
            ContentPart own => own,
            IStandsFor standing => Of(standing.Of),
            _ => null,
        };
    }

    /// <summary>The line <paramref name="at"/> is on.</summary>
    private static string Line(string source, int at)
    {
        var start = source.LastIndexOf('\n', Math.Clamp(at - 1, 0, Math.Max(0, source.Length - 1))) + 1;
        var end = source.IndexOf('\n', Math.Clamp(start, 0, source.Length));

        return source[start..(end < 0 ? source.Length : end)].Trim();
    }

    /// <summary>
    /// How many stretches of the diagrams in <paramref name="source"/> their own grammar holds as written, unable to read them.
    /// Parsed by the mermaid parser per diagram — see the control, above.
    /// </summary>
    private static int Held(string source)
    {
        var trouble = 0;

        foreach (var (start, end) in Diagrams(source))
            trouble += MermaidParser.Parse(source[start..end]).SelfAndDescendants()
                           .Count(node => node is { Kind: Kinds.Verbatim, Trouble: not null });

        return trouble;
    }
}
