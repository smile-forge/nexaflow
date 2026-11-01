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
/// The content laid a unit at a time. Every unit of its reading stands on the page at a height — the one it was laid at, where a
/// laying of it stands, and otherwise what its characters come to at the height a character of what is laid came to — and only the
/// units the caller asks for are worked over by the language's stages and laid, each from its own tree, and kept while what it was
/// worked over and laid with stands. So a keystroke works over and lays the unit it fell in, and a jump to the end lays the end.
///
/// <para>
/// <strong>What one unit says reaches the others through the engine.</strong> What every unit read says to the rest of the document —
/// what it defines, and what its blocks say to the outline — is known whether or not it is laid (<see cref="UnitFacts"/>): from the
/// stages that worked it over to lay it, or from working it over in the background. What the units define is gathered into the
/// document's link targets, in order, and a unit's stages are answered from those, every question remembered — so a definition that
/// changes what it means works over again only the units that asked about it.
/// </para>
/// <para>
/// <strong>The document is its units.</strong> What is laid is a document piece holding a mount for each unit laid, put down where the
/// heights above it put it, the gap at each seam between two laid units said by the language from the blocks either side. What reads
/// the document as blocks asks the unit holding the place, through a reading of that unit placed where it starts — worked over then,
/// where nothing has yet.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>Every unit of the reading shown, with where it starts, in order.</summary>
    private List<(ContentUnit Unit, int Start)> _units = [];

    /// <summary>The version of the document the units are of.</summary>
    private ContentDocument _unitsIn = ContentDocument.Of(string.Empty);

    /// <summary>What the reading the units are of keeps of all of it (<see cref="ContentUnits.Syntax"/>), as last laid.</summary>
    private IReadingSyntax? _readingSyntax;

    /// <summary>Each unit worked over, by the tree it was read into — kept while that tree is read, laid or not, so its height outlives being off screen.</summary>
    private Dictionary<ContentNode, StagedUnit> _staged = new(ReferenceEqualityComparer.Instance);

    /// <summary>What each unit read says to the rest of the document, by the tree it was read into, once something has worked it over.</summary>
    private readonly ConditionalWeakTable<ContentNode, UnitFacts> _facts = new();

    /// <summary>What a unit nothing has worked over yet says to the outline: how wide it is, and nothing more.</summary>
    private readonly ConditionalWeakTable<ContentNode, OutlineUnit> _unworked = new();

    /// <summary>How tall each unit read comes to as its language says (<see cref="ContentLanguage.UnitHeight"/>), with the style it was said in, by the tree it was read into.</summary>
    private readonly ConditionalWeakTable<ContentNode, StrongBox<(LayoutStyle? Style, double Height)>> _heights = new();

    /// <summary>Where on the page each unit stood as last laid — and, one past the last, where what is read ends.</summary>
    private double[] _tops = [0];

    /// <summary>Which units stood laid as last laid, the laying of each standing.</summary>
    private bool[] _standing = [];

    /// <summary>How tall a character of what was laid came to, as last laid — or zero before anything has been.</summary>
    private double _perCharacter;

    /// <summary>The language and showing the units were last worked over in, which a unit worked over to be read rather than laid is worked over in too.</summary>
    private (ContentLanguage Language, ContentShowing Showing)? _stagedBy;

    /// <summary>How many times what the units define has come to mean something else — what a unit kept was last held to.</summary>
    private int _defined;

    /// <summary>What each unit said of itself when what the units define was last gathered, in order — null for a unit nothing had worked over.</summary>
    private List<UnitFacts?> _definedBy = [];

    /// <summary>How many diagrams are written in the units before each, in order — which diagram is which, for what the reader opened in it.</summary>
    private int[] _diagramsBefore = [];

    /// <summary>The places in each unit's reading where a diagram's body starts, in order, by the unit's tree.</summary>
    private static readonly ConditionalWeakTable<ContentNode, int[]> DiagramsIn = new();

    /// <summary>
    /// Brings what the units <paramref name="wanted"/> picks out were worked over into up to date: a unit whose tree, showing and the
    /// answers it was given stand is kept, and every other is worked over again, the blocks of those it replaces handed to
    /// <see cref="WithUnchanged"/> first. Every unit no longer read is let go. What the units define is gathered into the document's link
    /// targets, and a unit wanted that asked about a definition now meaning something else is worked over again.
    /// </summary>
    private void Stage(ContentLanguage language, IReadOnlyCollection<int> wanted, ContentShowing showing)
    {
        var now = new Dictionary<ContentNode, StagedUnit>(ReferenceEqualityComparer.Instance);
        foreach (var (unit, _) in _units)
            if (_staged.Remove(unit.Tree, out var kept)) now[unit.Tree] = kept;

        // What is no longer read — read again from a change — is what the units read in its place are matched against.
        var leaving = new List<StagedUnit>(_staged.Values);
        var again = new List<int>();

        foreach (var index in wanted)
        {
            var (unit, start) = _units[index];
            if (now.TryGetValue(unit.Tree, out var kept) && kept.Key == this.KeyFor(showing, start, unit.Width) && !kept.Stale) continue;

            if (kept is not null) leaving.Add(kept);
            again.Add(index);
        }

        _staged = now;
        _stagedBy = (language, showing);

        // Blocks of the units leaving are what the blocks of the units replacing them are matched against — none, once told that
        // nothing laid is to be set down as it was.
        if (!_unkept)
            foreach (var gone in leaving)
                if (gone.Key.Forgotten == _forgotten) _unchanged.Leaving(gone.Tree);

        _unkept = false;

        foreach (var index in again) this.Restaged(language, index, showing);

        // What the units define, gathered — and a unit wanted that asked about something now meaning otherwise, worked over again. A
        // unit defines what its own characters say, so working one over again changes nothing it defines, and once is enough.
        this.Defined();

        foreach (var index in wanted)
        {
            var staged = _staged[_units[index].Unit.Tree];
            if (staged.Defined == _defined) continue;

            if (staged.Targets.Answered())
            {
                staged.Defined = _defined;
                continue;
            }

            _unchanged.Leaving(staged.Tree);
            this.Restaged(language, index, showing);
        }

        _unchanged.Settle();
    }

    /// <summary>The unit at <paramref name="index"/> worked over afresh, kept, and what it says to the rest of the document said.</summary>
    private StagedUnit Restaged(ContentLanguage language, int index, ContentShowing showing)
    {
        var (unit, start) = _units[index];
        var staged = this.Staged(language, unit, start, showing, _unitsIn);

        _staged[unit.Tree] = staged;
        return staged;
    }

    /// <summary>
    /// <paramref name="unit"/>'s own tree, starting at <paramref name="start"/> in <paramref name="document"/>, worked over by its
    /// language's stages: offsets the unit's own, the showing standing where it starts, and link targets of its own over the
    /// document's — and what that says to the rest of the document, said (<see cref="UnitFacts"/>).
    /// </summary>
    private StagedUnit Staged(ContentLanguage language, ContentUnit unit, int start, ContentShowing showing, ContentDocument document)
    {
        var asking = (_asking, _askingLays);
        (_asking, _askingLays) = (unit.Tree, false);

        try
        {
            var tree = Bound(language, unit.Tree);
            var targets = new LinkTargets(_targets);
            var own = showing with { At = start, Targets = targets, Syntax = showing.Syntax is { } read ? (read.Syntax, start) : null };

            var stages = language.Stages(tree, own).OfType<IAstStage>()
                                 .Select(stage => stage is ISlowStage slow ? Staging(language, document.Stretch(start, unit.Width), slow) : stage)
                                 .ToList();

            var worked = stages.Count == 0 ? tree : new AstPipeline(stages).Run(tree);

            this.Said(unit.Tree, new UnitFacts(OutlineUnit.Of(worked.Children, unit.Width), targets.Reported, targets.Defined));

            return new StagedUnit(unit.Tree, worked, unit.Width, this.KeyFor(showing, start, unit.Width), targets, _defined);
        }
        finally
        {
            (_asking, _askingLays) = asking;
        }
    }

    /// <summary>Keeps what the unit read into <paramref name="read"/> says of itself — the one it said before, where it says the same.</summary>
    private void Said(ContentNode read, UnitFacts facts)
    {
        if (_facts.TryGetValue(read, out var said) && said.Same(facts)) return;

        _facts.AddOrUpdate(read, facts);
    }

    /// <summary>
    /// Gathers what every unit read defines into the document's link targets, in order — again only where a unit now says something
    /// other than it did, and counted as a change to what the units define only where that means something else. A unit nothing has
    /// worked over yet defines nothing until something has.
    /// </summary>
    private void Defined()
    {
        var by = new List<UnitFacts?>(_units.Count);
        foreach (var (unit, _) in _units) by.Add(_facts.TryGetValue(unit.Tree, out var facts) ? facts : null);

        var first = 0;
        while (first < by.Count && first < _definedBy.Count && ReferenceEquals(by[first], _definedBy[first])) first++;

        var last = 0;
        while (last < by.Count - first && last < _definedBy.Count - first && ReferenceEquals(by[^(last + 1)], _definedBy[^(last + 1)])) last++;

        var was = _definedBy;
        _definedBy = by;

        if (first == by.Count && first == was.Count) return;
        if (UnitFacts.Meant(was.Skip(first).Take(was.Count - first - last)) == UnitFacts.Meant(by.Skip(first).Take(by.Count - first - last))) return;
        if (!by.Any(facts => facts?.Reports == true)) return;

        var defined = new List<LinkTarget>();

        for (var index = 0; index < by.Count; index++)
            foreach (var target in by[index]?.Defined ?? [])
                defined.Add(target with { At = target.At + _units[index].Start });

        _targets.Report(_unitsIn.Length, defined);
        _defined++;
    }

    /// <summary>What a unit starting at <paramref name="start"/>, <paramref name="width"/> long, is worked over with.</summary>
    private StagingKey KeyFor(ContentShowing showing, int start, int width) =>
        new(showing.Style, showing.Writing,
            showing.Shown is { } zone && zone.Start <= start + width && start <= zone.End ? zone with { Start = zone.Start - start, End = zone.End - start } : null,
            _forgotten);

    /// <summary>
    /// Whether the unit at <paramref name="index"/> shows anything: one with no block of its own — trivia between blocks, a document's
    /// closing marks — takes no room and is laid only where it is the whole of the content, which shows something whatever it holds.
    /// One nothing has worked over yet is taken to.
    /// </summary>
    private bool Shows(int index) =>
        _units.Count == 1 || !_facts.TryGetValue(_units[index].Unit.Tree, out var facts) || facts.Outline.Blocks > 0;

    /// <summary>
    /// What the unit at <paramref name="index"/> was laid into, where that laying still stands: worked over as it would be now, and laid
    /// at <paramref name="room"/>, only read or not as it is to be, and with the folds that reach into it as they now are.
    /// </summary>
    private LaidUnit? Standing(int index, ContentShowing showing, double room, bool readOnly)
    {
        var (unit, start) = _units[index];

        return _staged.TryGetValue(unit.Tree, out var staged) && !staged.Stale && staged.Key == this.KeyFor(showing, start, unit.Width)
               && staged.Laid is { } laid && laid.Room == room && laid.ReadOnly == readOnly && laid.Folds == this.Folds(start, unit.Width)
            ? laid
            : null;
    }

    /// <summary>Whether <paramref name="laid"/> laid any block of its unit: one all trivia, definitions, or folded away, laid nothing.</summary>
    private static bool Drew(ContentLanguage language, LaidUnit laid) =>
        laid.Laid.Exists && (language.Seam is null || laid.Laid.Ends.First is not null);

    /// <summary>
    /// Where each unit stands on the page: the units whose laying stands, at the height each was laid at, the gap at a seam between two
    /// of them said by the language; every other at an estimate — its height when last laid, where it was, and otherwise its characters
    /// at the height a character of what stands laid comes to. With, for each unit, its laying where that stands, and that height.
    /// </summary>
    private (double[] Tops, LaidUnit?[] Laid, double PerCharacter) Placed(ContentLanguage language, ContentShowing showing, double room, bool readOnly)
    {
        var count = _units.Count;
        var laid = new LaidUnit?[count];
        var (high, wide) = (0.0, 0L);

        for (var index = 0; index < count; index++)
        {
            if (!this.Shows(index) || this.Standing(index, showing, room, readOnly) is not { } standing) continue;

            laid[index] = standing;
            if (Drew(language, standing)) (high, wide) = (high + standing.Laid.Height, wide + _units[index].Unit.Width);
        }

        var perCharacter = wide > 0 ? high / wide : Unsaid(showing.Style, room);
        var tops = new double[count + 1];
        var (y, above) = (0.0, (ContentPart?)null);

        for (var index = 0; index < count; index++)
        {
            tops[index] = y;
            if (!this.Shows(index)) continue;

            if (laid[index] is not { } standing)
            {
                var (unit, _) = _units[index];
                y += language.UnitHeight is { } saying ? this.HeightSaid(unit.Tree, showing.Style, saying)
                   : _staged.TryGetValue(unit.Tree, out var staged) && staged.Laid is { } was ? was.Laid.Height
                   : unit.Width * perCharacter;
                above = null;
                continue;
            }

            if (!Drew(language, standing)) continue;

            if (above is not null && standing.Laid.Ends.First is { } first && language.Seam is { } seam) y += seam(above, first, showing.Style);

            tops[index] = y;
            y += standing.Laid.Height;
            above = standing.Laid.Ends.Last ?? above;
        }

        tops[count] = y;
        return (tops, laid, perCharacter);
    }

    /// <summary>
    /// How tall a character comes to before anything is laid to say: a line one and a half times the text size for every line's worth
    /// of characters, each half the text size wide, across <paramref name="room"/>.
    /// </summary>
    private static double Unsaid(LayoutStyle style, double room) => 0.75 * style.TextSize * style.TextSize / Math.Max(room, 1);

    /// <summary>How tall the unit read into <paramref name="tree"/> comes to in <paramref name="style"/>, as its language says — said once for each style.</summary>
    private double HeightSaid(ContentNode tree, LayoutStyle style, Func<ContentNode, LayoutStyle, ILayoutMedium, double> height)
    {
        var said = _heights.GetValue(tree, static _ => new StrongBox<(LayoutStyle? Style, double Height)>());
        if (!ReferenceEquals(said.Value.Style, style)) said.Value = (style, height(tree, style, WpfMedium.OnThisThread));

        return said.Value.Height;
    }

    /// <summary>
    /// Lays out every unit <paramref name="wanted"/> picks out whose laying no longer stands, and puts the units together: a document
    /// piece holding the tree of each unit laid, mounted where its unit stands on the page and shifted to where it now starts, every
    /// other unit — and the <paramref name="unread"/> characters past what is read — leaving room for what it is estimated to come to.
    /// One unit standing where it was laid is the whole of what was laid.
    /// </summary>
    private Laid Composed(ContentLanguage language, ContentShowing showing, double room, bool readOnly, Keeping holding, IReadOnlyCollection<int> wanted,
                          int unread)
    {
        foreach (var index in wanted)
        {
            if (!this.Shows(index) || this.Standing(index, showing, room, readOnly) is not null) continue;

            var (unit, start) = _units[index];
            var staged = _staged[unit.Tree];
            var reading = staged.Placed(start, _unitsIn);
            _readings.AddOrUpdate(staged.Tree, holding.Address);

            var asking = (_asking, _askingLays);
            (_asking, _askingLays) = (unit.Tree, true);

            try
            {
                staged.Laid = new LaidUnit(Builder(language, reading, showing, LayoutStanding.Alone, holding, Folding).Lay(room), start, reading.Root,
                                           room, readOnly, this.Folds(start, unit.Width));
            }
            finally
            {
                (_asking, _askingLays) = asking;
            }
        }

        var (tops, laid, perCharacter) = this.Placed(language, showing, room, readOnly);
        (_tops, _standing, _perCharacter) = (tops, [.. laid.Select(standing => standing is not null)], perCharacter);

        if (_units.Count == 1 && unread == 0 && laid[0] is { } only && only.Start == _units[0].Start) return only.Laid;

        // Standing for the whole reading as the root of it laid whole stands for it: what its units are laid as.
        var kind = LayoutPieces.Document;
        for (var index = 0; index < _units.Count; index++)
            if (laid[index] is { } first && Drew(language, first))
            {
                kind = first.Laid.Root.Kind;
                break;
            }

        var into = new LayoutBuilder(WpfMedium.OnThisThread, _units.Count + 1);
        into.Open(kind, new SourceSpan(0, _unitsIn.Length), stops: LayoutStops.None);

        var reach = 0.0;
        var trouble = new List<LayoutDiagnostic>();

        for (var index = 0; index < _units.Count; index++)
        {
            if (laid[index] is not { } standing || !Drew(language, standing)) continue;

            var by = _units[index].Start - standing.Start;
            var mount = by == 0 ? new LayoutMount(standing.Laid.Tree) : new LayoutMount(standing.Laid.Tree, standing.Root, by);
            into.Mount(mount, new LayoutPoint(0, tops[index]));

            foreach (var said in standing.Laid.Trouble)
                trouble.Add(by == 0 ? said : said with { Start = said.Start + by, Part = mount.Restated(said.Part) });

            reach = Math.Max(reach, standing.Laid.Width);
        }

        into.Close();

        return new Laid(into.Seal(), Math.Max(reach, 1), Math.Max(tops[^1] + (unread * perCharacter), 1), trouble);
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
    /// Counts the diagrams written in each unit, in order, so which one a body is can be said where it is asked for — or, for content
    /// that is itself a diagram, takes what was opened in it.
    /// </summary>
    private void Views(ContentLanguage language)
    {
        _top = language == ContentLanguages.Markdown ? null : _opened.At(0);
        _diagramsBefore = new int[_units.Count];
        if (_top is not null) return;

        var count = 0;

        for (var index = 0; index < _units.Count; index++)
        {
            _diagramsBefore[index] = count;
            count += DiagramsOf(_units[index].Unit.Tree).Length;
        }
    }

    /// <summary>What the reader opened in the diagram whose body starts at <paramref name="start"/>, counted in the order the diagrams are written — or null where none does.</summary>
    private DiagramViewState? Diagram(int start)
    {
        if (_top is not null || this.Holding(start) is not (>= 0 and var index) || index >= _diagramsBefore.Length) return null;

        var at = Array.BinarySearch(DiagramsOf(_units[index].Unit.Tree), start - _units[index].Start);

        return at >= 0 ? _opened.At(_diagramsBefore[index] + at) : null;
    }

    /// <summary>Where in <paramref name="tree"/>, a unit's reading, each diagram's body starts, in order — found once for each tree.</summary>
    private static int[] DiagramsOf(ContentNode tree) => DiagramsIn.GetValue(tree, static tree => Diagrams(tree));

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

    /// <summary>Every unit worked over, each as a reading of it placed where it starts, in order.</summary>
    internal IEnumerable<ContentPart> Readings =>
        _units.Where(read => _staged.ContainsKey(read.Unit.Tree)).Select(read => _staged[read.Unit.Tree].Placed(read.Start, _unitsIn).Root);

    /// <summary>The reading of the unit holding <paramref name="offset"/>, placed where it starts — or null where nothing is read.</summary>
    private ContentPart? UnitHolding(int offset) => this.Holding(offset) is >= 0 and var index ? this.PlacedAt(index) : null;

    /// <summary>
    /// Which unit holds <paramref name="offset"/> — the last starting at or before it, the end of what is read held by the last — or -1
    /// where it is past what is read, or nothing is.
    /// </summary>
    private int Holding(int offset)
    {
        if (_units.Count == 0 || offset < 0) return -1;

        var (last, start) = _units[^1];
        if (offset > start + last.Width) return -1;

        var (low, high) = (0, _units.Count - 1);

        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_units[middle].Start <= offset) low = middle;
            else high = middle - 1;
        }

        return low;
    }

    /// <summary>
    /// The reading of the unit at <paramref name="index"/>, placed where it starts — worked over now where nothing has yet, as the units
    /// laid were; null before anything has been laid to say how.
    /// </summary>
    private ContentPart? PlacedAt(int index)
    {
        var (unit, start) = _units[index];
        if (_staged.TryGetValue(unit.Tree, out var staged)) return staged.Placed(start, _unitsIn).Root;
        if (_stagedBy is not var (language, showing)) return null;

        // What it says to the rest of the document may be new: the outline and the link targets take it in when next laid.
        staged = this.Restaged(language, index, showing);
        Grow();

        return staged.Placed(start, _unitsIn).Root;
    }

    /// <summary>The content's own blocks, starting from <paramref name="from"/> up to <paramref name="to"/>, in order across its units.</summary>
    private IEnumerable<ContentPart> Tops(int from = 0, int to = int.MaxValue)
    {
        for (var index = Math.Max(this.Holding(from), 0); index < _units.Count; index++)
        {
            var (unit, start) = _units[index];
            if (start >= to) yield break;
            if (start + unit.Width <= from && unit.Width > 0) continue;

            if (this.PlacedAt(index) is not { } reading) yield break;

            foreach (var part in Inside(reading))
                if (part.Start >= from && part.Start < to) yield return part;
        }
    }

    /// <summary>The written block — not trivia, nothing derived — that <paramref name="offset"/> is in, or the one starting there.</summary>
    private ContentPart? WrittenAt(int offset)
    {
        var index = this.Holding(offset);
        if (index < 0) return null;

        // The block it is in is in the unit holding it — or, where it stands at that unit's very end, the next unit's first.
        for (var at = index; at < _units.Count && at <= index + 1; at++)
            foreach (var block in this.PlacedAt(at) is { } reading ? Inside(reading) : [])
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
        foreach (var staged in _staged.Values)
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
    private sealed class StagedUnit(ContentNode read, ContentNode tree, int width, StagingKey key, LinkTargets targets, int defined)
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

        /// <summary>What it was last laid into, or null before it is — kept off screen, where its height is where it stands.</summary>
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

/// <summary>
/// What a unit read says to the rest of its document, worked over by its language's stages on its own — with nothing a host said, which
/// no heading or definition turns on — whether or not it is laid: what its blocks say to the outline, and what it defines.
/// </summary>
/// <param name="Outline">What its blocks say to the outline.</param>
/// <param name="Reports">Whether a stage said what it defines — never, for content in a language none of whose stages look.</param>
/// <param name="Defined">What it defines, in the order written, where in the unit each stands.</param>
internal sealed record UnitFacts(OutlineUnit Outline, bool Reports, IReadOnlyList<LinkTarget> Defined)
{
    /// <summary>Whether <paramref name="other"/> says the same of the same unit.</summary>
    public bool Same(UnitFacts other) =>
        Reports == other.Reports && Outline.Blocks == other.Outline.Blocks && Outline.Entries.SequenceEqual(other.Outline.Entries)
        && Defined.SequenceEqual(other.Defined);

    /// <summary>What the units <paramref name="said"/> define means, compared by value: what each target means and is called, not where it stands.</summary>
    public static string Meant(IEnumerable<UnitFacts?> said) =>
        string.Join('\n', said.SelectMany(facts => facts?.Defined ?? []).Select(target => $"{target.Kind}\t{target.Name}\t{target.Meaning}\t{target.Title}"));
}
