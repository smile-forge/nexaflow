using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The content laid a unit at a time: each unit of its reading worked over by its language's stages from its own tree, and laid
/// from that on its own, kept while what it was worked over and laid with stands — so a keystroke works over and lays the unit
/// it fell in, and every other unit is set down again where it now stands.
///
/// <para>
/// <strong>What one unit says reaches the others through the engine.</strong> What a unit defines is gathered into the
/// document's link targets, in order, and a unit's stages are answered from those, every question remembered — so a definition
/// that changes what it means works over again only the units that asked about it. What a unit's blocks come to across the
/// document is the outline's (<see cref="ContentOutline"/>), and which diagram is which, for what the reader opened in each, is
/// counted unit by unit.
/// </para>
/// <para>
/// <strong>The document is its units.</strong> What is laid is a document piece holding one mount per unit, the gap at each seam
/// said by the language from the blocks either side; what reads the document as blocks asks the unit holding the place, through a
/// reading of that unit placed where it starts.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>The units laid, each as it was worked over, with where it starts — in order.</summary>
    private List<(StagedUnit Unit, int Start)> _laidUnits = [];

    /// <summary>The version of the document the units were last laid from.</summary>
    private ContentDocument _laidIn = ContentDocument.Of(string.Empty);

    /// <summary>How many times what the units define has come to mean something else — what a unit kept was last held to.</summary>
    private int _defined;

    /// <summary>What the units defined, as it means, when it last changed.</summary>
    private string _definedMeant = string.Empty;

    /// <summary>The places in each unit's reading where a diagram's body starts, in order, by the unit's tree.</summary>
    private static readonly ConditionalWeakTable<ContentNode, int[]> DiagramsIn = new();

    /// <summary>
    /// Brings what each unit of <paramref name="units"/> was worked over into up to date: a unit whose tree, showing and the
    /// answers it was given stand is kept, and every other is worked over again, the blocks of those it replaces handed to
    /// <see cref="WithUnchanged"/> first. What the units define is gathered into the document's link targets, and a unit kept that
    /// asked about a definition now meaning something else is worked over again.
    /// </summary>
    private void Stage(ContentLanguage language, List<(ContentUnit Unit, int Start)> units, ContentShowing showing, ContentDocument document)
    {
        var was = new Dictionary<ContentNode, StagedUnit>(ReferenceEqualityComparer.Instance);
        foreach (var (staged, _) in _laidUnits) was.TryAdd(staged.Read, staged);

        var now = new StagedUnit?[units.Count];
        var leaving = new List<StagedUnit>();

        for (var index = 0; index < units.Count; index++)
        {
            var (unit, start) = units[index];
            if (!was.Remove(unit.Tree, out var kept)) continue;

            if (kept.Key == this.KeyFor(showing, start, unit.Width) && !kept.Stale) now[index] = kept;
            else leaving.Add(kept);
        }

        leaving.AddRange(was.Values);

        // Blocks of the units leaving are what the blocks of the units replacing them are matched against — none, once told that
        // nothing laid is to be set down as it was.
        if (!_unkept)
            foreach (var gone in leaving)
                if (gone.Key.Forgotten == _forgotten) _unchanged.Leaving(gone.Tree);

        _unkept = false;

        for (var index = 0; index < units.Count; index++)
            now[index] ??= this.Staged(language, units[index].Unit, units[index].Start, showing, document);

        // What the units define, gathered — and a unit that asked about something now meaning otherwise, worked over again. A unit
        // defines what its own characters say, so working one over again changes nothing it defines, and once is enough.
        this.Defined(units, now);

        for (var index = 0; index < units.Count; index++)
            {
                var staged = now[index]!;
                if (staged.Defined == _defined) continue;

                if (staged.Targets.Answered())
                {
                    staged.Defined = _defined;
                    continue;
                }

                _unchanged.Leaving(staged.Tree);
                now[index] = this.Staged(language, units[index].Unit, units[index].Start, showing, document);
            }

        _unchanged.Settle();

        _laidUnits = [.. now.Select((staged, index) => (staged!, units[index].Start))];
        _laidIn = document;
    }

    /// <summary>
    /// <paramref name="unit"/>'s own tree, starting at <paramref name="start"/> in <paramref name="document"/>, worked over by its
    /// language's stages: offsets the unit's own, the showing standing where it starts, and link targets of its own over the
    /// document's.
    /// </summary>
    private StagedUnit Staged(ContentLanguage language, ContentUnit unit, int start, ContentShowing showing, ContentDocument document)
    {
        var tree = Bound(language, unit.Tree);
        var targets = new LinkTargets(_targets);
        var own = showing with { At = start, Targets = targets };

        var stages = language.Stages(tree, own).OfType<IAstStage>()
                             .Select(stage => stage is ISlowStage slow ? Staging(language, document.Stretch(start, unit.Width), slow) : stage)
                             .ToList();

        var worked = stages.Count == 0 ? tree : new AstPipeline(stages).Run(tree);

        return new StagedUnit(unit.Tree, worked, unit.Width, this.KeyFor(showing, start, unit.Width), targets, _defined,
                              OutlineUnit.Of(worked.Children, unit.Width));
    }

    /// <summary>
    /// Gathers what the units define into the document's link targets, in order, each where it now stands — counting it as a change
    /// to what the units define where it now means something it did not.
    /// </summary>
    private void Defined(List<(ContentUnit Unit, int Start)> units, StagedUnit?[] now)
    {
        if (!now.Any(staged => staged!.Targets.Reported)) return;

        var defined = new List<LinkTarget>();
        var reach = 0;

        for (var index = 0; index < units.Count; index++)
        {
            var (unit, start) = units[index];
            foreach (var target in now[index]!.Targets.Defined) defined.Add(target with { At = target.At + start });

            reach = start + unit.Width;
        }

        _targets.Report(reach, defined);

        var meant = string.Join('\n', defined.Select(target => $"{target.Kind}\t{target.Name}\t{target.Meaning}\t{target.Title}"));
        if (meant != _definedMeant) (_definedMeant, _defined) = (meant, _defined + 1);
    }

    /// <summary>What a unit starting at <paramref name="start"/>, <paramref name="width"/> long, is worked over with.</summary>
    private StagingKey KeyFor(ContentShowing showing, int start, int width) =>
        new(showing.Style, showing.Writing,
            showing.Shown is { } zone && zone.Start <= start + width && start <= zone.End ? zone with { Start = zone.Start - start, End = zone.End - start } : null,
            _forgotten);

    /// <summary>
    /// Lays out every unit laid whose laying no longer stands, and puts them together: a document piece holding each unit's tree,
    /// mounted where its unit now starts, the gap at each seam said by the language. One unit standing where it was laid is the
    /// whole of what was laid.
    /// </summary>
    private Laid Composed(ContentLanguage language, ContentShowing showing, double room, bool readOnly, Keeping holding)
    {
        // A unit with no block of its own to show — trivia between blocks, a document's closing marks — is laid only where it is
        // the whole of the content, which shows something whatever it holds.
        bool Shows(StagedUnit staged) => _laidUnits.Count == 1 || staged.Outline.Blocks > 0;

        foreach (var (staged, start) in _laidUnits)
        {
            if (!Shows(staged)) continue;

            var folds = this.Folds(start, staged.Width);
            if (staged.Laid is { } kept && kept.Room == room && kept.ReadOnly == readOnly && kept.Folds == folds) continue;

            var reading = staged.Placed(start, _laidIn);
            _readings.AddOrUpdate(staged.Tree, holding.Address);

            staged.Laid = new LaidUnit(Builder(language, reading, showing, LayoutStanding.Alone, holding, Folding).Lay(room), start, reading.Root,
                                       room, readOnly, folds);
        }

        if (_laidUnits is [var (only, at)] && only.Laid!.Start == at) return only.Laid.Laid;

        var into = new LayoutBuilder(WpfMedium.OnThisThread, _laidUnits.Count + 1);
        // Standing for the whole document, as the root of the reading laid whole stands for it.
        into.Open(LayoutPieces.Document, new SourceSpan(0, _laidIn.Length), stops: LayoutStops.None);

        var (y, reach, above) = (0.0, 0.0, (ContentPart?)null);
        var trouble = new List<LayoutDiagnostic>();

        foreach (var (staged, start) in _laidUnits)
        {
            if (!Shows(staged) || staged.Laid is not { } laid) continue;
            // A unit that laid none of its blocks — all trivia, definitions, or folded away — takes no room, whatever least size its
            // builder gave what it laid; a language with a seam says which blocks a unit laid (Laid.Ends).
            if (!laid.Laid.Exists || (language.Seam is not null && laid.Laid.Ends.First is null)) continue;

            if (above is not null && laid.Laid.Ends.First is { } first && language.Seam is { } seam) y += seam(above, first, showing.Style);

            var by = start - laid.Start;
            var mount = by == 0 ? new LayoutMount(laid.Laid.Tree) : new LayoutMount(laid.Laid.Tree, laid.Root, by);
            into.Mount(mount, new LayoutPoint(0, y));

            foreach (var said in laid.Laid.Trouble)
                trouble.Add(by == 0 ? said : said with { Start = said.Start + by, Part = mount.Restated(said.Part) });

            (y, reach) = (y + laid.Laid.Height, Math.Max(reach, laid.Laid.Width));
            above = laid.Laid.Ends.Last ?? above;
        }

        into.Close();

        return new Laid(into.Seal(), Math.Max(reach, 1), Math.Max(y, 1), trouble);
    }

    /// <summary>
    /// What the reader's folding says of a unit starting at <paramref name="start"/>, <paramref name="width"/> long: each folded
    /// section reaching into it, where in the unit, and how many blocks wait behind a heading of its own — what laying it again turns on.
    /// </summary>
    private string Folds(int start, int width)
    {
        var end = start + width;
        var said = new List<string>();

        foreach (var section in _folded)
            if (section.Start < end && section.End > start)
                said.Add($"{Math.Max(section.Start, start) - start}:{Math.Min(section.End, end) - start}:{section.Name}:{(section.Start >= start ? section.Behind : -1)}");

        return string.Join('|', said);
    }

    /// <summary>
    /// Numbers what the reader opened in each diagram in the order the diagrams are written, a unit at a time — or, for content that
    /// is itself a diagram, takes what was opened in it.
    /// </summary>
    private void Views(ContentLanguage language, List<(ContentUnit Unit, int Start)> units)
    {
        _views.Clear();
        _opened.Rewind();

        _top = language == ContentLanguages.Markdown ? null : _opened.Next();
        if (_top is not null) return;

        foreach (var (unit, start) in units)
            foreach (var at in DiagramsIn.GetValue(unit.Tree, static tree => Diagrams(tree)))
                _views[start + at] = _opened.Next();
    }

    /// <summary>Where in <paramref name="tree"/>, a unit's reading, each diagram's body starts, in order.</summary>
    private static int[] Diagrams(ContentNode tree)
    {
        var found = new List<int>();
        Held(tree, 0);

        return [.. found];

        void Held(ContentNode node, int at)
        {
            if (ContentNested.Language(node) is { } language)
            {
                if (!ContentLanguages.Reads(language)) return;

                foreach (var child in node.Children)
                {
                    if (child.Kind == Kinds.Nested) { found.Add(at); return; }
                    at += child.Width;
                }

                return;
            }

            foreach (var child in node.Children)
            {
                Held(child, at);
                at += child.Width;
            }
        }
    }

    // ── The document as its units ───────────────────────────────────────────

    /// <summary>The units laid, each as a reading of it placed where it starts, in order.</summary>
    internal IEnumerable<ContentPart> Readings => _laidUnits.Select(laid => laid.Unit.Placed(laid.Start, _laidIn).Root);

    /// <summary>The reading of the unit laid holding <paramref name="offset"/>, placed where it starts — or null where nothing is laid.</summary>
    private ContentPart? UnitHolding(int offset) => this.UnitAt(offset) is var index and >= 0 ? this.PlacedAt(index) : null;

    /// <summary>Which of the units laid holds <paramref name="offset"/> — the last starting at or before it — or -1 where none is laid.</summary>
    private int UnitAt(int offset)
    {
        if (_laidUnits.Count == 0) return -1;

        var (low, high) = (0, _laidUnits.Count - 1);

        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_laidUnits[middle].Start <= offset) low = middle;
            else high = middle - 1;
        }

        return low;
    }

    /// <summary>The reading of the unit laid at <paramref name="index"/>, placed where it starts.</summary>
    private ContentPart PlacedAt(int index) => _laidUnits[index].Unit.Placed(_laidUnits[index].Start, _laidIn).Root;

    /// <summary>The content's own blocks laid, starting from <paramref name="from"/> up to <paramref name="to"/>, in order across its units.</summary>
    private IEnumerable<ContentPart> Tops(int from = 0, int to = int.MaxValue)
    {
        foreach (var (staged, start) in _laidUnits)
        {
            if (start >= to) yield break;
            if (start + staged.Width <= from && staged.Width > 0) continue;

            foreach (var part in Inside(staged.Placed(start, _laidIn).Root))
                if (part.Start >= from && part.Start < to) yield return part;
        }
    }

    /// <summary>The written block — not trivia, nothing derived — that <paramref name="offset"/> is in, or the one starting there.</summary>
    private ContentPart? WrittenAt(int offset)
    {
        var index = this.UnitAt(offset);
        if (index < 0) return null;

        // The block it is in is in the unit holding it — or, where it stands at that unit's very end, the next unit's first.
        for (var at = index; at < _laidUnits.Count && at <= index + 1; at++)
            foreach (var block in Inside(this.PlacedAt(at)))
                if (block.End > offset) return block.Start <= offset ? block : null;

        return null;
    }

    /// <summary>The part of the content's own — trivia included — that <paramref name="offset"/> is in.</summary>
    private ContentPart? PartHolding(int offset) =>
        this.UnitHolding(offset)?.Children.FirstOrDefault(part => !part.Derived && offset >= part.Start && offset < part.End);

    /// <summary>The root of the reading <paramref name="part"/> is a part of.</summary>
    private static ContentPart RootOf(ContentPart part)
    {
        var root = part;
        while (root.Parent is { } holder) root = holder;

        return root;
    }

    /// <summary>
    /// Says that something the characters do not say has changed about the blocks <paramref name="which"/> picks out: each is laid
    /// afresh, and the unit holding it laid again however it was kept — and worked over again, where what changed is what a stage
    /// reads into it (<paramref name="reread"/>: what a binding supplies).
    /// </summary>
    private void Unkeep(Func<ContentNode, bool> which, bool reread = false)
    {
        foreach (var (staged, _) in _laidUnits)
        {
            var forgot = _unchanged.Forget(staged.Tree, which);
            if (!forgot && !(reread && staged.Tree.Children.Any(which))) continue;

            staged.Laid = null;
            staged.Stale |= reread;
        }
    }

    // ── What is kept ────────────────────────────────────────────────────────

    /// <summary>What a unit is worked over with, besides its tree: if any of it changes, the unit is worked over again.</summary>
    /// <param name="Style">What it is drawn in.</param>
    /// <param name="Writing">Whether somebody is writing in it.</param>
    /// <param name="Shown">The stretch shown as written within it, counted from where it starts — null where none reaches it.</param>
    /// <param name="Forgotten">How many times the engine was told nothing laid is to be set down as it was.</param>
    private readonly record struct StagingKey(LayoutStyle Style, bool Writing, RawZone? Shown, int Forgotten);

    /// <summary>A unit as its language's stages worked it over, kept by the tree it was read into, with what it was laid into.</summary>
    private sealed class StagedUnit(ContentNode read, ContentNode tree, int width, StagingKey key, LinkTargets targets, int defined, OutlineUnit outline)
    {
        /// <summary>Its tree as read — what it is kept by.</summary>
        public ContentNode Read { get; } = read;

        /// <summary>Its tree worked over, its offsets its own.</summary>
        public ContentNode Tree { get; } = tree;

        /// <summary>How many characters it stands for.</summary>
        public int Width { get; } = width;

        public StagingKey Key { get; } = key;

        /// <summary>What it defines, and every question its stages asked of the document's.</summary>
        public LinkTargets Targets { get; } = targets;

        /// <summary>The version of what the units define it was last held to.</summary>
        public int Defined { get; set; } = defined;

        /// <summary>What its blocks say to the outline.</summary>
        public OutlineUnit Outline { get; } = outline;

        /// <summary>What it was last laid into, or null before it is.</summary>
        public LaidUnit? Laid { get; set; }

        /// <summary>Whether something its stages read into it, which its characters do not say, has changed since — so it is worked over again.</summary>
        public bool Stale { get; set; }

        private (int Start, ContentDocument Document, ContentReading Reading)? _placed;

        /// <summary>A reading of it standing at <paramref name="start"/> in <paramref name="document"/> — made once for each place, and kept while it stands there.</summary>
        public ContentReading Placed(int start, ContentDocument document)
        {
            if (_placed is { } placed && placed.Start == start && ReferenceEquals(placed.Document, document)) return placed.Reading;

            // A reading's document is the characters it prints as: the unit's own stretch of the document, as a body's reading is its body's.
            var reading = ContentReading.Of(this.Tree, start, document.Stretch(start, this.Width));
            _placed = (start, document, reading);

            return reading;
        }
    }

    /// <summary>
    /// What a unit was laid into: the laying, where the unit started then, the root of the reading it was laid from, and what it was
    /// laid with — kept while the unit is, and the room, whether it is only read and the folds reaching into it are as they were.
    /// </summary>
    private sealed record LaidUnit(Laid Laid, int Start, ContentPart Root, double Room, bool ReadOnly, string Folds);
}
