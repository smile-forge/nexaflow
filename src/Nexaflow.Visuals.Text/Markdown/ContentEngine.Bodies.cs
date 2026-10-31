using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

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
/// <para>
/// <strong>What a slower reading changes.</strong> A body read before its language's second reading landed
/// (<see cref="ContentParse.Provisional"/>) is read again once it has — looked into only after something has landed
/// (<see cref="Forget"/>), never on every laying out.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>What the content as a whole is to a carry (<see cref="Keeping.Address"/>): there is one.</summary>
    private static readonly object Itself = new();

    /// <summary>The content read whole, where it is too short to be shown a stretch at a time: what it read to, and from which version.</summary>
    private ContentWindow? _whole;

    /// <summary>How many times what was read has been said to be stale (<see cref="Forget"/>), which a provisional body is checked against.</summary>
    private int _forgotten;

    /// <summary>Each provisional body, with how many times what was read had been said to be stale when it was last looked into.</summary>
    private readonly ConditionalWeakTable<ContentBody, StrongBox<int>> _checked = new();

    /// <summary>Each body's stretch of the document, as a document of its own: made the first time it is laid, and kept as long as the body is.</summary>
    private readonly ConditionalWeakTable<ContentBody, ContentDocument> _stretches = new();

    /// <summary>Which reading each tree laid is the root of (<see cref="Keeping.Address"/>), so a carry knows the content it was picked up in.</summary>
    private readonly ConditionalWeakTable<ContentNode, object> _readings = new();

    /// <summary>
    /// <paramref name="document"/> as <paramref name="language"/> reads it whole: read afresh the first time, and after that read again
    /// from where each change since was written — every body read inside it carried with it.
    /// </summary>
    private Parsed ReadAgain(ContentLanguage language, string? named, ContentDocument document)
    {
        if (_whole is not { } whole || !ReferenceEquals(whole.Language, language))
        {
            if (!_parses.TryGetValue(language, out var parse)) _parses[language] = parse = language.Parser();

            var reads = ReadingWhole(language, parse);
            whole = new ContentWindow(language, document, new ContentUnits(named ?? string.Empty).ReadOn(reads, document, int.MaxValue), reads)
            {
                Length = int.MaxValue,
                Taken = 1,
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

            whole = new ContentWindow(language, document, units, whole.Parse) { Length = int.MaxValue, Taken = 1 };
        }

        _whole = whole;
        return new Parsed(language, named ?? string.Empty, document, whole.Units.Tree(document));
    }

    /// <summary>
    /// <paramref name="parse"/>, which reads a whole string, as a windowed parse reading its stretch to the end — by the language's second
    /// reading where that has landed for these characters (<see cref="Read(ContentLanguage, Func{string, ContentParse}, string)"/>).
    /// </summary>
    private Func<ContentStretch, ContentParse> ReadingWhole(ContentLanguage language, Func<string, ContentParse> parse) =>
        stretch => Read(language, parse, stretch.Window);

    /// <summary>
    /// How a body <paramref name="length"/> characters long written in <paramref name="language"/> is read: a stretch at a time, where it
    /// is long enough and its language reads that way, and otherwise whole.
    /// </summary>
    private ContentReader Reader(ContentLanguage language, int length)
    {
        if (language.Windowed is { } windowed && length >= WindowedFrom) return new(windowed(), StretchLength, language.Bounds);

        if (!_nestedParses.TryGetValue(language, out var parse)) _nestedParses[language] = parse = language.Parser();

        return new(ReadingWhole(language, parse), int.MaxValue, language.Bounds);
    }

    /// <summary>
    /// The reading of the body held by the holder starting at <paramref name="at"/> in <paramref name="holding"/>, written in
    /// <paramref name="language"/> and <paramref name="length"/> characters long: the one kept there, unless its language's second
    /// reading has landed since it was read; and otherwise read now, and kept.
    /// </summary>
    private ContentBody? Body(Keeping holding, int at, string named, ContentLanguage language, int length)
    {
        if (holding.Held(at) is { } kept && kept.Language == named && !LandedSince(kept, language)) return kept;

        return holding.Read(at, named, Reader(language, length));
    }

    /// <summary>Whether <paramref name="body"/> was read before its language's second reading of it, which has landed since.</summary>
    private bool LandedSince(ContentBody body, ContentLanguage language)
    {
        if (body.Units is not { Count: 1 } units || !units[0].Unit.Provisional) return false;

        var seen = _checked.GetValue(body, static _ => new StrongBox<int>(-1));
        if (seen.Value == _forgotten) return false;

        seen.Value = _forgotten;
        return HasLanded(new Kept(language, null, ContentDocument.Of(body.Space.Slice(0, body.Space.Length))));
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
