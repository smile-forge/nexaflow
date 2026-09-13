using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Latex;
using XamlMath.Exceptions;
using XamlMath.Parsers;
using XamlMath.Parsers.Matrices;
using Nexaflow.Markdown.Ast;
using XamlMath;
using TexEnvironment = XamlMath.TexEnvironment;
using XamlMath.Rendering.Transformations;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// The setting half: a formula's reading turned into measured pieces, each construct set the way TeX sets it.
///
/// <para>
/// Handed the reading and never the string, so nothing here can name a point in the source. What a piece came
/// from is its part; where that part is written is the reading's to answer.
/// </para>
/// <para>
/// A construct is set as it is reached, in the environment its parent chose, and comes back a <see cref="Set"/>: the
/// room it takes and a way to draw it once its parent has decided where. Anything nothing here has a drawing for is
/// set as the characters it was written with, so a formula comes back whole whatever it holds.
/// </para>
/// <para>
/// <b>Space that was typed is not built; space that was asked for is.</b> TeX takes the gaps between symbols from
/// their classes, not from what was typed, so <c>a+b</c> and <c>a + b</c> are set identically. <c>\,</c>,
/// <c>\quad</c> and the rest are the writer overriding that, so they are: most as macros expanded while the formula is
/// read, and the handful that are a length in mu with no LaTeX spelling by <see cref="PrimitiveItem"/>.
/// </para>
/// </summary>
public sealed partial class LatexBuilder
{
    /// <summary>
    /// Whether the handful of disagreements parked for review are declined — which is what parking one
    /// means, and the default.
    /// <para>
    /// Turned off to look at one. A decline is never exercised, so it goes stale without anything saying
    /// so: *a script on `\overline`* sat on the list for weeks describing a difference that was not the
    /// difference, over ten times as many formulas as recorded, and it stayed there because looking took
    /// an edit to this file. It takes a line now.
    /// </para>
    /// <para>
    /// It changes what the builder draws, so it is not a thing to leave off. Nothing but a diagnostic
    /// sets it.
    /// </para>
    /// </summary>
    internal static bool DeclineUnsettled = true;

    /// <summary>
    /// Where <see cref="Words"/> puts what it drew nothing for, while a build is running.
    ///
    /// <para>
    /// Held here rather than threaded through, because it would otherwise have to pass through every
    /// method between the two — a parameter twenty signatures wide to carry something two of them use.
    /// Per thread, because the corpus sweep builds on all of them at once, and restored rather than
    /// cleared so that a build nested inside another gives its findings to its own formula.
    /// </para>
    /// </summary>
    [System.ThreadStatic]
    private static List<ContentPart>? _ignored;

    /// <summary>
    /// An equation's number — the <c>\tag</c> written in the reading — as a formula of its own, or null where there is
    /// none.
    ///
    /// <para>
    /// Not part of what <see cref="Build"/> makes, and not carried on it. LaTeX sets the number against the right edge
    /// of the block the equation is displayed in, wherever the <c>\tag</c> was written, and where that edge is belongs to
    /// whatever lays the block out — so the number is handed over on its own, for that to place.
    /// </para>
    /// </summary>
    internal static Set? Number(ContentPart root, TexEnvironment environment)
    {
        System.ArgumentNullException.ThrowIfNull(root);

        // The last one written. A second \tag in one equation is an error to LaTeX, and there is no second place to
        // put it.
        if (root.SelfAndDescendants().LastOrDefault(IsTag) is not { } tag
            || tag.Part(TexRole.Argument) is not { } written)
            return null;

        var number = Inside(written);
        var starred = tag.Part(Roles.Name)?.Text == @"\tag*";
        return LettersItem(starred ? number : $"({number})", TexUtilities.TextStyleName, spaced: true, tag).Make(environment, null);
    }

    // ── Setting ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A formula's reading set in the environment it is displayed in — measured, and ready to lay — with whatever in it
    /// nothing could draw. Null where nothing in it builds at all.
    /// <para>
    /// The way in that replaces <see cref="Build"/>. That one makes atoms, and the atoms are boxed later in whatever
    /// environment somebody hands them; this sets each construct as it is reached, in the environment its parent chose,
    /// so the atoms stop being a tree of their own. Constructs move here a family at a time, and one that has not moved
    /// yet is still built as an atom and boxed on the spot — which is why both ways in exist for now.
    /// </para>
    /// </summary>
    internal static (Set? Set, IReadOnlyList<ContentPart> Ignored) Formula(
        ContentPart root, TexEnvironment environment, TexFormulaParser knowledge)
    {
        System.ArgumentNullException.ThrowIfNull(root);

        var ignored = new List<ContentPart>();
        var was = _ignored;
        _ignored = ignored;

        try
        {
            var set = Sequence(root.Parts, root, null, knowledge)?.Make(environment, null);
            return (set, ignored);
        }
        finally
        {
            _ignored = was;
        }
    }

    /// <summary>
    /// One piece of a run before it is set: its TeX class at either end, and how to set it once the run knows what came
    /// before it.
    /// <para>
    /// The class is known before anything is measured, which is what lets a run decide its spacing: TeX turns a binary
    /// operator into an ordinary atom by looking at its neighbours, and the gaps between atoms come from their classes.
    /// <see cref="Glyph"/> is the character a piece is, where it is one — a run asks it whether two letters kern or join
    /// into a ligature, and a script how far to tuck under its lean.
    /// </para>
    /// </summary>
    private sealed record Item(TexAtomType Left, TexAtomType Right, Glyph? Glyph, System.Func<TexEnvironment, Previous?, Set> Make)
    {
        /// <summary>Whether this is a kern: room that takes no part in the spacing rules around it.</summary>
        public bool IsKern { get; init; }

        /// <summary>
        /// The letter an accent over this would lean with — the character at the bottom of any accents already on it.
        /// </summary>
        public Glyph? Nucleus { get; init; }

        /// <summary>
        /// Whether what this lays is another piece's drawing rather than its own, as a group's is its contents'. Such a
        /// piece keeps naming what that drawing named when something else claims it.
        /// </summary>
        public bool PassesThrough { get; init; }

        /// <summary>What a row would take in place of this piece, where it is itself a row.</summary>
        public IReadOnlyList<Item>? Elements { get; init; }

        /// <summary>
        /// Whether this is a piece retyped as a class of its own — a group, <c>\mathop</c> and its family. Claimed by
        /// something else, it hands the claim on to what it holds wherever that names nothing, or only the zero-length
        /// part of an expansion: it is how a macro that expands to one comes to be pointable at all.
        /// </summary>
        public bool Retyped { get; init; }

        /// <summary>
        /// Where this is a big operator, its sign and how it asked to wear limits — so a script arriving later builds
        /// the operator again round the same sign rather than nesting an operator inside one.
        /// </summary>
        public (Item? Sign, bool? Vertical)? Operator { get; init; }
    }

    /// <summary>What stood before a piece of a run: its class on the side facing it, and whether it was a kern.</summary>
    private readonly record struct Previous(TexAtomType Right, bool IsKern);

    /// <summary>Several things in a row, or the one thing when there is only one — as <see cref="Run"/>.</summary>
    private static Item? Sequence(IEnumerable<ContentPart> parts, ContentPart whole, string? style, TexFormulaParser knowledge)
    {
        var built = Pieces(parts, style, knowledge);
        if (built is null || built.Count == 0) return null;

        return built.Count == 1 ? built[0] : Sequenced(built, whole);
    }

    /// <summary>A run of pieces, as <see cref="Built"/>: a switch takes the rest of its group, and a piece nothing can draw is its characters.</summary>
    private static List<Item>? Pieces(IEnumerable<ContentPart> parts, string? style, TexFormulaParser knowledge)
    {
        var built = new List<Item>();
        var run = parts.ToList();

        for (var at = 0; at < run.Count; at++)
        {
            if (Switch(run[at]) is { } switched)
            {
                if (Pieces(run.Skip(at + 1), switched.TextStyle ?? style, knowledge) is not { } after)
                    return null;

                var scope = after.Count switch
                {
                    0 => NullItem(),
                    1 => after[0],
                    _ => Sequenced(after, run[at]),
                };

                built.Add(switched.Style is { } size ? Styled(scope, size, run[at]) : scope);
                break;
            }

            if (IsTag(run[at])) continue;
            if (Discarded(run[at])) continue;

            built.Add(Piece(run[at], style, knowledge) ?? UnreadItem(run[at], style));
        }

        return built;
    }

    /// <summary>One piece of a run — a construct set here, or one still built as an atom.</summary>
    private static Item? Piece(ContentPart part, string? style, TexFormulaParser knowledge) =>
        part.Kind switch
        {
            Kinds.Sequence => Sequence(part.Parts, part, style, knowledge),
            TexKinds.Group when !part.Parts.Any() => NullItem(),
            TexKinds.Group when Written(part) => Grouped(part, style, knowledge),
            TexKinds.Group => Sequence(part.Parts, part, style, knowledge),
            TexKinds.Script => Scripted(part, style, knowledge),
            TexKinds.Command => Commanded(part, style, knowledge),
            TexKinds.Fence => Fenced(part, style, knowledge),
            TexKinds.Environment => Environmented(part, style, knowledge),
            Kinds.Char => CharacterItem(part, style),
            Kinds.Verbatim => LettersItem(part.Text, style, spaced: true, part),
            Kinds.Hole => HoleItem(part),
            _ => null,
        };

    /// <summary>
    /// A braced group, as <see cref="Group"/>: an ordinary atom whatever it holds, because braces change a class. What
    /// it holds is set as it would be anywhere, and names the group where it names nothing of its own.
    /// </summary>
    private static Item? Grouped(ContentPart part, string? style, TexFormulaParser knowledge)
    {
        if (Sequence(part.Parts, part, style, knowledge) is not { } inner) return null;

        return new Item(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (environment, _) =>
        {
            var set = inner.Make(environment, null);
                return set.Part is null or { Length: 0 } ? set with { Part = part } : set;
            })
            {
            PassesThrough = true,
                Retyped = true,
            };
    }

    /// <summary>A scope set in a style of its own — <c>\displaystyle</c> and its family — as the rest of its group.</summary>
    private static Item Styled(Item scope, TexStyle size, ContentPart part) =>
        new(scope.Left, scope.Right, null, (environment, _) =>
        {
            var set = scope.Make(environment with { Style = size }, null);
                return set.Part is null ? set with { Part = part } : set;
            })
            {
                PassesThrough = true,
            };

    /// <summary>Pieces standing in a row, as one piece: its class is its first piece's on the left and its last piece's on the right.</summary>
    private static Item Sequenced(List<Item> items, ContentPart? whole) =>
        new(items.Count == 0 ? TexAtomType.Ordinary : items[0].Left, items.Count == 0 ? TexAtomType.Ordinary : items[^1].Right, null, (environment, previous) => Row(items, whole, environment, previous))
        {
            Elements = items,
        };

    // ── Leaves ──────────────────────────────────────────────────────────────

    /// <summary>A glyph standing as a piece of a run.</summary>
    private static Item GlyphItem(Glyph glyph) =>
        new(glyph.Type, glyph.Type, glyph, (environment, _) => glyph.Set(environment))
        {
            Nucleus = glyph,
        };

    /// <summary>
    /// Room of a width and height measured in a unit — or, with no unit, an inter-word space in the current font. A kern:
    /// it takes no part in the spacing rules around it.
    /// </summary>
    private static Item SpaceItem(TexUnit? unit, double width, double height = 0, double depth = 0) =>
        new(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (environment, _) => Room(unit, width, height, depth, environment))
        {
            IsKern = true,
        };

    private static Set Room(TexUnit? unit, double width, double height, double depth, TexEnvironment environment) =>
        unit is { } measured
            ? Strut(width * Conversion(measured, environment), height * Conversion(measured, environment),
                    depth * Conversion(measured, environment))
            : Strut(environment.MathFont.GetSpace(environment.Style), 0, 0);

    /// <summary>How long one of a unit is in the environment given: an em and an ex are an x-height here, a mu an eighteenth of a quad.</summary>
    private static double Conversion(TexUnit unit, TexEnvironment environment) => unit switch
    {
        TexUnit.Em or TexUnit.Ex => environment.MathFont.GetXHeight(environment.Style, environment.LastFontId),
        TexUnit.Pixel => 1.0 / environment.MathFont.Size,
        TexUnit.Point => TexFontUtilities.PixelsPerPoint / environment.MathFont.Size,
        TexUnit.Pica => 12 * TexFontUtilities.PixelsPerPoint / environment.MathFont.Size,
        _ => environment.MathFont.GetQuad(environment.MathFont.GetMuFontId(), environment.Style) / 18,
    };

    /// <summary>Nothing, of no size, still an ordinary piece of its run — an empty group, a cell nobody wrote.</summary>
    private static Item NullItem() =>
        new(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (_, _) => Strut(0, 0, 0));

    /// <summary>The hollow box standing where an argument has still to be written.</summary>
    private static Item HoleItem(ContentPart part) =>
        new(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (environment, _) =>
            Set.Placeholder(environment) with { Part = part });

    /// <summary>One character of the reading, as <see cref="Character"/>: a symbol by the table, a letter, or a tie.</summary>
    private static Item? CharacterItem(ContentPart part, string? style)
    {
        if (part.Text.Length != 1) return null;

        var character = part.Text[0];
        if (character == '\'') return null;
        if (character == '~') return SpaceItem(null, 0);

        return GlyphItem(TexFormulaParser.GlyphOf(character, style) with { Origin = part });
    }

    /// <summary>A stretch set as the characters it is written with, as <see cref="Letters"/>, standing for <paramref name="origin"/>.</summary>
    private static Item LettersItem(string text, string? style, bool spaced, ContentPart? origin)
    {
        style ??= TexUtilities.TextStyleName;

        if (!spaced && text.Length == 1 && !char.IsWhiteSpace(text[0]))
            return GlyphItem(Glyph.Letter(text[0], style) with { Origin = origin });

        var letters = new List<Item>();
        if (spaced) letters.Add(SpaceItem(TexUnit.Mu, 3));

        foreach (var letter in text)
            letters.Add(char.IsWhiteSpace(letter) ? SpaceItem(null, 0) : GlyphItem(Glyph.Letter(letter, style)));

        if (spaced) letters.Add(SpaceItem(TexUnit.Mu, 3));

        return Sequenced(letters, origin);
    }

    /// <summary>A piece nothing here can draw, set as its characters and reported, as <see cref="Unread"/>.</summary>
    private static Item UnreadItem(ContentPart part, string? style)
    {
        if (!part.SelfAndDescendants().Any(piece => piece.Trouble is not null)) _ignored?.Add(Whole(part));

        return LettersItem(part.Node.Print(), style, spaced: true, part);
    }

    /// <summary>A command nothing anywhere knows, set as what was typed and reported, as <see cref="Words"/>.</summary>
    private static Item? WordsItem(ContentPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(Roles.Name) is not { Text: { } name } named || knowledge.Knows(name[1..])) return null;

        if (named.Trouble is null) _ignored?.Add(Whole(part));

        return LettersItem(part.Node.Print(), style, spaced: true, part);
    }

    /// <summary>A symbol standing on its own, as <see cref="Symbol"/> — a big operator whatever its limits, or a primitive the tables draw.</summary>
    private static Item? SymbolItem(string name, ContentPart part)
    {
        if (Glyph.Symbol(name) is not { } glyph) return PrimitiveItem(name, part);

        glyph = glyph with { Origin = part };

        return glyph.Type == TexAtomType.BigOperator
            ? Operator(GlyphItem(glyph), null, null, TexFormulaParser.SetsLimitsBeside(name) ? false : null, part)
            : GlyphItem(glyph);
    }

    /// <summary>What a name draws that LaTeX has no spelling for.</summary>
    private static Item? PrimitiveItem(string name, ContentPart? part)
    {
        if (StandardCommands.StrutOf(name) is { } mu) return SpaceItem(TexUnit.Mu, mu);

        return name switch
        {
            // A radical sign with nothing under it, lifted so it sits about the axis.
            "surd" => Made(TexAtomType.Ordinary, part, environment =>
            {
                var sign = Glyph.Symbol("surdsign")!.Set(environment);
                sign = sign with
                {
                    Shift = -((sign.Height + sign.Depth) / 2) - environment.MathFont.GetAxisHeight(environment.Style),
                };
                return Horizontal([sign], null, null);
            }),

            // A dot or a tilde set over an equals sign at a fixed height, and a relation either side.
            "doteq" => Pile("equals", "ldotp", 2, part),
            "cong" => Pile("equals", "sim", 1, part),

            _ => null,
        };
    }

    private static Item Pile(string under, string over, double mu, ContentPart? part) =>
        Typed(UnderOver(GlyphItem(Glyph.Symbol(under)!), GlyphItem(Glyph.Symbol(over)!), TexUnit.Mu, mu,
                        smaller: false, over: true, part),
              TexAtomType.Relation, part);

    /// <summary>A delimiter at one of the four set sizes of <c>\big</c> … <c>\Bigg</c>, centred on the axis.</summary>
    private static Item SizedDelimiter(Glyph delimiter, double minHeight, TexAtomType type, ContentPart part) =>
        new(type, type, null, (environment, _) =>
        {
            var set = Delimiter(delimiter.SymbolName!, minHeight, environment);
            set = set with
            {
                Shift = -((set.Height + set.Depth) / 2 - set.Height) - environment.MathFont.GetAxisHeight(environment.Style),
            };
            return Horizontal([set], null, null) with { Part = part };
        });

    /// <summary>
    /// A delimiter at least as tall as asked: the first of the font's sizes that reaches it, or else one assembled from
    /// the font's extension pieces — a top, a middle, a bottom, and a repeated piece between them until it is tall enough.
    /// </summary>
    private static Set Delimiter(string symbol, double minHeight, TexEnvironment environment)
    {
        var texFont = environment.MathFont;
        var style = environment.Style;
        var charInfo = texFont.GetCharInfo(symbol, style).Value;

        // The first version of the character with at least the height asked for.
        var totalHeight = charInfo.Metrics.Height + charInfo.Metrics.Depth;
        while (totalHeight < minHeight && texFont.HasNextLarger(charInfo))
        {
            charInfo = texFont.GetNextLargerCharInfo(charInfo, style);
            totalHeight = charInfo.Metrics.Height + charInfo.Metrics.Depth;
        }

        // Tall enough, or as tall as it comes and with nothing to build a taller one from.
        if (totalHeight >= minHeight || !texFont.IsExtensionChar(charInfo))
            return Set.Glyph(environment, charInfo);

        var extension = texFont.GetExtension(charInfo, style);
        var pieces = new List<Set>();
        if (extension.Top != null) pieces.Add(Set.Glyph(environment, extension.Top));
        if (extension.Middle != null) pieces.Add(Set.Glyph(environment, extension.Middle));
        if (extension.Bottom != null) pieces.Add(Set.Glyph(environment, extension.Bottom));

        if (extension.Repeat != null)
        {
            var repeat = Set.Glyph(environment, extension.Repeat);
            do
            {
                if (extension.Top != null && extension.Bottom != null)
                {
                    pieces.Insert(1, repeat);
                    if (extension.Middle != null) pieces.Insert(pieces.Count - 1, repeat);
                }
                else if (extension.Bottom != null)
                {
                    pieces.Insert(0, repeat);
                }
                else
                {
                    pieces.Add(repeat);
                }
            } while (pieces.Sum(piece => piece.Height + piece.Depth) < minHeight);
        }

        return Vertical(pieces);
    }

    /// <summary>A drawn rule sized in a unit — <c>\_</c>, which the text encoding has no glyph for.</summary>
    private static Item RuleItem(TexUnit unit, double width, double thickness, double shift, ContentPart part) =>
        new(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (environment, _) =>
            Set.Rule(
                            environment,
                            thickness * Conversion(unit, environment),
                            width * Conversion(unit, environment),
                            shift * Conversion(unit, environment)) with { Part = part });

    /// <summary>The delimiter a <c>\left</c> or <c>\right</c> was written with, as <see cref="Delimiter"/>, naming the whole of it.</summary>
    private static Glyph? DelimiterGlyph(ContentPart fence)
    {
        if (fence.Part(TexRole.Argument) is not { } written) return null;

        var text = written.Node.Print();
        var symbol = text switch
        {
            @"\|" => Glyph.Delimiter("Vert"),
            { Length: 1 } => Glyph.Delimiter(text[0]),
            _ => Glyph.Delimiter(text.TrimStart('\\')),
        };

        return symbol is null ? null : symbol with { Origin = fence };
    }

    /// <summary>Classes that make a binary operator after them an ordinary atom.</summary>
    private static readonly HashSet<TexAtomType> OperandsNot =
        [TexAtomType.BinaryOperator, TexAtomType.BigOperator, TexAtomType.Relation, TexAtomType.Opening, TexAtomType.Punctuation];

    /// <summary>Classes a character may be kerned against, or joined with into a ligature.</summary>
    private static readonly HashSet<TexAtomType> Kernable =
        [TexAtomType.Ordinary, TexAtomType.BigOperator, TexAtomType.BinaryOperator, TexAtomType.Relation,
         TexAtomType.Opening, TexAtomType.Closing, TexAtomType.Punctuation];

    /// <summary>
    /// A row: each piece set in turn, TeX's glue between them by class, a kern or a ligature between two letters that
    /// have one.
    /// <para>
    /// <paramref name="outer"/> is what stood before the row in the row holding it. It decides whether the row's first
    /// piece is a binary operator or an ordinary atom, and nothing else — the first piece never gets glue in front of it.
    /// </para>
    /// </summary>
    private static Set Row(IReadOnlyList<Item> items, ContentPart? whole, TexEnvironment environment, Previous? outer)
    {
        var children = new List<Set>();
        var previous = outer;

        for (var i = 0; i < items.Count; i++)
        {
            var current = items[i];
            var left = current.Left;
            var right = current.Right;
            var next = i < items.Count - 1 ? items[i + 1] : null;

            // A binary operator with nothing to operate on is an ordinary atom: at the start, after another operator,
            // a relation, an opening or a comma — or before a relation, a closing or a comma.
            if (left == TexAtomType.BinaryOperator && (previous is null || OperandsNot.Contains(previous.Value.Right)))
                left = right = TexAtomType.Ordinary;
            else if (next is not null && right == TexAtomType.BinaryOperator
                     && next.Left is TexAtomType.Relation or TexAtomType.Closing or TexAtomType.Punctuation)
                left = right = TexAtomType.Ordinary;

            var kern = 0d;
            if (next is not null && right == TexAtomType.Ordinary
                && current.Glyph is { } letter && next.Glyph is { } following && Kernable.Contains(next.Left))
            {
                var font = following.FontFor(environment);
                var style = environment.Style;

                if (font.SupportsMetrics && letter.IsSupportedByFont(font, style))
                {
                    var leftChar = letter.FontOf(font).Value;
                    var rightChar = following.FontOf(font).Value;

                    if (font.GetLigature(leftChar, rightChar) is { } ligature)
                    {
                        current = GlyphItem(Glyph.Slot(ligature));
                        left = right = current.Left;
                        i++;
                    }
                    else
                    {
                        kern = font.GetKern(leftChar, rightChar, style);
                    }
                }
            }

            if (i != 0 && previous is { IsKern: false } before && !current.IsKern)
                children.Add(Set.Glue(Glue.Between(before.Right, left, environment)));

            var set = current.Make(environment, previous);
            children.Add(set);
            environment.LastFontId = set.LastFontId;

            if (kern > TexUtilities.FloatPrecision)
                children.Add(Strut(0, kern, 0));

            if (!current.IsKern)
                previous = new Previous(right, false);
        }

        return Horizontal(children, whole, (environment.Background as WpfMath.Rendering.WpfBrush)?.Value);
    }

    /// <summary>Pieces laid left to right on one baseline, each dropped by its own shift.</summary>
    private static Set Horizontal(List<Set> children, ContentPart? part, System.Windows.Media.Brush? background)
    {
        double run = 0, width = 0, height = 0, depth = 0, italic = 0;
        var lastFont = TexFontUtilities.NoFontId;
        var counting = true;

        foreach (var child in children)
        {
            run += child.Width;
            width = System.Math.Max(width, run);
            height = System.Math.Max(height, child.Height - child.Shift);
            depth = System.Math.Max(depth, child.Depth + child.Shift);
            italic = System.Math.Max(italic, child.Italic);

            // The font of the last thing drawn — or none, as soon as something in the row draws in no font at all.
            if (counting)
            {
                lastFont = child.LastFontId;
                counting = lastFont != TexFontUtilities.NoFontId;
            }
        }

        return new Set
        {
            Kind = "HorizontalBox",
            Width = width,
            Height = height,
            Depth = depth,
            Italic = italic,
            Part = part,
            Background = background,
            LastFontId = lastFont,
            Draw = (layer, _, x, y) =>
            {
                var at = x;
                foreach (var child in children)
                {
                    layer.Place(child, at, y + child.Shift);
                    at += child.Width;
                }
            },
        };
    }

    /// <summary>Room and nothing else: a strut, which reserves space and makes no piece of the layout.</summary>
    private static Set Strut(double width, double height, double depth, double shift = 0) => new()
    {
        Kind = "StrutBox",
        Width = width,
        Height = height,
        Depth = depth,
        Shift = shift,
        Spacing = true,
    };

    /// <summary>
    /// Pieces stacked top to bottom, each moved right by its own shift. The stack starts as tall as its first piece and
    /// deepens by each piece after it; a caller that knows better states its height and depth outright, which is how
    /// TeX pins a stack's baseline.
    /// </summary>
    private static Set Vertical(
        List<Set> children, double? height = null, double? depth = null, string kind = "VerticalBox",
        IReadOnlyList<double>? measuredShifts = null)
    {
        double tall = 0, deep = 0;
        double leftMost = double.MaxValue, rightMost = double.MinValue;
        var lastFont = TexFontUtilities.NoFontId;
        var counting = true;

        for (var at = 0; at < children.Count; at++)
        {
            var child = children[at];

            if (at == 0)
            {
                tall = child.Height;
                deep = child.Depth;
            }
            else
            {
                deep += child.Height + child.Depth;
            }

            // Where the stack's width is measured from, which is where each piece stood when it was added. A caller
            // that moves a piece after measuring says where it stood then.
            var shift = measuredShifts?[at] ?? child.Shift;
            leftMost = System.Math.Min(leftMost, shift);
            rightMost = System.Math.Max(rightMost, shift + (child.Width > 0 ? child.Width : 0));

            if (counting)
            {
                lastFont = child.LastFontId;
                counting = lastFont != TexFontUtilities.NoFontId;
            }
        }

        var left = leftMost;

        return new Set
        {
            Kind = kind,
            Width = rightMost - leftMost,
            Height = height ?? tall,
            Depth = depth ?? deep,
            LastFontId = lastFont,
            Draw = (layer, self, x, y) =>
            {
                var at = y - self.Height;
                foreach (var child in children)
                {
                    at += child.Height;
                    layer.Place(child, x + child.Shift - left, at);
                    at += child.Depth;
                }
            },
        };
    }

    /// <summary>A piece centred in a wider row of its own, unless it is already that wide.</summary>
    private static Set Widened(Set set, double width)
    {
        if (System.Math.Abs(width - set.Width) <= TexUtilities.FloatPrecision) return set;

        var half = Strut((width - set.Width) / 2, 0, 0);
        return Horizontal([half, set, half], null, null);
    }

    /// <summary>A piece centred in a row of its own of the given width — wrapped even where that adds no room.</summary>
    private static Set Centred(Set set, double width)
    {
        var half = Strut((width - set.Width) / 2, 0, 0);
        return Horizontal([half, set, half], null, null);
    }

    /// <summary>
    /// A script, as <see cref="Script"/>: something written onto a base — or onto nothing, drawn where it was written
    /// on a box of no width — or a brace wearing its label.
    /// </summary>
    private static Item? Scripted(ContentPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(TexRole.Base) is null)
            return ScriptedOn(part, NullItem(), style, knowledge);

        if (Braced(part, style, knowledge) is { } braced) return braced;

        return PartPiece(part, TexRole.Base, style, knowledge) is { } on
            ? ScriptedOn(part, on, style, knowledge)
            : null;
    }

    /// <summary>The one part with this role, as a piece — or null when it is absent or not buildable.</summary>
    private static Item? PartPiece(ContentPart whole, string role, string? style, TexFormulaParser knowledge)
    {
        foreach (var part in whole.Children)
            if (part.Role == role) return Piece(part, style, knowledge);

        return null;
    }

    /// <summary>Everything written onto a base, once the base itself is built — as <see cref="Scripted(ContentPart, Atom, string?, TexFormulaParser)"/>.</summary>
    private static Item? ScriptedOn(ContentPart part, Item on, string? style, TexFormulaParser knowledge)
    {
        // A prefix: an empty box wearing the scripts, followed by the base.
        if (Order(part, Roles.Name) is var name and >= 0 && Order(part, TexRole.Base) > name)
        {
            var carried = ScriptsOn(part, NullItem(), style, knowledge);
            if (carried is null) return null;

            return Sequenced([carried, on], part);
        }

        // The marks first, and separately: all of them make one superscript on the base, and whatever is written after
        // them goes on the whole of that.
        var marks = part.Children.Where(child => child.Role == TexRole.Mark).ToList();

        if (marks.Count > 0)
        {
            var primes = marks.Select(mark => GlyphItem(Glyph.Symbol("prime")! with { Origin = mark })).ToList();
            on = Scripts(on, null, Sequenced(primes, part), part);
        }

        var superscript = PartPiece(part, TexRole.Superscript, style, knowledge);
        var subscript = PartPiece(part, TexRole.Subscript, style, knowledge);

        if (part.Part(TexRole.Superscript) is not null && superscript is null) return null;
        if (part.Part(TexRole.Subscript) is not null && subscript is null) return null;

        if (superscript is null && subscript is null) return marks.Count > 0 ? on : null;

        var asked = part.Part(TexRole.Base)?.Children
            .FirstOrDefault(child => child.Kind == TexKinds.Command
                                     && child.Part(Roles.Name)?.Text is @"\limits" or @"\nolimits")
            ?.Part(Roles.Name)?.Text switch
        {
            @"\limits" => true,
            @"\nolimits" => false,
            _ => (bool?)null,
        };

        // Scripts on a big operator are its limits — over and under it, or beside it as scripts, by style and by what
        // was asked for — and so are scripts on anything typed as one.


        if (on.Operator is { } made)
            return Operator(made.Sign, subscript, superscript, asked ?? made.Vertical, part);

        if (on.Left == TexAtomType.BigOperator)
            return Operator(on, subscript, superscript, asked, part);

        return Scripts(on, subscript, superscript, part);
    }

    /// <summary>This node's scripts set on whatever is handed in, as <see cref="Scripts(ContentPart, Atom, string?, TexFormulaParser)"/> — used for a prefix.</summary>
    private static Item? ScriptsOn(ContentPart part, Item on, string? style, TexFormulaParser knowledge)
    {
        var superscript = PartPiece(part, TexRole.Superscript, style, knowledge);
        var subscript = PartPiece(part, TexRole.Subscript, style, knowledge);

        if (part.Part(TexRole.Superscript) is not null && superscript is null) return null;
        if (part.Part(TexRole.Subscript) is not null && subscript is null) return null;
        if (superscript is null && subscript is null) return null;

        return Scripts(on, subscript, superscript, part);
    }

    /// <summary>Scripts on a base, as TeX's rules for sub- and superscripts set them — the piece standing for <paramref name="part"/>.</summary>
    private static Item Scripts(Item on, Item? subscript, Item? superscript, ContentPart? part) =>
        new(on.Left, on.Right, null, (environment, _) =>
        {
            var set = ScriptsSet(on, subscript, superscript, environment);
            return set.Part is null ? set with { Part = part } : set;
        });

    private static Set ScriptsSet(Item? on, Item? subscript, Item? superscript, TexEnvironment environment)
    {
        var texFont = environment.MathFont;
        var style = environment.Style;

        var baseSet = on is null ? Strut(0, 0, 0) : on.Make(environment, null);
        if (subscript is null && superscript is null)
        {
            // Only a big operator's own scripts-less fall-through lands here, and its glyph is centred on the axis.
            if (baseSet.Kind == "CharBox")
                baseSet = baseSet with
                {
                    Shift = -(baseSet.Height + baseSet.Depth) / 2 - environment.MathFont.GetAxisHeight(environment.Style),
                };

            return baseSet;
        }

        var result = new List<Set> { baseSet };

        var lastFontId = baseSet.LastFontId;
        if (lastFontId == TexFontUtilities.NoFontId) lastFontId = texFont.GetMuFontId();

        var subscriptStyle = environment.GetSubscriptStyle();
        var superscriptStyle = environment.GetSuperscriptStyle();

        var delta = 0d;
        double shiftUp, shiftDown;

        if (on?.Glyph is { SymbolName: { } symbolName, Type: TexAtomType.BigOperator })
        {
            var charInfo = texFont.GetCharInfo(symbolName, style).Value;
            if (style < TexStyle.Text && texFont.HasNextLarger(charInfo))
                charInfo = texFont.GetNextLargerCharInfo(charInfo, style);

            var glyph = Set.Glyph(environment, charInfo);
            glyph = glyph with { Shift = -(glyph.Height + glyph.Depth) / 2 - environment.MathFont.GetAxisHeight(environment.Style) };
            result = [glyph];

            delta = charInfo.Metrics.Italic;
            if (delta > TexUtilities.FloatPrecision && subscript is null)
                result.Add(Strut(delta, 0, 0));

            var measured = Horizontal(result, null, null);
            shiftUp = measured.Height - texFont.GetSupDrop(superscriptStyle.Style);
            shiftDown = measured.Depth + texFont.GetSubDrop(subscriptStyle.Style);
        }
        else if (on?.Glyph is { } letter && letter.IsSupportedByFont(texFont, style))
        {
            var charFont = letter.FontOf(texFont).Value;
            // A glyph here is never a text symbol, which is the case that could have skipped this.
                delta = texFont.GetCharInfo(charFont, style).Value.Metrics.Italic;

            if (delta > TexUtilities.FloatPrecision && subscript is null)
            {
                result.Add(Strut(delta, 0, 0));
                delta = 0;
            }

            shiftUp = 0;
            shiftDown = 0;
        }
        else
        {
            // Anything that is not a single character is measured as what it came out as — which is what lifts a
            // script clear of an accent, lining the exponent of \dot{C} up with the dot rather than the C.
            shiftUp = baseSet.Height - texFont.GetSupDrop(superscriptStyle.Style);
            shiftDown = baseSet.Depth + texFont.GetSubDrop(subscriptStyle.Style);
        }

        Set? superscriptSet = null, subscriptSet = null;
        List<Set>? superscriptRow = null, subscriptRow = null;

        if (superscript is not null)
        {
            superscriptSet = superscript.Make(superscriptStyle, null);
            superscriptRow = [superscriptSet, Strut(0.5 * Conversion(TexUnit.Point, environment), 0, 0)];

            double p;
            if (style == TexStyle.Display)
                p = texFont.GetSup1(style);
            else if (environment.GetCrampedStyle().Style == style)
                p = texFont.GetSup3(style);
            else
                p = texFont.GetSup2(style);

            shiftUp = System.Math.Max(System.Math.Max(shiftUp, p),
                                      superscriptSet.Depth + System.Math.Abs(texFont.GetXHeight(style, lastFontId)) / 4);
        }

        if (subscript is not null)
        {
            subscriptSet = subscript.Make(subscriptStyle, null);
            subscriptRow = [subscriptSet, Strut(0.5 * Conversion(TexUnit.Point, environment), 0, 0)];
        }

        if (subscriptSet is null)
        {
            result.Add(Horizontal(superscriptRow!, null, null) with { Shift = -shiftUp });
            return Horizontal(result, null, null);
        }

        if (superscriptSet is null)
        {
            var drop = System.Math.Max(System.Math.Max(shiftDown, texFont.GetSub1(style)),
                                       subscriptSet.Height - 4 * System.Math.Abs(texFont.GetXHeight(style, lastFontId)) / 5);
            result.Add(Horizontal(subscriptRow!, null, null) with { Shift = drop });
            return Horizontal(result, null, null);
        }

        shiftDown = System.Math.Max(shiftDown, texFont.GetSub2(style));

        var rule = texFont.GetDefaultLineThickness(style);
        var between = shiftUp - superscriptSet.Depth + shiftDown - subscriptSet.Height;
        if (between < 4 * rule)
        {
            shiftUp += 4 * rule - between;

            // The bottom of the superscript at least four fifths of an x-height above the baseline.
            var psi = 0.8 * System.Math.Abs(texFont.GetXHeight(style, lastFontId)) - (shiftUp - superscriptSet.Depth);
            if (psi > 0)
            {
                shiftUp += psi;
                shiftDown -= psi;
            }
        }

        between = shiftUp - superscriptSet.Depth + shiftDown - subscriptSet.Height;

        result.Add(Vertical(
            [
                Horizontal(superscriptRow!, null, null) with { Shift = delta },
                Strut(0, between, 0),
                Horizontal(subscriptRow!, null, null),
            ],
            height: shiftUp + superscriptSet.Height,
            depth: shiftDown + subscriptSet.Depth));

        return Horizontal(result, null, null);
    }

    /// <summary>
    /// A big operator wearing limits, as TeX sets them: over and under the sign in display style or where
    /// <c>\limits</c> asked, beside it as scripts otherwise.
    /// </summary>
    private static Item Operator(Item? sign, Item? lower, Item? upper, bool? vertical, ContentPart part) =>
        new(TexAtomType.BigOperator, TexAtomType.BigOperator, null, (environment, _) =>
        {
            var set = OperatorSet(sign, lower, upper, vertical, environment);
                return set.Part is null ? set with { Part = part } : set;
            })
            {
                Operator = (sign, vertical),
            };

    private static Set OperatorSet(Item? sign, Item? lower, Item? upper, bool? vertical, TexEnvironment environment)
    {
        if ((vertical.HasValue && !vertical.Value) || (!vertical.HasValue && environment.Style >= TexStyle.Text))
        {
            // An operator with nothing attached still has to be the right size: the display form of its glyph is
            // picked here.
            if (lower is null && upper is null) return OperatorSign(sign, environment).Set;

            return ScriptsSet(sign, lower, upper, environment);
        }

        var (signSet, delta) = OperatorSign(sign, environment);
        var upperSet = upper?.Make(environment.GetSuperscriptStyle(), null);
        var lowerSet = lower?.Make(environment.GetSubscriptStyle(), null);

        // Every component as wide as the widest.
        var width = System.Math.Max(System.Math.Max(signSet.Width, upperSet?.Width ?? 0), lowerSet?.Width ?? 0);
        signSet = Widened(signSet, width);
        upperSet = upperSet is null ? null : Widened(upperSet, width);
        lowerSet = lowerSet is null ? null : Widened(lowerSet, width);

        var texFont = environment.MathFont;
        var style = environment.Style;
        var spacing5 = texFont.GetBigOpSpacing5(style);
        var kern = 0d;

        var stack = new List<Set>();

        if (upperSet is not null)
        {
            stack.Add(Strut(0, spacing5, 0));
            stack.Add(upperSet with { Shift = delta / 2 });
            kern = System.Math.Max(texFont.GetBigOpSpacing1(style), texFont.GetBigOpSpacing3(style) - upperSet.Depth);
            stack.Add(Strut(0, kern, 0));
        }

        stack.Add(signSet);

        if (lowerSet is not null)
        {
            stack.Add(Strut(0, System.Math.Max(texFont.GetBigOpSpacing2(style), texFont.GetBigOpSpacing4(style) - lowerSet.Height), 0));
            stack.Add(lowerSet with { Shift = -delta / 2 });
            stack.Add(Strut(0, spacing5, 0));
        }

        var measured = Vertical(stack);
        var total = measured.Height + measured.Depth;
        var height = signSet.Height;
        if (upperSet is not null) height += spacing5 + kern + upperSet.Height + upperSet.Depth;

        return Vertical(stack, height, total - height);
    }

    /// <summary>A big operator's sign at the size its style asks for, centred on the axis, and how far it leans.</summary>
    private static (Set Set, double Delta) OperatorSign(Item? sign, TexEnvironment environment)
    {
        if (sign?.Glyph is { SymbolName: { } name, Type: TexAtomType.BigOperator } symbol)
        {
            var texFont = environment.MathFont;
            var style = environment.Style;

            var character = texFont.GetCharInfo(name, style).Value;
            if (style < TexStyle.Text && texFont.HasNextLarger(character))
                character = texFont.GetNextLargerCharInfo(character, style);

            // The sign is the operator's own drawing of itself, and says which part it was set from.
            var glyph = Set.Glyph(environment, character) with { Part = symbol.Origin };
            glyph = glyph with { Shift = -(glyph.Height + glyph.Depth) / 2 - environment.MathFont.GetAxisHeight(environment.Style) };

            var delta = character.Metrics.Italic;
            List<Set> row = [glyph];
            if (delta > TexUtilities.FloatPrecision) row.Add(Strut(delta, 0, 0));

            return (Horizontal(row, null, null), delta);
        }

        return (Horizontal([sign is null ? Strut(0, 0, 0) : sign.Make(environment, null)], null, null), 0);
    }

    /// <summary>
    /// A piece that says it stands for <paramref name="part"/>, as <see cref="Tag"/> says it of an atom: outright, unless
    /// what it lays is another piece's drawing, which keeps naming what it named.
    /// </summary>
    private static Item Retagged(Item item, ContentPart part)
    {
        if (item.Glyph is { } glyph && item.Operator is null) return GlyphItem(glyph with { Origin = part });

        return item with
        {
            Make = (environment, previous) =>
            {
                var set = item.Make(environment, previous);
                return item.Retyped ? (set.Part is null or { Length: 0 } ? set with { Part = part } : set)
                     : item.PassesThrough ? (set.Part is null ? set with { Part = part } : set)
                     : set with { Part = part };
            },
        };
    }

    /// <summary>TeX's <c>\nulldelimiterspace</c>, 1.2pt: the room a fraction keeps either side in place of the delimiters it has not got.</summary>
    private const double NullDelimiterSpace = 0.12;

    /// <summary>A piece with a rule over it: room above the rule, the rule, the gap beneath it, and the piece.</summary>
    private static Set Overbar(Set set, double kern, double thickness, TexEnvironment environment) =>
        Vertical(
            [
                Strut(0, thickness, 0),
                Set.Rule(environment, thickness, set.Width, 0),
                Strut(0, kern, 0),
                set,
            ],
            kind: "OverBar");

    /// <summary>A square root — or an nth root, with its degree tucked small over the sign.</summary>
    private static Set Root(Item radicand, Item? degree, TexEnvironment environment)
    {
        const string sqrt = "sqrt";

        var texFont = environment.MathFont;
        var style = environment.Style;

        var rule = texFont.GetDefaultLineThickness(style);
        var clearance = style < TexStyle.Text
            ? texFont.GetXHeight(style, texFont.GetCharInfo(sqrt, style).Value.FontId)
            : rule;
        clearance = rule + System.Math.Abs(clearance) / 4;

        var inside = radicand.Make(environment.GetCrampedStyle(), null);

        var total = inside.Height + inside.Depth;
        var sign = Delimiter(sqrt, total + clearance + rule, environment);

        // Half of whatever the sign has to spare goes into the clearance.
        clearance += (sign.Depth - (total + clearance)) / 2;

        sign = sign with { Shift = -(inside.Height + clearance) };
        var bar = Overbar(inside, clearance, sign.Height, environment) with { Shift = -(inside.Height + clearance + rule) };
        var root = Horizontal([sign, bar], null, null);

        if (degree is null) return root;

        var small = degree.Make(environment.GetRootStyle(), null);
        small = small with { Shift = root.Depth - small.Depth - 0.55 * (root.Height + root.Depth) };

        var back = Strut(-10 * Conversion(TexUnit.Mu, environment), 0, 0);
        var reach = small.Width + back.Width;

        var row = new List<Set>();
        if (reach < 0) row.Add(Strut(-reach, 0, 0));
        row.Add(small);
        row.Add(back);
        row.Add(root);

        return Horizontal(row, null, null);
    }

    /// <summary>
    /// An accent over a piece: the largest size of the accent no wider than the piece, centred over it and skewed as the
    /// letter underneath asks.
    /// </summary>
    private static Set Accented(Item inner, Glyph accent, TexEnvironment environment)
    {
        var texFont = environment.MathFont;
        var style = environment.Style;

        var body = inner.Make(environment.GetCrampedStyle(), null);
        var skew = inner.Nucleus?.FontOf(texFont).Value is { } letter ? texFont.GetSkew(letter, style) : 0.0;

        var character = texFont.GetCharInfo(accent.SymbolName!, style).Value;
        while (texFont.HasNextLarger(character))
        {
            var larger = texFont.GetNextLargerCharInfo(character, style);
            if (larger.Metrics.Width > body.Width) break;
            character = larger;
        }

        var mark = Set.Glyph(environment, character);
        var lean = character.Metrics.Italic;
        if (lean > TexUtilities.FloatPrecision) mark = Horizontal([mark, Strut(lean, 0, 0)], null, null);

        var delta = System.Math.Min(body.Height, texFont.GetXHeight(style, character.FontId));

        var difference = (body.Width - mark.Width) / 2;
        var shifted = mark with { Shift = skew + System.Math.Max(difference, 0) };
        if (difference < 0) body = Centred(body, mark.Width);

        // The accent is measured into the stack before it is moved over the centre, as TeX's own port of this did.
        List<Set> stack = [shifted, Strut(0, -delta, 0), body];
        var measured = Vertical(stack, measuredShifts: [0, 0, body.Shift]);
        var height = measured.Height + measured.Depth - body.Depth;

        return Vertical(stack, height, body.Depth, measuredShifts: [0, 0, body.Shift]);
    }

    /// <summary>A command, set as <see cref="Command"/> reads it — in the same order, so the same reading wins.</summary>
    private static Item? Commanded(ContentPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(Roles.Name)?.Text is not { } name) return null;

        switch (name)
        {
            case @"\frac":
            {
                if (PartPiece(part, TexRole.Numerator, style, knowledge) is not { } numerator) return null;
                if (PartPiece(part, TexRole.Denominator, style, knowledge) is not { } denominator) return null;

                return new Item(TexAtomType.Inner, TexAtomType.Inner, null, (environment, _) =>
                    Fraction(numerator, denominator, environment) with { Part = part });
            }

            case @"\sqrt":
            {
                if (PartPiece(part, TexRole.Radicand, style, knowledge) is not { } radicand) return null;

                var asked = part.Part(TexRole.Degree);
                var degree = asked is null ? null : PartPiece(part, TexRole.Degree, style, knowledge);
                if (asked is not null && degree is null) return null;

                return new Item(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (environment, _) =>
                    Root(radicand, degree, environment) with { Part = part });
            }

            case @"\substack":
                return Substacked(part, style, knowledge);

            case @"\overline":
            {
                if (PartPiece(part, TexRole.Base, style, knowledge) is not { } inner) return null;

                return new Item(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (environment, _) =>
                {
                    var set = inner.Make(environment.GetCrampedStyle(), null);
                    var rule = environment.MathFont.GetDefaultLineThickness(environment.Style);
                    return Overbar(set, 3 * rule, rule, environment) with
                    {
                        Height = set.Height + 5 * rule,
                        Depth = set.Depth,
                        Part = part,
                    };
                });
            }

            case @"\underline":
            {
                if (PartPiece(part, TexRole.Base, style, knowledge) is not { } inner) return null;

                return new Item(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (environment, _) =>
                {
                    var rule = environment.MathFont.GetDefaultLineThickness(environment.Style);
                    var set = inner.Make(environment, null);
                    return Vertical(
                        [set, Strut(0, 3 * rule, 0), Set.Rule(environment, rule, set.Width, 0)],
                        height: set.Height,
                        depth: set.Depth + 5 * rule) with { Part = part };
                });
            }

            case @"\not":
            {
                if (part.Part(TexRole.Base) is null) return null;
                if (DeclineUnsettled && part.Parent is { Kind: TexKinds.Script } && part.Role == TexRole.Base) return null;
                if (SymbolItem("not", part) is not { } slash) return null;

                // The slash, then everything written after the name in the order it was written: one sign.
                var sign = new List<Item> { slash };
                foreach (var written in part.Children)
                {
                    if (written.Role is not (Roles.Element or TexRole.Base)) continue;

                    if (Piece(written, style, knowledge) is { } built) sign.Add(Retagged(built, part));
                    else if (written.Role == TexRole.Base) return null;
                }

                return Sequenced(sign, part);
            }

            case @"\mspace":
            case @"\hspace":
            case @"\hspace*":
            case @"\kern":
            case @"\mkern":
            {
                if (part.Part(TexRole.Argument) is not { } amount) return null;

                return StandardCommands.LengthOf(name, Inside(amount)) is { } length ? SpaceItem(length.Unit, length.Value) : null;
            }

            case @"\ ":
            case @"\nbsp":
                return part.Parts.Any() ? null : SpaceItem(null, 0);
        }

        // A sized delimiter: one bracket at a chosen size, standing on its own.
        if (DelimiterGlyph(part) is { } sized && StandardCommands.SizedDelimiterOf(name[1..]) is { } big)
            return SizedDelimiter(sized, big.MinHeight, big.Type, part);

        // Something set above or below something else — \stackrel, \overset, \underset.
        if (part.Part(TexRole.Over) is not null || part.Part(TexRole.Under) is not null)
        {
            var above = part.Part(TexRole.Over) is not null ? TexRole.Over : TexRole.Under;

            if (PartPiece(part, above, style, knowledge) is { } annotation
                && PartPiece(part, TexRole.Base, style, knowledge) is { } on
                && StandardCommands.Dictionary.TryGetValue(name[1..], out var entry)
                && entry is StandardCommands.StackedAnnotationCommand stacked)
            {
                var set = UnderOver(on, annotation, TexUnit.Mu, StandardCommands.StackedAnnotationCommand.AnnotationSpace,
                                    smaller: true, stacked.Over, part);
                return stacked.AsRelation ? Typed(set, TexAtomType.Relation, part) : set;
            }
        }

        // A style: carried down the build rather than built.
        if (TexFormulaParser.TextStyleOf(name[1..]) is { } restyled)
        {
            if (TexFormulaParser.IsRawTextStyle(name[1..]))
            {
                if (part.Part(TexRole.Base) is not { } worded) return null;

                var face = name[1..] == "mbox" ? TexUtilities.TextStyleName : restyled;
                return LettersItem(Inside(worded), face, spaced: false, part);
            }

            if (part.Part(TexRole.Base) is not { } styled) return null;
            if (Piece(styled, restyled, knowledge) is not { } inner) return null;

            return Retagged(inner, part);
        }

        // Every accent at once.
        if (part.Part(TexRole.Base) is { } accented && Glyph.Symbol(name.TrimStart('\\')) is { Type: TexAtomType.Accent } accent)
        {
            if (Piece(accented, style, knowledge) is not { } inner) return null;

            return new Item(TexAtomType.Ordinary, TexAtomType.Ordinary, null, (environment, _) =>
                Accented(inner, accent, environment) with { Part = part })
            {
                Nucleus = inner.Nucleus,
            };
        }

        // Any other command the table makes from arguments already built.
        if (StandardCommands.Dictionary.TryGetValue(name[1..], out var command) && Assembles(command))
        {
            var arguments = new List<Item>();

            foreach (var argument in part.Parts)
            {
                if (Piece(argument, style, knowledge) is not { } built) return null;

                arguments.Add(built);
            }

            return Assembled(command, arguments, part);
        }

        // Shorthand: what the reader said it stands for is hanging underneath.
        if (PartPiece(part, Roles.Derived, style, knowledge) is { } shorthand) return Retagged(shorthand, part);

        // A symbol standing on its own, if it is one.
        if (!part.Parts.Any() && SymbolItem(name[1..], part) is { } symbol) return symbol;

        return WordsItem(part, style, knowledge);
    }

    /// <summary>Whether a table entry is one <see cref="Assembled"/> sets from its arguments.</summary>
    private static bool Assembles(object? command) =>
        command is StandardCommands.OverArrowCommand or StandardCommands.DotsCommand or StandardCommands.FracStyleCommand
            or StandardCommands.CfracCommand or StandardCommands.SlashFractionCommand or StandardCommands.ParenModCommand
            or StandardCommands.GenFracCommand or StandardCommands.PhantomCommand or StandardCommands.SmashCommand
            or StandardCommands.BoxedCommand or StandardCommands.ExtensibleArrowCommand or StandardCommands.BraceCommand
            or StandardCommands.BoldSymbolCommand or StandardCommands.OperatorNameCommand or StandardCommands.BinomCommand
            or StandardCommands.BraketCommand or StandardCommands.CancelCommand or StandardCommands.AtomTypeCommand
            or StandardCommands.UnderscoreCommand or StandardCommands.TransparentCommand;

    /// <summary>
    /// What a command in the table makes of arguments already built — as each entry's <c>Assemble</c> does with atoms.
    /// Null where these arguments do not suit it.
    /// </summary>
    private static Item? Assembled(object? command, IReadOnlyList<Item> arguments, ContentPart origin)
    {
        switch (command)
        {
            case StandardCommands.OverArrowCommand arrow when arguments.Count == 1:
            {
                var inner = arguments[0];
                return Made(TexAtomType.Ordinary, origin, environment => OverArrow(inner, arrow.Decoration, arrow.Over, environment));
            }

            case StandardCommands.DotsCommand dots when arguments.Count == 0:
                return Made(TexAtomType.Ordinary, origin, environment => Dots(dots.Shape, environment));

            case StandardCommands.FracStyleCommand forced when arguments.Count == 2:
            {
                var (numerator, denominator) = (arguments[0], arguments[1]);
                return Made(TexAtomType.Inner, origin, environment =>
                    Fraction(numerator, denominator, environment, forced: forced.Style));
            }

            case StandardCommands.CfracCommand when arguments.Count is 2 or 3:
            {
                var half = arguments.Count == 3 ? 1 : 0;
                var (numerator, denominator) = (arguments[half], arguments[half + 1]);
                var leaning = StandardCommands.CfracCommand.Leaning(origin);
                return Made(TexAtomType.Inner, origin, environment =>
                    Fraction(numerator, denominator, environment, forced: TexStyle.Display, keep: true, numeratorAlignment: leaning));
            }

            case StandardCommands.SlashFractionCommand when arguments.Count == 2:
            {
                var (numerator, denominator) = (arguments[0], arguments[1]);
                return Made(TexAtomType.Ordinary, origin, environment => SlashFraction(numerator, denominator, environment));
            }

            case StandardCommands.ParenModCommand mod when arguments.Count == 1:
                return Mod(arguments[0], mod.WithMod, mod.Fenced, origin);

            case StandardCommands.GenFracCommand when arguments.Count == 6:
            {
                // Written as `{}` where there is to be none: an argument that is not a delimiter is a side left open.
                var left = arguments[0].Glyph is { SymbolName: not null } l ? l : null;
                var right = arguments[1].Glyph is { SymbolName: not null } r ? r : null;
                var (numerator, denominator) = (arguments[4], arguments[5]);

                var fraction = Made(TexAtomType.Inner, origin, environment => Fraction(numerator, denominator, environment));
                return left is null && right is null ? fraction : Fenced(fraction, left, right, origin);
            }

            case StandardCommands.PhantomCommand phantom when arguments.Count == 1:
                return Phantom(arguments[0], phantom.UseWidth, phantom.UseHeight, phantom.UseHeight);

            case StandardCommands.SmashCommand smash when arguments.Count == 1:
            {
                var inner = arguments[0];
                return smash.LapAlignment is { } alignment
                    ? new Item(inner.Left, inner.Right, null, (environment, _) => Lap(inner, alignment, environment) with { Part = origin })
                    : new Item(inner.Left, inner.Right, null, (environment, _) => Smashed(inner, environment) with { Part = origin });
            }

            case StandardCommands.BoxedCommand when arguments.Count == 1:
            {
                var inner = arguments[0];
                return Made(TexAtomType.Ordinary, origin, environment => Boxed(inner, environment));
            }

            case StandardCommands.ExtensibleArrowCommand arrow when arguments.Count is 1 or 2:
            {
                var over = arguments[^1];
                var under = arguments.Count == 2 ? arguments[0] : null;
                return Made(TexAtomType.Relation, origin, environment => ExtensibleArrow(over, under, arrow.Decoration, environment));
            }

            case StandardCommands.BraceCommand brace:
                return arguments.Count == 1 ? Brace(arguments[0], null, brace.IsOver, origin) : null;

            case StandardCommands.BoldSymbolCommand when arguments.Count == 1:
            {
                var inner = arguments[0];
                return new Item(inner.Left, inner.Right, null, (environment, _) =>
                {
                    var set = inner.Make(environment with { IsBold = true }, null);
                    return set.Part is null ? set with { Part = origin } : set;
                })
                {
                    PassesThrough = true,
                };
            }

            case StandardCommands.OperatorNameCommand name when arguments.Count == 1:
                return Operator(arguments[0], null, null, name.Starred ? null : false, origin);

            case StandardCommands.BinomCommand binom when arguments.Count == 2:
            {
                var (numerator, denominator) = (arguments[0], arguments[1]);
                var fraction = Made(TexAtomType.Inner, origin, environment =>
                    Fraction(numerator, denominator, environment, line: 0, forced: binom.Style, bare: true));

                return Fenced(fraction,
                              Glyph.Named("(", TexAtomType.Opening, true) with { Origin = origin },
                              Glyph.Named(")", TexAtomType.Closing, true) with { Origin = origin },
                              origin);
            }

            case StandardCommands.BraketCommand braket when arguments.Count == 1:
                return Fenced(arguments[0],
                              Glyph.Named(braket.Open, TexAtomType.Opening, true) with { Origin = origin },
                              Glyph.Named(braket.Close, TexAtomType.Closing, true) with { Origin = origin },
                              origin);

            case StandardCommands.CancelCommand cancel when arguments.Count == 1:
            {
                var inner = arguments[0];
                return Made(TexAtomType.Ordinary, origin, environment =>
                {
                    var set = inner.Make(environment, null);
                    var stroke = Set.Stroke(cancel.Mode, set.Width, set.Height, set.Depth);
                    return Layered([set, stroke]);
                });
            }

            case StandardCommands.AtomTypeCommand typed when arguments.Count == 1:
                return Typed(arguments[0], typed.Type, origin);

            case StandardCommands.UnderscoreCommand when arguments.Count == 0:
                return RuleItem(TexUnit.Ex, width: 0.7, thickness: 0.1, shift: 0.3, origin);

            case StandardCommands.TransparentCommand when arguments.Count == 1:
                return arguments[0];

            default:
                return null;
        }
    }

    /// <summary>A construct of its own class with nothing to say about what came before it, standing for <paramref name="origin"/>.</summary>
    private static Item Made(TexAtomType type, ContentPart? origin, System.Func<TexEnvironment, Set> make) =>
        new(type, type, null, (environment, _) =>
        {
            var set = make(environment);
            return set.Part is null ? set with { Part = origin } : set;
        });

    /// <summary>A piece retyped as another class — <c>\mathop</c> and its family — drawing as it did.</summary>
    private static Item Typed(Item inner, TexAtomType type, ContentPart? origin) =>
        new(type, type, null, (environment, _) =>
        {
            var set = inner.Make(environment, null);
                return set.Part is null ? set with { Part = origin } : set;
            })
            {
            PassesThrough = true,
                Retyped = true,
            };

    /// <summary>Something between two named delimiters, as a fence built by a command rather than by <c>\left</c>.</summary>
    private static Item Fenced(Item inside, Glyph? left, Glyph? right, ContentPart? origin) =>
        new(TexAtomType.Opening, TexAtomType.Closing, null, (environment, _) =>
        {
            var set = Fence(inside, left, right, environment);
            return set.Part is null ? set with { Part = origin } : set;
        });

    /// <summary>Pieces drawn over one another at one point, as wide and tall as the largest of them.</summary>
    private static Set Layered(List<Set> children)
    {
        double width = 0, height = 0, depth = 0, italic = 0;
        var lastFont = TexFontUtilities.NoFontId;
        var counting = true;

        for (var at = 0; at < children.Count; at++)
        {
            var child = children[at];

            if (at == 0)
            {
                width = child.Width;
                height = child.Height - child.Shift;
                depth = child.Depth + child.Shift;
                italic = child.Italic;
            }
            else
            {
                width = System.Math.Max(width, child.Width);
                height = System.Math.Max(height, child.Height - child.Shift);
                depth = System.Math.Max(depth, child.Depth + child.Shift);
                italic = System.Math.Max(italic, child.Italic);
            }

            if (counting)
            {
                lastFont = child.LastFontId;
                counting = lastFont != TexFontUtilities.NoFontId;
            }
        }

        return new Set
        {
            Kind = "LayeredBox",
            Width = width,
            Height = height,
            Depth = depth,
            Italic = italic,
            LastFontId = lastFont,
            Draw = (layer, _, x, y) =>
            {
                foreach (var child in children) layer.Place(child, x, y + child.Shift);
            },
        };
    }

    /// <summary>A stretchy arrow over or under a piece — <c>\overrightarrow</c> and its family.</summary>
    private static Set OverArrow(Item inner, ArrowDecoration decoration, bool over, TexEnvironment environment)
    {
        var body = inner.Make(environment.GetCrampedStyle(), null);
        var thickness = environment.MathFont.GetDefaultLineThickness(environment.Style);
        var arrow = Set.Arrow(environment, body.Width, thickness, decoration);

        return over
            ? Vertical([Strut(0, thickness, 0), arrow, Strut(0, 3 * thickness, 0), body],
                       height: body.Height + arrow.Height + 4 * thickness, depth: body.Depth)
            : Vertical([body, Strut(0, 3 * thickness, 0), arrow, Strut(0, thickness, 0)],
                       height: body.Height, depth: body.Depth + arrow.Height + 4 * thickness);
    }

    /// <summary>Three dots stacked, or run down the diagonal, centred on the axis — <c>\vdots</c> and <c>\ddots</c>.</summary>
    private static Set Dots(StandardCommands.DotsCommand.DotsShape shape, TexEnvironment environment)
    {
        var font = environment.MathFont;
        var style = environment.Style;

        Set Dot() => Glyph.Symbol("ldotp")!.Set(environment);

        var first = Dot();
        var quad = font.GetQuad(first.LastFontId, style);
        var gap = 0.18 * quad;
        var step = shape == StandardCommands.DotsCommand.DotsShape.Diagonal ? first.Height + first.Depth + gap : 0.0;

        var column = new List<Set>();
        for (var i = 0; i < 3; i++)
        {
            if (i > 0) column.Add(Strut(0, gap, 0));
            column.Add((i == 0 ? first : Dot()) with { Shift = i * step });
        }

        var stack = Vertical(column);
        stack = stack with { Shift = -((stack.Height + stack.Depth) / 2) - font.GetAxisHeight(style) };
        return Horizontal([stack], null, null);
    }

    /// <summary>A fraction set as TeX sets a <c>\genfrac</c>: its halves in their styles, a bar or none, the null delimiter space either side unless bare.</summary>
    private static Set Fraction(
        Item numerator, Item denominator, TexEnvironment environment,
        double? line = null, TexStyle? forced = null, bool keep = false, bool bare = false,
        TexAlignment numeratorAlignment = TexAlignment.Center)
    {
        if (forced is { } style0) environment = environment with { Style = style0 };

        var texFont = environment.MathFont;
        var style = environment.Style;

        var rule = texFont.GetDefaultLineThickness(style);
        var thickness = line ?? rule;

        var top = numerator.Make(keep ? environment.GetCrampedStyle() : environment.GetNumeratorStyle(), null);
        var bottom = denominator.Make(keep ? environment.GetCrampedStyle() : environment.GetDenominatorStyle(), null);

        if (top.Width < bottom.Width) top = Aligned(top, bottom.Width, numeratorAlignment);
        else bottom = Centred(bottom, top.Width);

        double shiftUp, shiftDown;
        if (style < TexStyle.Text)
        {
            shiftUp = texFont.GetNum1(style);
            shiftDown = texFont.GetDenom1(style);
        }
        else
        {
            shiftDown = texFont.GetDenom2(style);
            shiftUp = thickness > 0 ? texFont.GetNum2(style) : texFont.GetNum3(style);
        }

        var stack = new List<Set> { top };
        var axis = texFont.GetAxisHeight(style);

        if (thickness > 0)
        {
            var clearance = style < TexStyle.Text ? 3 * thickness : thickness;

            var half = thickness / 2;
            var kern1 = shiftUp - top.Depth - (axis + half);
            var kern2 = axis - half - (bottom.Height - shiftDown);
            var delta1 = clearance - kern1;
            var delta2 = clearance - kern2;
            if (delta1 > 0)
            {
                shiftUp += delta1;
                kern1 += delta1;
            }
            if (delta2 > 0)
            {
                shiftDown += delta2;
                kern2 += delta2;
            }

            stack.Add(Strut(0, kern1, 0));
            stack.Add(Set.Rule(environment, thickness, top.Width, 0));
            stack.Add(Strut(0, kern2, 0));
        }
        else
        {
            var clearance = style < TexStyle.Text ? 7 * rule : 3 * rule;

            var kern = shiftUp - top.Depth - (bottom.Height - shiftDown);
            var delta = (clearance - kern) / 2;
            if (delta > 0)
            {
                shiftUp += delta;
                shiftDown += delta;
                kern += 2 * delta;
            }

            stack.Add(Strut(0, kern, 0));
        }

        stack.Add(bottom);

        var fraction = Vertical(stack, shiftUp + top.Height, shiftDown + bottom.Depth);
        return bare
            ? fraction
            : Horizontal([Strut(NullDelimiterSpace, 0, 0), fraction, Strut(NullDelimiterSpace, 0, 0)], null, null);
    }

    /// <summary>A piece in a row of its own of the given width, pushed left, right or centred — wrapped whatever the slack.</summary>
    private static Set Aligned(Set set, double width, TexAlignment alignment)
    {
        var extra = width - set.Width;
        return alignment switch
        {
            TexAlignment.Left => Horizontal([set, Strut(extra, 0, 0)], null, null),
            TexAlignment.Right => Horizontal([Strut(extra, 0, 0), set], null, null),
            _ => Centred(set, width),
        };
    }

    /// <summary>An inline slash fraction — <c>\nicefrac</c>: a raised script numerator, the slash, a lowered script denominator.</summary>
    private static Set SlashFraction(Item numerator, Item denominator, TexEnvironment environment)
    {
        var script = environment.GetSubscriptStyle();
        var top = numerator.Make(script, null);
        var bottom = denominator.Make(script, null);
        var slash = Glyph.Symbol("slash")!.Set(environment);

        var xHeight = environment.MathFont.GetXHeight(environment.Style, environment.LastFontId);
        top = top with { Shift = -(0.6 * xHeight + top.Depth) };
        bottom = bottom with { Shift = 0.2 * xHeight };

        var kern = -0.12 * slash.Width;
        return Horizontal([top, Strut(kern, 0, 0), slash, Strut(kern, 0, 0), bottom], null, null);
    }

    /// <summary><c>\pmod</c>, <c>\pod</c> and <c>\mod</c>: the word, the argument, the brackets where there are any, and the gap before.</summary>
    private static Item Mod(Item argument, bool withMod, bool fenced, ContentPart origin)
    {
        var inside = new List<Item>();
        if (withMod)
        {
            foreach (var letter in "mod") inside.Add(GlyphItem(Glyph.Letter(letter, "mathrm")));
            if (PrimitiveItem("thickspace", null) is { } thin) inside.Add(thin);
        }
        inside.Add(argument);

        var word = Sequenced(inside, null);

        if (!fenced)
        {
            var bare = new List<Item>();
            if (PrimitiveItem("quad", null) is { } lead) bare.Add(lead);
            bare.Add(word);
            return Sequenced(bare, origin);
        }

        var brackets = Fenced(word,
                              Glyph.Named("(", TexAtomType.Opening, true) with { Origin = origin },
                              Glyph.Named(")", TexAtomType.Closing, true) with { Origin = origin },
                              origin);

        var whole = new List<Item>();
        if (PrimitiveItem("quad", null) is { } gap) whole.Add(gap);
        whole.Add(brackets);
        return Sequenced(whole, origin);
    }

    /// <summary>What a row of the typesetter's would hold if handed this piece: its elements when it is a row, and itself otherwise.</summary>
    private static List<Item> Elements(Item item) =>
        item.Elements is { } elements ? [.. elements] : [item];

    /// <summary>A piece measured and not drawn — <c>\phantom</c> and its one-dimensional variants.</summary>
    private static Item Phantom(Item inner, bool useWidth, bool useHeight, bool useDepth)
    {
        var elements = Elements(inner);
        var left = elements.Count == 0 ? TexAtomType.Ordinary : elements[0].Left;
        var right = elements.Count == 0 ? TexAtomType.Ordinary : elements[^1].Right;

        return new Item(left, right, null, (environment, previous) =>
        {
            var row = Row(elements, null, environment, previous);
            return Strut(useWidth ? row.Width : 0, useHeight ? row.Height : 0, useDepth ? row.Depth : 0, row.Shift);
        });
    }

    /// <summary>A piece drawn with no height or depth — <c>\smash</c>.</summary>
    private static Set Smashed(Item inner, TexEnvironment environment) =>
        Horizontal([inner.Make(environment, null)], null, null) with { Height = 0, Depth = 0 };

    /// <summary>A piece drawn with no width, hanging left, right or either side of where it stands — <c>\mathllap</c> and its family.</summary>
    private static Set Lap(Item inner, TexAlignment alignment, TexEnvironment environment)
    {
        var body = inner.Make(environment, null);
        var offset = alignment switch
        {
            TexAlignment.Left => -body.Width,
            TexAlignment.Center => -body.Width / 2,
            _ => 0.0,
        };

        List<Set> row = offset != 0.0 ? [Strut(offset, 0, 0), body] : [body];
        return Horizontal(row, null, null) with { Width = 0 };
    }

    /// <summary>A piece in a frame, padded as the standard classes pad a <c>\fbox</c>.</summary>
    private static Set Boxed(Item inner, TexEnvironment environment)
    {
        var body = inner.Make(environment, null);
        var thickness = environment.MathFont.GetDefaultLineThickness(environment.Style);
        var inset = thickness + 7.5 * thickness;

        var content = Horizontal([Strut(inset, 0, 0), body, Strut(inset, 0, 0)], null, null) with
        {
            Height = body.Height + inset,
            Depth = body.Depth + inset,
        };

        var frame = Set.Frame(environment, thickness, content.Width, content.Height, content.Depth);

        return Layered([content, frame]);
    }

    /// <summary>An arrow stretched to its labels — <c>\xrightarrow</c> and its family — its shaft on the axis.</summary>
    private static Set ExtensibleArrow(Item over, Item? under, ArrowDecoration decoration, TexEnvironment environment)
    {
        var overSet = over.Make(environment.GetSuperscriptStyle(), null);
        var underSet = under?.Make(environment.GetSubscriptStyle(), null);

        var quad = environment.MathFont.GetQuad(environment.LastFontId, environment.Style);
        var padding = 0.25 * quad;
        var labels = System.Math.Max(overSet.TotalWidth, underSet?.TotalWidth ?? 0);
        var width = System.Math.Max(quad, labels + 2 * padding);

        var thickness = environment.MathFont.GetDefaultLineThickness(environment.Style);
        var arrow = Set.Arrow(environment, width, thickness, decoration);
        var gap = thickness;

        var stack = new List<Set> { Centred(overSet, width), Strut(0, gap, 0) };
        var above = overSet.TotalHeight + gap;

        stack.Add(arrow);

        var below = 0.0;
        if (underSet is not null)
        {
            stack.Add(Strut(0, gap, 0));
            stack.Add(Centred(underSet, width));
            below = underSet.TotalHeight + gap;
        }

        var axis = environment.MathFont.GetAxisHeight(environment.Style);
        var total = above + arrow.TotalHeight + below;
        var height = above + arrow.Height / 2 + axis;

        return Vertical(stack, height, total - height) with { Width = width };
    }

    /// <summary>
    /// A piece with another set over or under it at a fixed gap, all of them as wide as the widest — <c>\overset</c>,
    /// and the pile <c>\doteq</c> is.
    /// </summary>
    private static Item UnderOver(Item on, Item annotation, TexUnit unit, double space, bool smaller, bool over, ContentPart? origin) =>
        Made(TexAtomType.Ordinary, origin, environment =>
        {
            var body = on.Make(environment, null);
            var width = body.Width;

            var marked = annotation.Make(smaller ? environment.GetSubscriptStyle() : environment, null);
            width = System.Math.Max(width, marked.Width);

            environment.LastFontId = body.LastFontId;

            var gap = Strut(0, space * Conversion(unit, environment), 0);
            var stack = new List<Set>();

            if (over)
            {
                stack.Add(Widened(marked, width));
                stack.Add(gap);
            }

            stack.Add(Widened(body, width));
            var measured = Vertical(stack);
            var height = measured.Height + measured.Depth - body.Depth;

            if (!over)
            {
                stack.Add(gap);
                stack.Add(Widened(marked, width));
            }

            measured = Vertical(stack);
            return Vertical(stack, height, measured.Height + measured.Depth - height);
        });

    /// <summary>A brace over or under a piece, with its label beyond it where one was written — <c>\overbrace{a+b}^{n}</c>.</summary>
    private static Item Brace(Item on, Item? label, bool over, ContentPart origin) =>
        Made(TexAtomType.Ordinary, origin, environment =>
        {
            var symbol = Glyph.Symbol(
                TexFormulaParser.DelimiterNames[(int)TexDelimiter.Brace][(int)(over ? TexDelimeterType.Over : TexDelimeterType.Under)]);

            var body = on.Make(environment, null);
            var brace = Delimiter(symbol!.SymbolName!, body.Width, environment);
            var script = label?.Make(over ? environment.GetSuperscriptStyle() : environment.GetSubscriptStyle(), null);

            var width = System.Math.Max(body.Width, brace.Height + brace.Depth);
            if (script is not null) width = System.Math.Max(width, script.Width);

            if (System.Math.Abs(width - body.Width) > TexUtilities.FloatPrecision)
                body = Centred(body, width);

            // Drawn turned, so its height and depth are what reach across.
            var length = brace.Height + brace.Depth;
            if (System.Math.Abs(width - length) > TexUtilities.FloatPrecision)
            {
                var rest = width - length;
                var half = Strut(0, rest / 2, 0);
                brace = Vertical([half, brace, half], height: brace.Height + rest / 2, depth: brace.Depth + rest / 2,
                                 measuredShifts: [brace.Shift, brace.Shift, brace.Shift]);
            }

            if (script is not null && System.Math.Abs(width - script.Width) > TexUtilities.FloatPrecision)
                script = Centred(script, width);

            var kern = StandardCommands.BraceCommand.LabelKern * Conversion(TexUnit.Ex, environment);

            return OverUnderSet(body, brace, script, kern, over);
        });

    /// <summary>A piece with a turned delimiter and a script over or under it.</summary>
    private static Set OverUnderSet(Set body, Set delimiter, Set? script, double kern, bool over) => new()
    {
        Kind = "OverUnderBox",
        Width = body.Width,
        Height = body.Height + (over ? delimiter.Width : 0.0) + (over && script is not null ? script.Height + script.Depth + kern : 0.0),
        Depth = body.Depth + (over ? 0.0 : delimiter.Width) + (!over && script is not null ? script.Height + script.Depth + kern : 0.0),
        Draw = (layer, _, x, y) =>
        {
            double translateX, translateY, scriptY;
            if (over)
            {
                var centre = y - body.Height - delimiter.Width;
                translateX = x + delimiter.Width / 2;
                translateY = centre + delimiter.Width / 2;
                scriptY = script is null ? 0.0 : centre - kern - script.Depth;
            }
            else
            {
                var centre = y + body.Depth + delimiter.Width;
                translateX = x + delimiter.Width / 2;
                translateY = centre - delimiter.Width / 2;
                scriptY = script is null ? 0.0 : centre + kern + script.Height;
            }

            layer.Place(body, x, y);
            layer.PlaceTransformed(
                delimiter,
                [new Transformation.Translate(translateX, translateY),
                 new Transformation.Rotate(90)],
                -delimiter.Width / 2,
                -delimiter.Depth + delimiter.Width / 2);
            if (script is not null) layer.Place(script, x, scriptY);
        },
    };

    /// <summary>This script read as a brace and its label, as <see cref="Labelled"/>, or null where it is not one.</summary>
    private static Item? Braced(ContentPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(TexRole.Base) is not { Kind: TexKinds.Command } braced) return null;
        if (braced.Part(Roles.Name)?.Text is not { } name) return null;

        var over = part.Part(TexRole.Superscript) is not null;
        var side = over ? TexRole.Superscript : TexRole.Subscript;

        if (part.Part(over ? TexRole.Subscript : TexRole.Superscript) is not null) return null;
        if (part.Part(TexRole.Mark) is not null) return null;

        if (PartPiece(braced, TexRole.Base, style, knowledge) is not { } on) return null;
        if (PartPiece(part, side, style, knowledge) is not { } label) return null;

        return StandardCommands.Dictionary.TryGetValue(name[1..], out var entry)
               && entry is StandardCommands.BraceCommand brace && brace.IsOver == over
            ? Brace(on, label, over, part)
            : null;
    }

    /// <summary>A block between <c>\begin</c> and <c>\end</c>, as <see cref="Environment"/>: a grid, an array, or a display environment that is its contents.</summary>
    private static Item? Environmented(ContentPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(TexRole.Begin) is not { } begin) return null;
        if (part.Part(TexRole.End) is null) return null;

        if (!StandardCommands.Environments.TryGetValue(TexParser.NameOf(begin), out var arrangement))
            return null;

        if (arrangement is StandardCommands.TransparentEnvironment)
        {
            var body = part.Parts.Where(child => child.Role is not (TexRole.Begin or TexRole.End or TexRole.Option));
            return Pieces(body, style, knowledge) is { Count: > 0 } built ? Sequenced(built, part) : null;
        }

        foreach (var child in part.Parts)
            if (child.Role is not (TexRole.Begin or TexRole.End or TexRole.Option or Roles.Row))
                return null;

        if (Grid(part, style, knowledge) is not { } cells) return null;

        return arrangement switch
        {
            MatrixCommandParser matrix => Arranged(matrix, cells, part),
            ArrayCommandParser => Arrayed(part, cells),
            _ => null,
        };
    }

    /// <summary>The grid, row by row and squared off, as <see cref="Cells"/>.</summary>
    private static List<List<Item>>? Grid(ContentPart environment, string? style, TexFormulaParser knowledge)
    {
        var rows = new List<List<Item>>();

        foreach (var row in environment.Children)
        {
            if (row.Role != Roles.Row) continue;

            if (row.Children.Where(child => child.Role == Roles.Cell)
                   .All(cell => cell.Parts.All(piece => IsRule(piece) || piece.Kind == Kinds.Space)))
                continue;

            var cells = new List<Item>();

            foreach (var cell in row.Children)
            {
                if (cell.Role != Roles.Cell) continue;

                var built = Pieces(cell.Parts.Where(piece => !IsRule(piece)), style, knowledge);
                if (built is null) return null;

                cells.Add(built.Count switch
                {
                    0 => NullItem(),
                    1 => built[0],
                    _ => Sequenced(built, cell),
                });
            }

            rows.Add(cells);
        }

        if (rows.Count == 0) return null;

        var columns = rows.Max(row => row.Count);
        if (columns == 0) return null;

        foreach (var row in rows)
            while (row.Count < columns) row.Add(NullItem());

        return rows;
    }

    /// <summary>A grid arranged as its environment arranges one — padded, aligned, bracketed and sized — as <see cref="MatrixCommandParser.Assemble"/>.</summary>
    private static Item Arranged(MatrixCommandParser arrangement, List<List<Item>> cells, ContentPart origin)
    {
        var grid = Made(TexAtomType.Ordinary, origin, environment => Matrix(
            cells, environment, arrangement.CellAlignment, arrangement.VerticalPadding, arrangement.HorizontalPadding,
            suppressOuterPadding: arrangement.CellAlignment != MatrixCellAlignment.Aligned,
            rowStrutHeight: arrangement.RowStrut ? MatrixCommandParser.DefaultRowStrutHeight : 0,
            rowStrutDepth: arrangement.RowStrut ? MatrixCommandParser.DefaultRowStrutDepth : 0));

        Glyph? Delimiter(string? name) =>
            name == null
                ? null
                : Glyph.Delimiter(name) is { } symbol
                    ? symbol
                    : throw new TexParseException($"The delimiter {name} could not be found");

        var left = Delimiter(arrangement.LeftDelimiter);
        var right = Delimiter(arrangement.RightDelimiter);

        var item = left is null && right is null ? grid : Fenced(grid, left, right, origin);

        return arrangement.Style is { } style ? Styled(item, style, origin) : item;
    }

    /// <summary><c>\begin{array}</c>, its preamble read as text, as <see cref="Array"/>.</summary>
    private static Item? Arrayed(ContentPart part, List<List<Item>> cells)
    {
        if (part.Part(TexRole.Option) is not { } option) return null;

        var preamble = option.Node.Print();
        if (preamble.Length < 2 || preamble[0] != '{' || preamble[^1] != '}') return null;

        var written = preamble[1..^1];
        var columns = cells.Count == 0 ? 0 : cells.Max(row => row.Count);
        if (columns == 0) return null;

        ArrayColumnSpec spec;
        if (!written.Any(c => c is 'l' or 'c' or 'r'))
        {
            spec = ArrayColumnSpec.Centred(columns);
        }
        else
        {
            try
            {
                spec = ArrayColumnSpec.Parse(written);
            }
            catch (TexParseException)
            {
                return null;
            }
        }

        var rules = Ruled(part);

        return Made(TexAtomType.Ordinary, part, environment => Matrix(
            cells, environment, MatrixCellAlignment.Center,
            verticalPadding: 0,
            horizontalPadding: MatrixCommandParser.DefaultColumnGap,
            suppressOuterPadding: true,
            columnSpec: spec,
            horizontalRules: rules,
            rowStrutHeight: MatrixCommandParser.DefaultRowStrutHeight,
            rowStrutDepth: MatrixCommandParser.DefaultRowStrutDepth));
    }

    /// <summary><c>\substack</c>: the lines of a limit, as a small grid set solid.</summary>
    private static Item? Substacked(ContentPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(TexRole.Base) is not { } lines) return null;
        if (Grid(lines, style, knowledge) is not { } stack) return null;

        return Arranged(MatrixCommandParser.SubStack, stack, part);
    }

    /// <summary>
    /// A grid: columns as wide as their widest cell, rows on a common baseline with a line's room between them, the whole
    /// centred on the axis, and an array's rules over it.
    /// </summary>
    private static Set Matrix(
        List<List<Item>> rows, TexEnvironment environment, MatrixCellAlignment alignment,
        double verticalPadding, double horizontalPadding, bool suppressOuterPadding = false,
        ArrayColumnSpec? columnSpec = null, IReadOnlyCollection<int>? horizontalRules = null,
        double rowStrutHeight = 0, double rowStrutDepth = 0)
    {
        const double lineSkip = 0.1;
        const double alignGroupLeftPadding = 4;

        var cells = rows.Select(row => row.Select(cell => cell.Make(environment, null)).ToArray()).ToArray();
        var columnCount = cells.Length == 0 ? 0 : cells.Max(row => row.Length);

        var columnWidths = new double[columnCount];
        foreach (var row in cells)
            for (var j = 0; j < row.Length; j++)
                columnWidths[j] = System.Math.Max(columnWidths[j], row[j].TotalWidth);

        (double Left, double Right) Gaps(double free, int column)
        {
            var half = horizontalPadding / 2;

            if (columnSpec is not null)
                return columnSpec.AlignmentOf(column) switch
                {
                    TexAlignment.Left => (half, half + free),
                    TexAlignment.Right => (half + free, half),
                    _ => (half + free / 2, half + free / 2),
                };

            return alignment switch
            {
                MatrixCellAlignment.Aligned => (column % 2) switch
                {
                    0 when column != 0 => (alignGroupLeftPadding + half + free, half),
                    0 => (half + free, half),
                    _ => (half, half + free),
                },
                MatrixCellAlignment.Left => (half, half + free),
                _ => (half + free / 2, half + free / 2),
            };
        }

        (double Left, double Right) Outer(int column)
        {
            if (!suppressOuterPadding) return (0, 0);

            var half = horizontalPadding / 2;
            return (column == 0 ? -half : 0, column == columnCount - 1 ? -half : 0);
        }

        var columnEdges = new List<double>();
        var rowHeights = new List<double>();
        var rowSets = new List<Set>();

        for (var r = 0; r < cells.Length; r++)
        {
            var placed = new List<(Set Cell, double Left, double Right)>();
            for (var column = 0; column < columnCount; column++)
            {
                var cell = column < cells[r].Length ? cells[r][column] : Strut(0, 0, 0);
                var (left, right) = Gaps(columnWidths[column] - cell.TotalWidth, column);
                var (outerLeft, outerRight) = Outer(column);
                placed.Add((cell, left + outerLeft, right + outerRight));
            }

            // Every cell on the row's baseline; a line's room between rows, but none above the first or below the last.
            var ascent = placed.Count > 0 ? placed.Max(p => p.Cell.Height) : 0.0;
            var descent = placed.Count > 0 ? placed.Max(p => p.Cell.Depth) : 0.0;
            var rowAscent = r == 0 ? ascent : System.Math.Max(ascent + lineSkip, rowStrutHeight);
            var rowDescent = r == cells.Length - 1 ? descent : System.Math.Max(descent, rowStrutDepth);
            var halfPadding = verticalPadding / 2;

            var edgesFromThisRow = columnEdges.Count == 0 && placed.Count == columnCount;
            var edge = 0.0;

            var row = new List<Set>();
            foreach (var (cell, left, right) in placed)
            {
                var top = rowAscent - cell.Height + halfPadding;
                var bottom = rowDescent - cell.Depth + halfPadding;
                var stack = Vertical([Strut(0, top, 0), cell, Strut(0, bottom, 0)]);

                row.Add(Strut(left, 0, 0));
                row.Add(stack with { Height = stack.TotalHeight, Depth = 0 });
                row.Add(Strut(right, 0, 0));

                if (edgesFromThisRow) columnEdges.Add(edge);
                edge += left + cell.TotalWidth + right;
            }

            var rowSet = Horizontal(row, null, null);
            rowHeights.Add(rowSet.TotalHeight);
            rowSets.Add(rowSet);
        }

        var axis = environment.MathFont.GetAxisHeight(environment.Style);
        var measured = Vertical(rowSets);
        var total = measured.TotalHeight;
        var grid = Vertical(rowSets, height: total / 2 + axis, depth: total / 2 - axis);

        var wantsVertical = columnSpec?.VerticalRules.Count > 0;
        var wantsHorizontal = horizontalRules?.Count > 0;
        if (!wantsVertical && !wantsHorizontal) return grid;

        var thickness = environment.MathFont.GetDefaultLineThickness(environment.Style);

        var verticalAt = new List<double>();
        if (columnSpec is not null)
            foreach (var boundary in columnSpec.VerticalRules)
                verticalAt.Add(boundary < columnEdges.Count ? columnEdges[boundary] : grid.Width - thickness);

        var horizontalAt = new List<double>();
        if (horizontalRules is not null)
            foreach (var boundary in horizontalRules)
            {
                var y = 0.0;
                for (var i = 0; i < boundary && i < rowHeights.Count; i++) y += rowHeights[i];
                horizontalAt.Add(boundary >= rowHeights.Count ? y - thickness : y);
            }

        var rules = Set.GridRules(environment, verticalAt, horizontalAt, thickness, grid.Width, grid.Height, grid.Depth);

        return Layered([grid, rules]) with { Height = grid.Height, Depth = grid.Depth, Width = grid.Width };
    }

    /// <summary>Something between delimiters that grow to hold it, as <see cref="Fence"/>.</summary>
    private static Item? Fenced(ContentPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(Roles.Body) is not { } body) return null;
        if (Piece(body, style, knowledge) is not { } inside) return null;

        if (part.Part(Roles.Open) is not { } open) return null;
        if (part.Part(Roles.Close) is not { } close) return null;

        var left = DelimiterGlyph(open);
        var right = DelimiterGlyph(close);

        return new Item(TexAtomType.Opening, TexAtomType.Closing, null, (environment, _) =>
            Fence(inside, left, right, environment) with { Part = part });
    }

    private static Set Fence(Item inside, Glyph? left, Glyph? right, TexEnvironment environment)
    {
        var texFont = environment.MathFont;
        var style = environment.Style;

        var body = inside.Make(environment, null);

        var axis = texFont.GetAxisHeight(style);
        var delta = System.Math.Max(body.Height - axis, body.Depth + axis);
        var minHeight = System.Math.Max(delta / 500 * 901, 2 * delta - 0.5);

        Set Delimited(Glyph symbol)
        {
            // Which part drew it: a delimiter is built from a name and a height, so it has to be said.
            var set = Delimiter(symbol.SymbolName!, minHeight, environment) with { Part = symbol.Origin };
            return set with { Shift = -((set.Height + set.Depth) / 2 - set.Height) - axis };
        }

        var row = new List<Set>();

        if (left is not null && left.SymbolName != Glyph.EmptyDelimiterName) row.Add(Delimited(left));
        if (!inside.IsKern) row.Add(Set.Glue(Glue.Between(TexAtomType.Opening, inside.Left, environment)));
        row.Add(body);
        if (!inside.IsKern) row.Add(Set.Glue(Glue.Between(inside.Right, TexAtomType.Closing, environment)));
        if (right is not null && right.SymbolName != Glyph.EmptyDelimiterName) row.Add(Delimited(right));

        return Horizontal(row, null, null);
    }

    // ── One part ────────────────────────────────────────────────────────────

    /// <summary>Whether this is an equation's number — <c>\tag</c>, or <c>\tag*</c> without its parentheses.</summary>
    private static bool IsTag(ContentPart part) =>
        part.Kind == TexKinds.Command && part.Part(Roles.Name)?.Text is @"\tag" or @"\tag*";

    /// <summary>Whether this piece is an <c>\hline</c> — a rule across the table, not a cell's contents.</summary>
    private static bool IsRule(ContentPart part) =>
        part.Kind == TexKinds.Command && part.Part(Roles.Name)?.Text == @"\hline";

    /// <summary>
    /// The row boundaries carrying a rule, numbered from 0 above the first row.
    ///
    /// <para>
    /// <c>\hline</c> is written inside the first cell of the row it sits above, which is where the reading
    /// leaves it — so this is a question to ask of the grid and never of a cell. A row holding nothing but
    /// rules is not a row at all: it is the line under the last one, and it names the boundary past the
    /// end rather than adding an empty line to the table.
    /// </para>
    /// <para>
    /// The builder used to hand <see cref="ArrayCommandParser.Assemble"/> a null here, so an array asking
    /// for rules got none and the <c>\hline</c> itself was shown as its own characters.
    /// </para>
    /// </summary>
    private static List<int> Ruled(ContentPart environment)
    {
        var rules = new List<int>();
        var at = 0;

        foreach (var row in environment.Children)
        {
            if (row.Role != Roles.Row) continue;

            var cells = row.Children.Where(child => child.Role == Roles.Cell).ToList();
            if (cells.Any(cell => cell.Parts.Any(IsRule))) rules.Add(at);

            // A row of rules and nothing else does not become a line of the table, so the rows after it
            // are not pushed down by one.
            var written = cells.Any(cell => cell.Parts.Any(piece => !IsRule(piece) && piece.Kind != Kinds.Space));
            if (written) at++;
        }

        return rules;
    }

    /// <summary>
    /// Whether these braces were written by the reader as part of the formula, rather than being how a
    /// command's argument was delimited.
    ///
    /// <para>
    /// The distinction decides whether the group becomes an atom of its own, and it is not the same as
    /// what the group is called. `{x}` standing in a run was written; so was the `{\gamma}` of
    /// <c>{\gamma}^2</c>, which is a script's base and got its script afterwards. But the `{q}` of
    /// <c>\dot{q}</c> is *also* a base, and it was not written — it is where <c>\dot</c>'s argument
    /// stops. Only what holds the group can tell those two apart.
    /// </para>
    /// </summary>
    private static bool Written(ContentPart group) =>
        group.Role == Roles.Element
        || (group.Role == TexRole.Base && group.Parent?.Kind == TexKinds.Script);

    /// <summary>
    /// What this part switches, when it is a switch standing in a run rather than a command with an
    /// argument. Which of the two it is comes from the engine's own table — the same table the parser
    /// reads, so neither of us can come to think <c>\bf</c> takes an argument while the other does not.
    /// </summary>
    private static (string? TextStyle, TexStyle? Style)? Switch(ContentPart part)
    {
        if (part.Kind != TexKinds.Command || part.Parts.Any()) return null;
        if (part.Part(Roles.Name)?.Text is not { } name) return null;

        return StandardCommands.IsSwitch(name[1..], out var textStyle, out var style)
            ? (textStyle, style)
            : null;
    }

    /// <summary>
    /// The commands this sets itself, rather than by asking the symbol tables for a glyph.
    ///
    /// <para>
    /// One list, beside the switch that acts on it, because the alternative was two. What can be drawn is
    /// something the <em>reading</em> has to know — it marks whatever cannot be, before anything is built,
    /// which is what lets the builder set the tree it is handed without arguing with it. The reading was
    /// asking a table describing a parser that has since been deleted, and that table had never heard of
    /// <c>\ </c>: a written space came out underlined in red because two statements of one fact were free
    /// to disagree.
    /// </para>
    /// <para>
    /// <c>BuilderSetsWhatItSaysItSets</c> holds this to the switch below, so a case added without a name
    /// added here fails rather than quietly reddening itself.
    /// </para>
    /// </summary>
    internal static readonly IReadOnlySet<string> Handles = new HashSet<string>(System.StringComparer.Ordinal)
    {
        @"\frac", @"\sqrt", @"\substack", @"\overline", @"\underline", @"\not",
        @"\mspace", @"\hspace", @"\hspace*", @"\kern", @"\mkern", @"\ ", @"\nbsp",
    };

    /// <summary>
    /// The commands this takes as structure rather than drawing: they say something about what is around
    /// them and make no mark of their own.
    ///
    /// <para>
    /// Separate from <see cref="Handles"/> because they are a different claim. A name in Handles has a
    /// case in the switch below and turns into an atom; one here is read off the tree and consumed —
    /// <c>\hline</c> becomes a rule the grid draws, <c>\limits</c> becomes the way an operator wears its
    /// scripts. Both must answer yes to <see cref="Draws"/>, or the reading marks them undrawable and
    /// shows them as their own characters; only the first kind can be checked against the switch.
    /// </para>
    /// </summary>
    internal static readonly IReadOnlySet<string> Absorbs = new HashSet<string>(System.StringComparer.Ordinal)
    {
        @"\hline", @"\limits", @"\nolimits",
    };

    /// <summary>
    /// Whether anything here can set this command, given its name as written, backslash and all.
    ///
    /// <para>
    /// What the reading asks before it decides to show something as its own characters. The answer is
    /// this builder's to give — it is what does the setting — and it is either something set here by name
    /// or something the tables have a glyph, an expansion or a face for.
    /// </para>
    /// </summary>
    public static bool Draws(string written, TexFormulaParser knowledge) =>
        Handles.Contains(written)
        || Absorbs.Contains(written)
        || (written.Length > 1 && PrimitiveItem(written[1..], null) is not null)
        || knowledge.Draws(written);

    // ── Bookkeeping ─────────────────────────────────────────────────────────

    private static bool Discarded(ContentPart part) =>
        part.Kind == TexKinds.Command
        && part.Part(Roles.Name)?.Text is { } name
        && StandardCommands.IsDiscarded(name[1..]);

    /// <summary>
    /// What a braced argument holds, as it was written — the braces themselves left off, and everything
    /// else, spaces and all, exactly as typed.
    /// </summary>
    private static string Inside(ContentPart argument)
    {
        if (argument.Children.Count == 0) return argument.Node.Print();

        var text = new System.Text.StringBuilder();

        foreach (var child in argument.Children)
            if (child.Role is not (Roles.Open or Roles.Close))
                text.Append(child.Node.Print());

        return text.ToString();
    }

    /// <summary>Where a part with this role was written among its siblings, or -1 for none.</summary>
    private static int Order(ContentPart whole, string role)
    {
        for (var at = 0; at < whole.Children.Count; at++)
            if (whole.Children[at].Role == role) return at;

        return -1;
    }

    /// <summary>
    /// The whole part behind the read-only view — the one place the narrowing is undone.
    ///
    /// <para>
    /// Everything in this file holds parts as <see cref="ContentPart"/>, so nothing here can read a position
    /// while it builds. What gets <em>stored</em> is the whole part, because the thing that follows the
    /// link afterwards is an editor and an editor needs to know where things are. Both halves are wanted,
    /// and the seam between them is worth having in exactly one place rather than at each handoff.
    /// </para>
    /// <para>
    /// <see cref="ContentPart"/> is the only reading of a formula there is and it is sealed, so this cannot
    /// fail; if it ever could, a part that is not one is not part of any formula and failing loudly is
    /// the right answer.
    /// </para>
    /// </summary>
    private static ContentPart Whole(ContentPart part) => (ContentPart)part;
}
