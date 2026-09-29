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
/// Every key a reader presses, at every place the caret can rest in every sample diagram, through the engine and with the
/// fallback its real caller uses — and afterwards the diagram still reads.
///
/// <para>
/// The measure this area has never had. Typing one character at the end of a drawn run says almost nothing about editing: most
/// of what a reader presses is Enter, a space, backspace and the arrows, and a caret can rest anywhere the layout offers a
/// stop rather than only where words were drawn. So this presses what they press, where they can be.
/// </para>
/// <para>
/// <strong>A floor, not a target.</strong> Each count below is what stands today and may only fall. A number that rises is a
/// regression and names the places; one that falls is progress and the floor comes down with it, which is what keeps the
/// figure honest rather than a decoration.
/// </para>
/// <para>
/// <c>Changed</c> is reported and not asserted, because what it should be is not zero and not everything: a key pressed where
/// the diagram has nothing written should leave the source alone, and one pressed in a name should not. It is here so that
/// number moving is visible while the engine learns the difference.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("mermaid-diagram-kit")]
public class DiagramKeyTests
{
    /// <summary>How many stops each key currently leaves a line the diagram can no longer read. Only ever revised downwards.</summary>
    private static readonly (string Key, int Broke)[] Floor =
    [
        ("type a", 416),
        ("space", 191),
        ("enter", 2487),
        ("backspace", 806),
        ("tab", 0),
    ];

    [TestMethod]
    public void NoKeyPressedAnywhereInADiagramLeavesALineItCannotRead() => UiThread.Run(() =>
    {
        var stops = 0;
        var changed = new Dictionary<string, int>();
        var broke = new Dictionary<string, List<string>>();

        foreach (var path in TestSampleData.Files("markdown")
                     .Where(path => Path.GetFileName(path).StartsWith("mermaid-", StringComparison.Ordinal)))
        {
            var document = File.ReadAllText(path).Replace("\r\n", "\n");

            foreach (var holder in new ContentEngine().Read(null, document).Root.SelfAndDescendants()
                         .Where(part => ContentNested.Language(part) == "mermaid"))
            {
                var source = ContentNested.Own(holder.Part(Roles.Body)!.Node);
                var laid = new ContentEngine().Lay("mermaid", EditState.For(source), StyleFormat.Dark, 700, readOnly: false);
                var held = Held(source);
                var what = Path.GetFileName(path);

                foreach (var place in laid.Places)
                {
                    var at = new EditState(source, place.Offset);
                    stops++;

                    // What the real caller does: the language's answer, and failing that what the key does to characters.
                    Press("type a", () => Edited(EditKind.Typing, "a", at, laid) ?? at.Write("a"));
                    Press("space", () => Edited(EditKind.Settling, " ", at, laid) ?? at.Write(" "));
                    Press("enter", () => Edited(EditKind.Settling, "\n", at, laid) ?? at.Write("\n"));
                    Press("backspace", () => Edited(EditKind.Erasing, string.Empty, at, laid) ?? at.Backspace());
                    Press("tab", () => Edited(EditKind.Tabbing, string.Empty, at, laid) ?? at);

                    void Press(string key, Func<EditState> press)
                    {
                        EditState after;
                        try { after = press(); }
                        catch (Exception trouble) { Blame(key, $"{what} @{place.Offset}: {trouble.GetType().Name}"); return; }

                        if (after.Source != at.Source) changed[key] = changed.GetValueOrDefault(key) + 1;
                        if (Held(after.Source) > held) Blame(key, $"{what} @{place.Offset}: '{Line(after.Source, place.Offset)}'");
                    }

                    void Blame(string key, string said) =>
                        (broke.TryGetValue(key, out var told) ? told : broke[key] = []).Add(said);
                }
            }
        }

        Assert.IsTrue(stops > 1000, $"only {stops} stop(s) were pressed at — are the samples being laid out?");

        var risen = Floor.Where(floor => broke.GetValueOrDefault(floor.Key)?.Count > floor.Broke).ToList();
        var fallen = Floor.Where(floor => (broke.GetValueOrDefault(floor.Key)?.Count ?? 0) < floor.Broke).ToList();

        Assert.AreEqual(0, risen.Count,
            $"across {stops} caret stops, more keys now leave a line the diagram cannot read:\n"
            + string.Join("\n", risen.Select(floor =>
                $"  {floor.Key}: {broke[floor.Key].Count}, was {floor.Broke} — changed the source at {changed.GetValueOrDefault(floor.Key)} stop(s)\n    "
                + string.Join("\n    ", broke[floor.Key].Take(6)))));

        Assert.AreEqual(0, fallen.Count,
            "fewer keys break a diagram than the floor says — bring it down:\n"
            + string.Join("\n", fallen.Select(floor => $"  {floor.Key}: {broke.GetValueOrDefault(floor.Key)?.Count ?? 0}, floor says {floor.Broke}")));
    });

    /// <summary>What the engine makes of a key pressed at <paramref name="at"/>, or null where it leaves it to the characters.</summary>
    private static EditState? Edited(EditKind kind, string text, EditState at, Laid laid) =>
        ContentEngine.Edited(kind, text, new Landing(at, laid, -1));

    /// <summary>The line <paramref name="at"/> is on.</summary>
    private static string Line(string source, int at)
    {
        var start = source.LastIndexOf('\n', Math.Clamp(at - 1, 0, Math.Max(0, source.Length - 1))) + 1;
        var end = source.IndexOf('\n', Math.Clamp(start, 0, source.Length));

        return source[start..(end < 0 ? source.Length : end)].Trim();
    }

    /// <summary>How many lines of a block its grammar holds as written, rather than reading them.</summary>
    private static int Held(string source) =>
        MermaidParser.Parse(source).SelfAndDescendants().Count(node => node is { Kind: Kinds.Verbatim, Trouble: not null });
}
