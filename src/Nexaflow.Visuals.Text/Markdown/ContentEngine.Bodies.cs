using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What the content reads to, kept from one version to the next — and the readings of what is written inside it in another language
/// (a fence's code, the markdown in a fence, a label in a diagram in that markdown), each a reading of its own (<see cref="ContentBody"/>)
/// attached to the piece holding it.
///
/// <para>
/// <strong>Read again from the changes.</strong> Content read whole, being too short to be shown a stretch at a time, is still read as a
/// reading of one unit (<see cref="_whole"/>), and a version made from the last by a change is read again from where the change was
/// written (<see cref="ContentUnits.Edited"/>), exactly as a long document is: so a keystroke inside a fence its language vouches for
/// reads nothing of the document again, and every body read inside it goes with its holder.
/// </para>
/// <para>
/// <strong>A body is found from its holder, not by what it says.</strong> A builder asking for a body to be laid (<see cref="Nested"/>) is
/// answered from the reading the holder was read in — the content's own, or the body holding it, found the same way from its own holder,
/// all the way up (<see cref="Keeping"/>). So laying a document holding a fence megabytes long neither builds nor hashes the fence's
/// characters: its reading is where its holder is. A body is read the first time something asks for it, by its own language, from its own
/// characters (<see cref="ContentSpace"/>) — a stretch at a time where it is long and its language reads that way — and is carried by
/// every change from then on.
/// </para>

/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>What the content as a whole is to a carry (<see cref="Keeping.Address"/>): there is one.</summary>
    private static readonly object Itself = new();

    /// <summary>The content read whole, where it is too short to be shown a stretch at a time: what it read to, and from which version.</summary>
    private ContentWindow? _whole;

    /// <summary>How many times the engine was told that nothing laid is to be set down again as it was (<see cref="Forget"/>).</summary>
    private int _forgotten;

    /// <summary>Each body's stretch of the document, as a document of its own: made the first time it is laid, and kept as long as the body is.</summary>
    private readonly ConditionalWeakTable<ContentBody, ContentDocument> _stretches = new();

    /// <summary>Which reading each tree laid is the root of (<see cref="Keeping.Address"/>), so a carry knows the content it was picked up in.</summary>
    private readonly ConditionalWeakTable<ContentNode, object> _readings = new();

    /// <summary>The window over each body read a stretch at a time, kept against the body through every version of it (<see cref="ContentBody.Continuity"/>).</summary>
    private readonly ConditionalWeakTable<object, ContentWindow> _bodies = new();

    /// <summary>The bodies read a stretch at a time that the page put down as it was last laid out where the reader is.</summary>
    private readonly List<ContentWindow> _onPage = [];

    /// <summary>
    /// <paramref name="document"/> as <paramref name="language"/> reads it whole: read afresh the first time, and after that read again
    /// from where each change since was written — every body read inside it carried with it.
    /// </summary>
    private ContentWindow ReadAgain(ContentLanguage language, string? named, ContentDocument document)
    {
        if (_whole is not { } whole || !ReferenceEquals(whole.Language, language))
        {
            if (!_parses.TryGetValue(language, out var parse)) _parses[language] = parse = language.Parser();

            var reads = ReadingWhole(parse);
            whole = new ContentWindow(language, document, new ContentUnits(named ?? string.Empty).WithSyntax(language.Syntax?.Invoke(document)).ReadOn(reads, document, int.MaxValue), reads)
            {
                Length = int.MaxValue,
                Targets = _targets,
            };
        }
        else if (!ReferenceEquals(whole.Document, document))
        {
            var units = whole.Units;

            foreach (var (change, after) in whole.Following(document) ?? [(document.ChangesFrom(whole.Document).Forward, document)])
            {
                if (change.Writes.Count == 0) continue;

                var (from, to, by) = Spanned(change);
                (units, _, _) = units.Edited(from, to, by, after, whole.Parse, int.MaxValue, bounds: language.Bounds);
            }

            whole.Became(document, units);
        }

        _whole = whole;
        return whole;
    }

    /// <summary><paramref name="parse"/>, which reads a whole string, as a windowed parse reading its stretch to the end.</summary>
    private static Func<ContentStretch, ContentParse> ReadingWhole(Func<string, ContentParse> parse) => stretch => parse(stretch.Window);

    /// <summary>
    /// How a body <paramref name="length"/> characters long written in <paramref name="language"/> is read: a stretch at a time, where it
    /// is long enough and its language reads that way, and otherwise whole.
    /// </summary>
    private ContentReader Reader(ContentLanguage language, int length)
    {
        if (language.Windowed is { } windowed && length >= WindowedFrom) return new(windowed(), StretchLength, language.Bounds, language.Syntax);

        if (!_nestedParses.TryGetValue(language, out var parse)) _nestedParses[language] = parse = language.Parser();

        return new(ReadingWhole(parse), int.MaxValue, language.Bounds, language.Syntax);
    }

    /// <summary>
    /// The reading of the body held by the holder starting at <paramref name="at"/> in <paramref name="holding"/>, written in
    /// <paramref name="language"/> and <paramref name="length"/> characters long: the one kept there, and otherwise read now, and kept.
    /// </summary>
    private ContentBody? Body(Keeping holding, int at, string named, ContentLanguage language, int length)
    {
        if (holding.Held(at) is { } kept && kept.Language == named) return kept;

        return holding.Read(at, named, Reader(language, length));
    }

    /// <summary>
    /// A body nothing it was read in holds — content a binding supplied, written nowhere in the document — read from its characters as
    /// they are, and kept by nothing.
    /// </summary>
    private ContentBody Unheld(string named, ContentLanguage language, ContentPart body)
    {
        var own = ContentDocument.Of(ContentNested.Own(body.Node));

        return ContentBody.Read(named, ContentSpace.Whole(own), 0, body.Length, Reader(language, own.Length));
    }

    /// <summary>The stretch of the document <paramref name="body"/>'s own characters stand in, as a document of its own — made once for each reading.</summary>
    private ContentDocument Stretched(ContentBody reading, ContentDocument document, ContentPart body) =>
        _stretches.GetValue(reading, _ => body.Supplied ? ContentDocument.Of(body.Node.Print()[..ContentNested.Own(body).Length])
                                                        : document.Stretch(body.Start, ContentNested.Own(body).Length));

    /// <summary>
    /// A body read a stretch at a time, laid as the content is (<see cref="Laying"/>): its window kept against the body, the units near the
    /// screen worked over and laid on their own — every unit, where the content is laid whole — the rest standing at their heights, and the
    /// whole put together, as tall as all of it, into a tree holding each unit laid as the tree it was laid as: what its holder sets down
    /// copies that tree's top and a piece for each unit, and none of what the units hold.
    /// </summary>
    private Laid BodyLaid(ContentBody reading, ContentLanguage language, string named, ContentPart holder, ContentPart body, ContentSpace placed,
                          Keeping within, Keeping holding, LayoutStyle style, RawZone? shown, bool writing, LayoutStanding standing, double room)
    {
        var characters = Stretched(reading, holding.Document, body);

        if (!_bodies.TryGetValue(reading.Continuity, out var window) || !ReferenceEquals(window.Language, language))
        {
            window = new ContentWindow(language, characters, reading.Units, reading.Reader.Parse)
            {
                Length = reading.Reader.SoftMaxLength,
                Targets = new LinkTargets(),
            };

            _bodies.AddOrUpdate(reading.Continuity, window);
        }
        else if (!ReferenceEquals(window.Units, reading.Units))
        {
            window.Became(characters, reading.Units);
        }

        window.Placing = (placed, holding.Document);
        window.Holder = _askingIn is { } held && _asking is { } unit && OwnIn(held, holder.Start) is { } at ? (held, unit, at) : null;
        Listing(window);

        // What is shown as written is where it is among the body's own characters, which its units are counted in.
        var own = shown is { } zone && ContentNested.Holds(body, zone.Start, zone.End)
            ? zone with { Start = placed.OwnAt(zone.Start - body.Start), End = placed.OwnAt(zone.End - body.Start) }
            : (RawZone?)null;

        var showing = Showing(named, style, writing, own, 0) with
        {
            Unchanged = _unchanged,
            Targets = window.Targets,
            Syntax = window.LaidSyntax is { } syntax ? (syntax, 0) : null,
            WrapsLines = language.WrapsLines,
        };

    return this.Laying(window, language, showing, room, !(language.Writable && writing), within, standing, tops => this.BodyWanted(window, tops), 0);
    }

    /// <summary>
    /// The units of the body <paramref name="window"/> is over to lay, at the places <paramref name="tops"/> gives them among its own: every
    /// one, where the content is laid whole; otherwise those the screen and a screen either side of it reach — going by where the body stood
    /// on the page when last put down, and from its start before it has been — the caret's and the one either side, each end of what is
    /// picked out, and what the reader went to.
    /// </summary>
    private SortedSet<int> BodyWanted(ContentWindow window, double[] tops)
    {
        var units = window.Listed;
        var wanted = new SortedSet<int>();

        if (!_windowing)
        {
            for (var index = 0; index < units.Count; index++) wanted.Add(index);
            return wanted;
        }

        var reach = Math.Max(_showing?.Height ?? Room, 1);
        var (top, bottom) = window.PageTop is { } stood && _showing is { } screen ? (screen.Y - reach - stood, screen.Bottom + reach - stood) : (0, reach * 2);

        for (var index = Reaching(tops, top); index < units.Count && tops[index] < bottom; index++) wanted.Add(index);

        if (OwnIn(window, _state.Caret) is { } caret && Holding(window, caret) is >= 0 and var holding)
            for (var near = Math.Max(holding - 1, 0); near <= Math.Min(holding + 1, units.Count - 1); near++) wanted.Add(near);

        foreach (var range in _state.Selection)
            foreach (var end in new[] { range.Start, range.End })
                if (OwnIn(window, end) is { } own && Holding(window, own) is >= 0 and var at) wanted.Add(at);

        if (_reveal is { } target && OwnIn(window, target) is { } revealed && Holding(window, revealed) is >= 0 and var reached) wanted.Add(reached);

        return wanted;
    }

    /// <summary>Whether the body <paramref name="window"/> is over, laid where the reader is, falls short of what it has to reach: a unit of it to lay that is not laid.</summary>
    private bool BodyShort(ContentWindow window)
    {
        if (window.Standing.Length != window.Listed.Count) return true;

        foreach (var index in this.BodyWanted(window, window.Tops))
            if (!window.Standing[index] && this.Shows(window, index)) return true;

        return false;
    }

    /// <summary>Says of every body on the page that falls short of what it has to reach that its holder is to be laid again (<see cref="Unlay"/>) — and whether any did.</summary>
    private bool BodiesShort()
    {
        var any = false;

        foreach (var body in _onPage)
        {
            if (!this.BodyShort(body)) continue;

            this.Unlay(body);
            any = true;
        }

        return any;
    }

    /// <summary>
    /// Says the block holding the body <paramref name="window"/> is over is to be laid again where its unit is next laid — that block and
    /// nothing else of its unit, which is set down as it was — and so on up through every body holding that one.
    /// </summary>
    private void Unlay(ContentWindow window)
    {
        if (window.Holder is not var (holding, unit, at) || !holding.Staged.TryGetValue(unit, out var staged)) return;

        // The block of the unit's own the holder starts in, counted among the unit's own characters.
        var inside = Holding(holding, at) is >= 0 and var index ? at - holding.Listed[index].Start : at;
        ContentNode? block = null;
        var from = 0;

        foreach (var child in staged.Tree.Children)
        {
            var begins = child.Offset ?? from;
            if (inside >= begins && inside < begins + child.Width) { block = child; break; }

            from = begins + child.Width;
        }

        _unchanged.Forget(staged.Tree, node => ReferenceEquals(node, block));
    staged.LaidStale = true;

        if (holding.Placing is not null) this.Unlay(holding);
    }

    /// <summary>
    /// Finds every body read a stretch at a time that <paramref name="page"/> puts down, and where on the page each stands — going by a unit
    /// of it laid, found down the trees put down in the page: its units, the units of bodies put down in those, and so on, going into a unit
    /// of a body only where it holds bodies of its own.
    /// </summary>
    private void FoundOnPage(LayoutTree page)
    {
        _onPage.Clear();

        // Each unit of a body laid, by the drawing it was laid as: a body is where any of them is put down, less where that unit stands in it.
        var laidUnits = new Dictionary<object, (ContentWindow Body, int Index)>(ReferenceEqualityComparer.Instance);

        foreach (var (_, body) in _bodies)
            for (var index = 0; index < body.Listed.Count && index < body.Standing.Length; index++)
                if (body.Standing[index] && body.Staged.TryGetValue(body.Listed[index].Unit.Tree, out var staged) && staged.Laid is { } laid)
                    laidUnits[laid.Laid.Tree.Drawing] = (body, index);

        if (laidUnits.Count > 0) Looked(page, null, units: true);

        // A tree whose mounts are units is gone down into; in a unit, only a unit of a body is, and that only where it holds bodies itself.
        void Looked(LayoutTree tree, LayoutMounted? within, bool units)
        {
            foreach (var (at, mount) in tree.Mounts)
            {
                var found = laidUnits.TryGetValue(mount.Tree.Drawing, out var unit);
                if (!found && !units) continue;

                var mounted = new LayoutMounted(tree, at, within);

                if (!found)
                {
                    Looked(mount.Tree, mounted, units: false);
                    continue;
                }

                var (body, index) = unit;

                if (!_onPage.Contains(body))
                {
                    body.PageTop = new LayoutPiece(mount.Tree, 0, mounted).Anchor.Y - body.Tops[index];
                    _onPage.Add(body);
                }

                if (body.Listed[index].Unit.Bodies.Count > 0) Looked(mount.Tree, mounted, units: false);
            }
        }
    }

    /// <summary>The content's own reading and every body's read a stretch at a time.</summary>
    private IEnumerable<ContentWindow> Windows()
    {
        if (_shown is { } shown) yield return shown;
        foreach (var (_, body) in _bodies) yield return body;
    }

    /// <summary>The reading <paramref name="root"/> was laid as the root of (<see cref="Keeping.Address"/>) — or the tree itself, where it was laid as none.</summary>
    private object ReadingOf(ContentNode root) => _readings.TryGetValue(root, out var address) ? address : root;

    /// <summary>
    /// A reading the bodies written inside it are kept in: the content's own, or a body's. Each is found from where its holder starts,
    /// and every place it is asked about is a place in the document, which the trees laid name.
    /// </summary>
    private abstract class Keeping(ContentDocument document)
    {
        /// <summary>The document being laid.</summary>
        public ContentDocument Document { get; } = document;

        /// <summary>What the reading is to a carry: the same through every laying out of the same content, and different for every other reading.</summary>
        public abstract object Address { get; }

        /// <summary>Where <paramref name="place"/>, a place in the document, stands among the characters this reading reads.</summary>
        public abstract int At(int place);

        /// <summary>The reading of the body held by the holder starting at <paramref name="at"/>, or null where none is read.</summary>
        public abstract ContentBody? Held(int at);

        /// <summary>Keeps <paramref name="body"/> as the reading of the body held by the holder starting at <paramref name="at"/>.</summary>
        public abstract void Keep(int at, ContentBody body);

        /// <summary>
        /// Reads the body held by the holder starting at <paramref name="at"/>, written in <paramref name="language"/>, by
        /// <paramref name="reader"/>, and keeps it — or null where nothing starting there holds one.
        /// </summary>
        public abstract ContentBody? Read(int at, string language, ContentReader reader);
    }

    /// <summary>The content's own reading: what a window, or the content read whole, has read.</summary>
    private sealed class KeptInContent(ContentWindow window, ContentDocument document) : Keeping(document)
    {
        public override object Address => Itself;

        public override int At(int place) => place;

        public override ContentBody? Held(int at) => window.Units.Body(at);

        public override void Keep(int at, ContentBody body) => window.Units = window.Units.Holding(at, body);

        public override ContentBody? Read(int at, string language, ContentReader reader)
        {
            if (window.Units.Reading(at, language, reader, window.Document) is not var (units, body)) return null;

            window.Units = units;
            return body;
        }
    }

    /// <summary>
    /// A body's reading, held by the holder starting at <paramref name="at"/> in <paramref name="holder"/>, the body starting at
    /// <paramref name="start"/> in the document — <paramref name="placed"/> saying where its own characters stand there, past the
    /// marks of every container they are written in.
    /// </summary>
    private sealed class KeptInBody(Keeping holder, int at, int start, ContentSpace placed) : Keeping(holder.Document)
    {
        public override object Address { get; } = (holder.Address, at);

        /// <summary>The body, as its holder keeps it now.</summary>
        private ContentBody Body => holder.Held(at) ?? throw new InvalidOperationException($"No reading is kept for the body held at {at}.");

        public override int At(int place) => placed.OwnAt(place - start);

        public override ContentBody? Held(int place) => this.Body.Units.Body(place);

        public override void Keep(int place, ContentBody body) => holder.Keep(at, this.Body.Holding(place, body));

        public override ContentBody? Read(int place, string language, ContentReader reader)
        {
            if (this.Body.Reading(place, language, reader) is not var (holding, body)) return null;

            holder.Keep(at, holding);
            return body;
        }
    }

    /// <summary>What nothing holds — content a binding supplied — keeps nothing, and is found again by nothing.</summary>
    private sealed class KeptNowhere(ContentDocument document) : Keeping(document)
    {
        public override object Address { get; } = new();

        public override int At(int place) => place;

        public override ContentBody? Held(int at) => null;

        public override void Keep(int at, ContentBody body)
        {
        }

        public override ContentBody? Read(int at, string language, ContentReader reader) => null;
    }
}
