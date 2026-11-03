using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Pipeline;


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
    /// <summary>The content's own reading as last laid out — what every question of its blocks, outline and places is asked of; null before anything is.</summary>
    private ContentWindow? _shown;

    /// <summary>What each unit read says to the rest of the document, by the tree it was read into, once something has worked it over.</summary>
    private readonly ConditionalWeakTable<ContentNode, UnitFacts> _facts = new();

    /// <summary>What a unit nothing has worked over yet says to the outline: how wide it is, and nothing more.</summary>
    private readonly ConditionalWeakTable<ContentNode, OutlineUnit> _unworked = new();

    /// <summary>How tall each unit read comes to as its language says (<see cref="ContentLanguage.UnitHeight"/>), with the style it was said in, by the tree it was read into.</summary>
    private readonly ConditionalWeakTable<ContentNode, StrongBox<(LayoutStyle? Style, double? WrapsIn, double Height)>> _heights = new();

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
    private void Stage(ContentWindow window, ContentLanguage language, IReadOnlyCollection<int> wanted, ContentShowing showing)
    {
        var now = new Dictionary<ContentNode, StagedUnit>(ReferenceEqualityComparer.Instance);
        foreach (var (unit, _) in window.Listed)
            if (window.Staged.Remove(unit.Tree, out var kept)) now[unit.Tree] = kept;

        // What is no longer read — read again from a change — is what the units read in its place are matched against.
        var leaving = new List<StagedUnit>(window.Staged.Values);
        var again = new List<int>();

        foreach (var index in wanted)
        {
            var (unit, start) = window.Listed[index];
            if (now.TryGetValue(unit.Tree, out var kept) && kept.Key == this.KeyFor(showing, start, unit.Width) && !kept.Stale) continue;

            if (kept is not null) leaving.Add(kept);
            again.Add(index);
        }

        window.Staged = now;
        window.StagedBy = (language, showing);

        // Blocks of the units leaving are what the blocks of the units replacing them are matched against — none, once told that
        // nothing laid is to be set down as it was.
        if (!_unkept)
            foreach (var gone in leaving)
                if (gone.Key.Forgotten == _forgotten) _unchanged.Leaving(gone.Tree);

        _unkept = false;

        foreach (var index in again) this.Restaged(window, language, index, showing);

        // What the units define, gathered — and a unit wanted that asked about something now meaning otherwise, worked over again. A
        // unit defines what its own characters say, so working one over again changes nothing it defines, and once is enough.
        this.Defined(window);

        foreach (var index in wanted)
        {
            var staged = window.Staged[window.Listed[index].Unit.Tree];
            if (staged.Defined == window.Defined) continue;

            if (staged.Targets.Answered())
            {
                staged.Defined = window.Defined;
                continue;
            }

            _unchanged.Leaving(staged.Tree);
            this.Restaged(window, language, index, showing);
        }

        _unchanged.Settle();
    }

    /// <summary>The unit at <paramref name="index"/> of <paramref name="window"/> worked over afresh, kept, and what it says to the rest of the document said.</summary>
    private StagedUnit Restaged(ContentWindow window, ContentLanguage language, int index, ContentShowing showing)
    {
        var (unit, start) = window.Listed[index];
        var staged = this.Staged(window, language, unit, start, showing);

        window.Staged[unit.Tree] = staged;
        return staged;
    }

    /// <summary>
    /// <paramref name="unit"/>'s own tree, starting at <paramref name="start"/> in <paramref name="window"/>'s characters, worked over by its
    /// language's stages: offsets the unit's own, the showing standing where it starts, and link targets of its own over the
    /// document's — and what that says to the rest of the document, said (<see cref="UnitFacts"/>).
    /// </summary>
    private StagedUnit Staged(ContentWindow window, ContentLanguage language, ContentUnit unit, int start, ContentShowing showing)
    {
        var asking = (_asking, _askingLays, _askingIn);
        (_asking, _askingLays, _askingIn) = (unit.Tree, false, window);

        try
        {
            var tree = Bound(language, unit.Tree);
            var targets = new LinkTargets(window.Targets);
            var own = showing with { At = start, Targets = targets, Syntax = showing.Syntax is { } read ? (read.Syntax, start) : null };

            var stages = language.Stages(tree, own).OfType<IAstStage>()
                                 .Select(stage => stage is ISlowStage slow ? Staging(language, window.ListedIn.Stretch(start, unit.Width), slow) : stage)
                                 .ToList();

            var worked = stages.Count == 0 ? tree : new AstPipeline(stages).Run(tree);

            this.Said(unit.Tree, new UnitFacts(OutlineUnit.Of(worked.Children, unit.Width), targets.Reported, targets.Defined));

            return new StagedUnit(unit.Tree, worked, unit.Width, this.KeyFor(showing, start, unit.Width), targets, window.Defined);
        }
        finally
        {
            (_asking, _askingLays, _askingIn) = asking;
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
    private void Defined(ContentWindow window)
    {
        var by = new List<UnitFacts?>(window.Listed.Count);
        foreach (var (unit, _) in window.Listed) by.Add(_facts.TryGetValue(unit.Tree, out var facts) ? facts : null);

        var first = 0;
        while (first < by.Count && first < window.DefinedBy.Count && ReferenceEquals(by[first], window.DefinedBy[first])) first++;

        var last = 0;
        while (last < by.Count - first && last < window.DefinedBy.Count - first && ReferenceEquals(by[^(last + 1)], window.DefinedBy[^(last + 1)])) last++;

        var was = window.DefinedBy;
        window.DefinedBy = by;

        if (first == by.Count && first == was.Count) return;
        if (UnitFacts.Meant(was.Skip(first).Take(was.Count - first - last)) == UnitFacts.Meant(by.Skip(first).Take(by.Count - first - last))) return;
        if (!by.Any(facts => facts?.Reports == true)) return;

        var defined = new List<LinkTarget>();

        for (var index = 0; index < by.Count; index++)
            foreach (var target in by[index]?.Defined ?? [])
                defined.Add(target with { At = target.At + window.Listed[index].Start });

        window.Targets.Report(window.ListedIn.Length, defined);
        window.Defined++;
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
    private bool Shows(ContentWindow window, int index) =>
        window.Listed.Count == 1 || !_facts.TryGetValue(window.Listed[index].Unit.Tree, out var facts) || facts.Outline.Blocks > 0;

    /// <summary>
    /// What the unit at <paramref name="index"/> was laid into, where that laying still stands: worked over as it would be now, and laid
    /// at <paramref name="room"/>, only read or not as it is to be, and with the folds that reach into it as they now are.
    /// </summary>
    private LaidUnit? Standing(ContentWindow window, int index, ContentShowing showing, double room, bool readOnly)
    {
        var (unit, start) = window.Listed[index];

        return window.Staged.TryGetValue(unit.Tree, out var staged) && !staged.Stale && !staged.LaidStale && staged.Key == this.KeyFor(showing, start, unit.Width)
               && staged.Laid is { } laid && laid.Room == room && laid.ReadOnly == readOnly && laid.Folds == this.Folds(window, start, unit.Width)
               && laid.SlidVersion == _slidVersion
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
    private (double[] Tops, LaidUnit?[] Laid, double PerCharacter) Placed(ContentWindow window, ContentLanguage language, ContentShowing showing, double room,
                                                                         bool readOnly)
    {
        var count = window.Listed.Count;
        var laid = new LaidUnit?[count];
        var (high, wide) = (0.0, 0L);

        for (var index = 0; index < count; index++)
        {
            if (!this.Shows(window, index) || this.Standing(window, index, showing, room, readOnly) is not { } standing) continue;

            laid[index] = standing;
            if (Drew(language, standing)) (high, wide) = (high + standing.Laid.Height, wide + window.Listed[index].Unit.Width);
        }

        var perCharacter = wide > 0 ? high / wide : Unsaid(showing.Style, room);
        var tops = new double[count + 1];
        var (y, above) = (0.0, (ContentPart?)null);

        for (var index = 0; index < count; index++)
        {
            tops[index] = y;
            if (!this.Shows(window, index)) continue;

            if (laid[index] is not { } standing)
            {
                var (unit, _) = window.Listed[index];
                y += language.UnitHeight is { } saying ? this.HeightSaid(unit.Tree, showing.Style, showing.WrapsLines ? room : null, saying)
                   : window.Staged.TryGetValue(unit.Tree, out var staged) && staged.Laid is { } was ? was.Laid.Height
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
    private double HeightSaid(ContentNode tree, LayoutStyle style, double? wrapsIn, Func<ContentNode, LayoutStyle, ILayoutMedium, double?, double> height)
    {
        var said = _heights.GetValue(tree, static _ => new StrongBox<(LayoutStyle? Style, double? WrapsIn, double Height)>());
        if (!ReferenceEquals(said.Value.Style, style) || said.Value.WrapsIn != wrapsIn) said.Value = (style, wrapsIn, height(tree, style, _medium, wrapsIn));

        return said.Value.Height;
    }

    /// <summary>
    /// Lays out every unit <paramref name="wanted"/> picks out whose laying no longer stands, and puts the units together: a document
    /// piece holding the tree of each unit laid, mounted where its unit stands on the page and shifted to where it now starts, every
    /// other unit — and the <paramref name="unread"/> characters past what is read — leaving room for what it is estimated to come to.
    /// One unit standing where it was laid is the whole of what was laid.
    /// </summary>
    private Laid Composed(ContentWindow window, ContentLanguage language, ContentShowing showing, double room, bool readOnly, Keeping holding,
                          LayoutStanding standingAs, IReadOnlyCollection<int> wanted,
                          int unread, double[] before)
    {
        foreach (var index in wanted)
        {
            if (!this.Shows(window, index)) continue;

            // Laid again where its laying does not stand, or stands for less of it than the reader now reaches.
            var reaches = this.ReachOf(window, index, before);
            if (this.Standing(window, index, showing, room, readOnly) is { } standing && Covered(window, index, standing, reaches)) continue;

            var (unit, start) = window.Listed[index];
            var staged = window.Staged[unit.Tree];
            var reading = PlacedIn(window, staged, start);
            _readings.AddOrUpdate(reading.Root.Node, holding.Address);

            var asking = (_asking, _askingLays, _askingIn);
            (_asking, _askingLays, _askingIn) = (unit.Tree, true, window);

            try
            {
                Func<ContentPart, LayoutFold>? folds = window.Placing is null ? Folding : null;
                var made = Builder(language, reading, Building(window, showing), standingAs, holding, folds, reaches).Lay(room);

                staged.Laid = new LaidUnit(made, Where(window, start), reading.Root, room, readOnly, this.Folds(window, start, unit.Width), _slidVersion);
                staged.LaidStale = false;
            }
            finally
            {
                (_asking, _askingLays, _askingIn) = asking;
            }
        }

        var (tops, laid, perCharacter) = this.Placed(window, language, showing, room, readOnly);
        // A unit stands laid where its laying stands for what the reader reaches of it — so laying again where they come to more of it.
        var covered = new bool[laid.Length];
        for (var index = 0; index < laid.Length; index++)
            covered[index] = laid[index] is { } standing
                             && Covered(window, index, standing, standing.Laid.Reached is null ? null : this.ReachOf(window, index, tops));

        (window.Tops, window.Standing, window.PerCharacter) = (tops, covered, perCharacter);

        if (window.Listed.Count == 1 && unread == 0 && laid[0] is { } only && only.Start == Where(window, window.Listed[0].Start)) return only.Laid;

        // Standing for the whole reading as the root of it laid whole stands for it: what its units are laid as.
        var kind = LayoutPieces.Document;
        for (var index = 0; index < window.Listed.Count; index++)
            if (laid[index] is { } first && Drew(language, first))
            {
                kind = first.Laid.Root.Kind;
                break;
            }

        var into = new LayoutBuilder(_medium, window.Listed.Count + 1);
        var (begins, ends) = (Where(window, 0), Where(window, window.ListedIn.Length));
        into.Open(kind, new SourceSpan(begins, ends - begins), stops: LayoutStops.None);

        var reach = 0.0;
        var trouble = new List<LayoutDiagnostic>();

        for (var index = 0; index < window.Listed.Count; index++)
        {
            if (laid[index] is not { } standing || !Drew(language, standing)) continue;

            var by = Where(window, window.Listed[index].Start) - standing.Start;
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
    /// What of the unit at <paramref name="index"/> of <paramref name="window"/>, standing where <paramref name="tops"/> puts it, the reader
    /// reaches — the screen and a screen either side of it, in the unit's own frame, and the caret, each end of what is picked out and where
    /// the reader went, where they are in it — or null where all of it is to be laid, as where the content is laid whole.
    /// </summary>
    private LayoutReach? ReachOf(ContentWindow window, int index, double[] tops) => _windowing ? this.ReachIn(window, index, tops, this.Reach()) : null;

    /// <summary>
    /// What of the unit at <paramref name="index"/> of <paramref name="window"/>, standing where <paramref name="tops"/> puts it, the reader
    /// reaches — <paramref name="band"/> of the page, in the unit's own frame, and the caret, each end of what is picked out and where the
    /// reader went, where they are in it — whether or not the content is being laid where the reader is just now.
    /// </summary>
    /// <param name="band">The stretch of the page asked for: the screen and a screen either side of it to lay, the screen alone to have laid.</param>
    private LayoutReach ReachIn(ContentWindow window, int index, double[] tops, (double Top, double Bottom) band)
    {
        // A body not yet found on the page is laid from its start, as much of it as a screen either side would reach.
        var (from, to) = band;
        var (top, bottom) = window.Placing is null ? (from, to)
                          : window.PageTop is { } stood ? (from - stood, to - stood)
                          : (0.0, Math.Max(_showing?.Height ?? Room, 1) * 2);

        var (unit, start) = window.Listed[index];
        var (begins, ends) = (Where(window, start), Where(window, start + unit.Width));
        var places = new List<int>();

        void Place(int offset)
        {
            if (offset >= begins && offset <= ends) places.Add(offset);
        }

        Place(_state.Caret);
        foreach (var range in _state.Selection)
        {
            Place(range.Start);
            Place(range.End);
        }

        if (_reveal is { } target) Place(target);

        return new LayoutReach(top - tops[index], bottom - tops[index], places);
    }

    /// <summary>
    /// Whether <paramref name="standing"/>, the laying of the unit at <paramref name="index"/> of <paramref name="window"/>, lays what
    /// <paramref name="reach"/> asks of it: all of it laid, or the part laid holding all that is asked — none of it, where all is asked.
    /// </summary>
    private static bool Covered(ContentWindow window, int index, LaidUnit standing, LayoutReach? reach) =>
        standing.Laid.Reached is not { } reached
        || (reach is not null && reached.Covers(reach, standing.Laid.Height, Where(window, window.Listed[index].Start) - standing.Start));

    /// <summary>Whether the laying of the unit at <paramref name="index"/> of <paramref name="window"/> laid what stands at <paramref name="offset"/>: all of it, or the part holding it.</summary>
    private static bool LaidHolds(ContentWindow window, int index, int offset) =>
        window.Staged.TryGetValue(window.Listed[index].Unit.Tree, out var staged) && staged.Laid is { } laid
        && (laid.Laid.Reached is not { } reached || reached.Holds(offset - (Where(window, window.Listed[index].Start) - laid.Start)));

    /// <summary>
    /// A reading of the unit <paramref name="staged"/>, starting at <paramref name="start"/> among <paramref name="window"/>'s own characters,
    /// placed where it stands in the document — through a body's space, its container's marks put back, for a unit of a body — so every part
    /// laid from it names the document's characters.
    /// </summary>
    private static ContentReading PlacedIn(ContentWindow window, StagedUnit staged, int start)
    {
        if (window.Placing is not var (space, document)) return staged.Placed(start, window.ListedIn);

        var tree = ContentNested.Spliced(staged.Tree, space, start, start + staged.Width);
        var at = Where(window, start);

        return ContentReading.Of(tree, at, document.Stretch(at, tree.Width));
    }

    /// <summary>
    /// Where a stretch of <paramref name="window"/>'s own characters starting at <paramref name="own"/> starts in the document — for a body,
    /// the marks its container put in front of it included.
    /// </summary>
    private static int Where(ContentWindow window, int own) => window.Placing is var (space, _) ? space.Start + space.HolderFrom(own) : own;

    /// <summary>Where <paramref name="place"/>, a place in the document, is among <paramref name="window"/>'s own characters — null for one outside a body's.</summary>
    private static int? OwnIn(ContentWindow window, int place)
    {
        if (window.Placing is not var (space, _)) return place;

        var at = place - space.Start;
        return at < 0 || at > space.HolderAt(space.Length) ? null : space.OwnAt(at);
    }

    /// <summary>
    /// <paramref name="showing"/> as a builder of <paramref name="window"/>'s units is handed it: for a body, what is shown as written where it
    /// stands in the document, which every part laid names.
    /// </summary>
    private static ContentShowing Building(ContentWindow window, ContentShowing showing) =>
        window.Placing is var (space, _) && showing.Shown is { } zone
            ? showing with { Shown = zone with { Start = space.Start + space.HolderAt(zone.Start), End = space.Start + space.HolderAt(zone.End) } }
            : showing;

    /// <summary>Takes in what <paramref name="window"/> has read, to be laid: its units in order, the version they are of, and what it keeps of all of it.</summary>
    private static void Listing(ContentWindow window)
    {
        window.Listed = [.. window.Units.From(0)];
        window.ListedIn = window.Document;
        window.LaidSyntax = window.Units.Syntax;
    }

    /// <summary>
    /// What <paramref name="window"/> has read, laid where <paramref name="wanted"/> says: every unit standing at a height, those it picks out
    /// worked over and laid where their laying no longer stands, and the whole put together (<see cref="Composed"/>). The content's own
    /// reading and a body's are laid the same way.
    /// </summary>
    private Laid Laying(ContentWindow window, ContentLanguage language, ContentShowing showing, double room, bool readOnly, Keeping holding,
                        LayoutStanding standingAs, Func<double[], IReadOnlyCollection<int>> wanted, int unread)
    {
        // A unit a slower reading landed for is laid again where it is wanted, as the rest are: what is laid follows the screen.
    var before = this.Placed(window, language, showing, room, readOnly).Tops;
        var asked = new SortedSet<int>(wanted(before));

        this.Stage(window, language, asked, showing);

        // What every unit of the content's own says to the outline, and what of that the reader folded away.
        if (window.Placing is null)
        {
            InStep();
            Fold();
        }

        return this.Composed(window, language, showing, room, readOnly, holding, standingAs, asked, unread, before);
    }

    /// <summary>
    /// What the reader's folding says of a unit starting at <paramref name="start"/>, <paramref name="width"/> long: each folded
    /// section reaching into it, where in the unit, and how many blocks wait behind a heading of its own — what laying it again turns on.
    /// Nothing, for a unit of a body, which the reader's sections do not fold.
    /// </summary>
    private string Folds(ContentWindow window, int start, int width)
    {
        if (window.Placing is not null) return string.Empty;
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
    private void Views(ContentWindow window, ContentLanguage language)
    {
        _top = language == ContentLanguages.Markdown ? null : _opened.At(0);
        _diagramsBefore = new int[window.Listed.Count];
        if (_top is not null) return;

        var count = 0;

        for (var index = 0; index < window.Listed.Count; index++)
        {
            _diagramsBefore[index] = count;
            count += DiagramsOf(window.Listed[index].Unit.Tree).Length;
        }
    }

    /// <summary>What the reader opened in the diagram whose body starts at <paramref name="start"/>, counted in the order the diagrams are written — or null where none does.</summary>
    private DiagramViewState? Diagram(int start)
    {
        if (_top is not null || this.Holding(start) is not (>= 0 and var index) || index >= _diagramsBefore.Length) return null;

        var (unit, from) = _shown!.Listed[index];
        var at = Array.BinarySearch(DiagramsOf(unit.Tree), start - from);

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
        _shown is not { } shown
            ? []
            : shown.Listed.Where(read => shown.Staged.ContainsKey(read.Unit.Tree)).Select(read => shown.Staged[read.Unit.Tree].Placed(read.Start, shown.ListedIn).Root);

    /// <summary>The reading of the unit holding <paramref name="offset"/>, placed where it starts — or null where nothing is read.</summary>
    private ContentPart? UnitHolding(int offset) => this.Holding(offset) is >= 0 and var index ? this.PlacedAt(index) : null;

    /// <summary>
    /// Which unit of <paramref name="window"/> holds <paramref name="offset"/> — the last starting at or before it, the end of what is read
    /// held by the last — or -1 where it is past what is read, or nothing is.
    /// </summary>
    private static int Holding(ContentWindow window, int offset)
    {
        var units = window.Listed;
        if (units.Count == 0 || offset < 0) return -1;

        var (last, start) = units[^1];
        if (offset > start + last.Width) return -1;

        var (low, high) = (0, units.Count - 1);

        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (units[middle].Start <= offset) low = middle;
            else high = middle - 1;
        }

        return low;
    }

    /// <summary>Which of the content's own units holds <paramref name="offset"/> (see <see cref="Holding(ContentWindow, int)"/>) — -1 before anything is laid.</summary>
    private int Holding(int offset) => _shown is { } shown ? Holding(shown, offset) : -1;

    /// <summary>
    /// The reading of the content's unit at <paramref name="index"/>, placed where it starts — worked over now where nothing has yet, as the
    /// units laid were; null before anything has been laid to say how.
    /// </summary>
    private ContentPart? PlacedAt(int index)
    {
        var shown = _shown!;
        var (unit, start) = shown.Listed[index];
        if (shown.Staged.TryGetValue(unit.Tree, out var staged)) return staged.Placed(start, shown.ListedIn).Root;
        if (shown.StagedBy is not var (language, showing)) return null;

        // What it says to the rest of the document may be new: the outline and the link targets take it in when next laid.
        staged = this.Restaged(shown, language, index, showing);
        Grow();

        return staged.Placed(start, shown.ListedIn).Root;
    }

    /// <summary>The content's own blocks, starting from <paramref name="from"/> up to <paramref name="to"/>, in order across its units.</summary>
    private IEnumerable<ContentPart> Tops(int from = 0, int to = int.MaxValue)
    {
        if (_shown is not { } shown) yield break;

        for (var index = Math.Max(this.Holding(from), 0); index < shown.Listed.Count; index++)
        {
            var (unit, start) = shown.Listed[index];
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
        for (var at = index; at < _shown!.Listed.Count && at <= index + 1; at++)
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
        if (_shown is not { } shown) return;

        foreach (var staged in shown.Staged.Values)
        {
            var forgot = _unchanged.Forget(staged.Tree, which);
            if (!forgot && !(reread && staged.Tree.Children.Any(which))) continue;

            staged.LaidStale = true;
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

        /// <summary>
        /// Whether what it was laid into no longer stands — something it shows has changed since — so it is laid again where it is next laid,
        /// and stands at the height it was laid at until then.
        /// </summary>
        public bool LaidStale { get; set; }

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
    private sealed record LaidUnit(Laid Laid, int Start, ContentPart Root, double Room, bool ReadOnly, string Folds, int SlidVersion);
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
