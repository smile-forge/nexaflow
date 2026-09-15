using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Chemistry;

/// <summary>
/// Reads the body of a <c>smiles</c> block into a tree — the format described at
/// <see href="https://markdown.org/tools/diagrams/chemistry/"/>.
///
/// <para>
/// A line per molecule: a SMILES string, then an optional caption in double quotes. A <c>#</c> starts a comment,
/// which is unambiguous even though <c>#</c> is also SMILES's triple bond, because no SMILES string begins with a
/// bond. The <c>chemistry</c> keyword that names the format may open the block and may be left out.
/// </para>
/// <para>
/// <b>The molecule is read to the character</b> — every atom, bond, branch bracket and ring-closure digit its own
/// piece — because each of them is something the drawing is made from and a reader can point at. What the parser
/// does not do is say what any of it amounts to: which atom a ring closure reaches, how many hydrogens an atom
/// carries, where a benzene ring's double bonds go. Those are facts about the whole molecule, and they are the
/// stages' (<see cref="SmilesPipeline"/>).
/// </para>
/// <para>
/// What will not read is held as it was written with the reason, never dropped and never repaired: a bracket that
/// never closes is not closed for you, because the half-written atom is exactly what an editor holds while
/// somebody types it.
/// </para>
/// </summary>
public static class SmilesParser
{
    /// <summary>The word that may open a block, naming its format.</summary>
    public const string Keyword = "chemistry";

    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;
        var lines = new List<ContentNode>();
        var started = false;

        for (var at = 0; at < source.Length;)
        {
            var newline = source.IndexOf('\n', at);
            var stop = newline < 0 ? source.Length : newline + 1;

            var end = newline < 0 ? source.Length : newline;
            if (end > at && source[end - 1] == '\r') end--;

            lines.Add(Line(source[at..end], source[end..stop], ref started));
            at = stop;
        }

        return ContentNode.Branch(SmilesKinds.Block, lines);
    }

    /// <summary>
    /// One SMILES string, on its own — what a line's molecule is read as, and what anything holding a bare string
    /// rather than a block reads it with.
    /// </summary>
    public static ContentNode Molecule(string smiles) =>
        ContentNode.Branch(SmilesKinds.Molecule, new Reader(smiles).Chain(anchored: false, inBranch: false),
                           SmilesRoles.Molecule);

    /// <summary>
    /// One line and the characters that ended it. The space either side of what it says is trivia of the line.
    /// </summary>
    /// <param name="started">
    /// Whether anything but a comment has been read yet — the keyword only names the format before it has.
    /// </param>
    private static ContentNode Line(string body, string terminator, ref bool started)
    {
        var pieces = new List<ContentNode>();

        var lead = Leading(body);
        var trail = Trailing(body, lead);
        var text = body[lead..(body.Length - trail)];

        if (lead > 0) pieces.Add(Space(body[..lead]));

        if (text.Length == 0) { }
        else if (text[0] == '#') pieces.Add(ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia));
        else if (!started && text.Equals(Keyword, StringComparison.OrdinalIgnoreCase))
        {
            pieces.Add(ContentNode.Leaf(SmilesKinds.Header, text, Roles.Name));
            started = true;
        }
        else
        {
            pieces.Add(Entry(text));
            started = true;
        }

        if (trail > 0) pieces.Add(Space(body[(body.Length - trail)..]));
        if (terminator.Length > 0) pieces.Add(Space(terminator));

        return ContentNode.Branch(SmilesKinds.Line, pieces);
    }

    /// <summary>A molecule, and the caption after it when one was written.</summary>
    private static ContentNode Entry(string text)
    {
        // A SMILES string has no space and no quote in it, so either ends it — which is what lets a caption be
        // written hard against the molecule and still be a caption.
        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '"') end++;

        if (end == 0)
            return ContentNode.Shown(text, "A caption needs a SMILES string before it: CCO \"Ethanol\".");

        var pieces = new List<ContentNode> { Molecule(text[..end]) };

        var at = end;
        var gap = at;
        while (gap < text.Length && char.IsWhiteSpace(text[gap])) gap++;
        if (gap > at) pieces.Add(Space(text[at..gap]));
        at = gap;

        if (at < text.Length && text[at] == '"')
        {
            var close = text.IndexOf('"', at + 1);
            pieces.Add(Label(close < 0 ? text[at..] : text[at..(close + 1)], closed: close >= 0));
            at = close < 0 ? text.Length : close + 1;

            gap = at;
            while (gap < text.Length && char.IsWhiteSpace(text[gap])) gap++;
            if (gap > at) pieces.Add(Space(text[at..gap]));
            at = gap;

            if (at < text.Length)
                pieces.Add(ContentNode.Shown(text[at..], "Only a caption in double quotes may follow a molecule."));
        }
        else if (at < text.Length)
        {
            pieces.Add(ContentNode.Shown(text[at..], "A caption is written in double quotes: CCO \"Ethanol\"."));
        }

        return ContentNode.Branch(SmilesKinds.Entry, pieces);
    }

    /// <summary>A caption: its quotes, and what is between them.</summary>
    private static ContentNode Label(string text, bool closed)
    {
        var inner = closed ? text[1..^1] : text[1..];

        var pieces = new List<ContentNode>
        {
            ContentNode.Leaf(Kinds.Token, "\"", Roles.Open,
                             closed ? null : "This caption is never closed — end it with a double quote."),
        };

        if (inner.Length > 0) pieces.Add(ContentNode.Leaf(Kinds.Char, inner, SmilesRoles.Label));
        if (closed) pieces.Add(ContentNode.Leaf(Kinds.Token, "\"", Roles.Close));

        return ContentNode.Branch(SmilesKinds.Label, pieces, SmilesRoles.Label);
    }

    private static ContentNode Space(string text) => ContentNode.Leaf(Kinds.Space, text, Roles.Trivia);

    private static int Leading(string text)
    {
        var n = 0;
        while (n < text.Length && char.IsWhiteSpace(text[n])) n++;
        return n;
    }

    /// <summary>How much space ends <paramref name="text"/>, never reaching back past <paramref name="floor"/>.</summary>
    private static int Trailing(string text, int floor)
    {
        var n = 0;
        while (text.Length - n > floor && char.IsWhiteSpace(text[text.Length - n - 1])) n++;
        return n;
    }

    // ── One SMILES string ───────────────────────────────────────────────────

    /// <summary>
    /// A walk along one string. Recursive descent, because a branch is a chain inside round brackets and nests as
    /// deep as it was written.
    /// </summary>
    private sealed class Reader(string s)
    {
        private int _at;

        /// <summary>
        /// A chain of atoms, and everything written between and after them, up to the end of the string or the
        /// bracket that closes the branch it is in.
        /// </summary>
        /// <param name="anchored">
        /// Whether there is an atom for the chain's first bond to start from — the atom a branch hangs off.
        /// </param>
        public List<ContentNode> Chain(bool anchored, bool inBranch)
        {
            var items = new List<ContentNode>();
            var atom = anchored;

            while (_at < s.Length)
            {
                var c = s[_at];

                if (c == ')')
                {
                    if (inBranch) return items;

                    items.Add(ContentNode.Shown(")", "This closes a branch that was never opened."));
                    _at++;
                    continue;
                }

                if (c == '(')
                {
                    items.Add(Branch(atom));
                    continue;
                }

                if (IsBond(c))
                {
                    if (_at + 1 < s.Length && (char.IsAsciiDigit(s[_at + 1]) || s[_at + 1] == '%'))
                    {
                        items.Add(RingBond(atom));
                        continue;
                    }

                    var joins = _at + 1 < s.Length && StartsAtom(s[_at + 1]);
                    items.Add(ContentNode.Leaf(SmilesKinds.Bond, c.ToString(), SmilesRoles.Bond,
                        !atom ? "A bond has to follow an atom."
                        : !joins ? "A bond has to be followed by an atom."
                        : null));
                    _at++;
                    continue;
                }

                if (char.IsAsciiDigit(c) || c == '%')
                {
                    items.Add(RingBond(atom));
                    continue;
                }

                if (c == '.')
                {
                    items.Add(ContentNode.Leaf(SmilesKinds.Dot, ".", Roles.Separator));
                    atom = false;
                    _at++;
                    continue;
                }

                if (c == '[')
                {
                    items.Add(BracketAtom());
                    atom = true;
                    continue;
                }

                if (StartsAtom(c))
                {
                    items.Add(OrganicAtom(ref atom));
                    continue;
                }

                items.Add(ContentNode.Shown(c.ToString(), $"'{c}' is not part of SMILES."));
                _at++;
            }

            return items;
        }

        private static bool IsBond(char c) => c is '-' or '=' or '#' or '$' or ':' or '/' or '\\';

        private static bool StartsAtom(char c) => c == '[' || c == '*' || char.IsAsciiLetter(c);

        /// <summary>A side chain: its round brackets and the chain between them.</summary>
        private ContentNode Branch(bool anchored)
        {
            _at++;
            var inner = Chain(anchored: true, inBranch: true);
            var closed = _at < s.Length && s[_at] == ')';

            var trouble = !anchored ? "A branch has to follow an atom."
                        : !closed ? "This branch is never closed — end it with )."
                        : inner.Count == 0 ? "This branch is empty."
                        : null;

            var pieces = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, "(", Roles.Open, trouble) };
            pieces.AddRange(inner);

            if (closed)
            {
                pieces.Add(ContentNode.Leaf(Kinds.Token, ")", Roles.Close));
                _at++;
            }

            return ContentNode.Branch(SmilesKinds.Branch, pieces, SmilesRoles.Branch);
        }

        /// <summary>A ring closure: the bond written before its number, when there is one, and the number.</summary>
        private ContentNode RingBond(bool anchored)
        {
            var pieces = new List<ContentNode>();

            if (IsBond(s[_at]))
                pieces.Add(ContentNode.Leaf(SmilesKinds.Bond, s[_at++].ToString(), SmilesRoles.Bond));

            string? trouble = anchored ? null : "A ring closure has to follow an atom.";

            if (s[_at] == '%')
            {
                var digits = 0;
                while (digits < 2 && _at + 1 + digits < s.Length && char.IsAsciiDigit(s[_at + 1 + digits])) digits++;

                var number = s.Substring(_at, 1 + digits);
                _at += number.Length;

                if (digits < 2) trouble = "A ring number past 9 is written % and two digits: %10.";
                pieces.Add(ContentNode.Leaf(SmilesKinds.RingNumber, number, SmilesRoles.RingNumber));
            }
            else
            {
                pieces.Add(ContentNode.Leaf(SmilesKinds.RingNumber, s[_at++].ToString(), SmilesRoles.RingNumber));
            }

            var ring = ContentNode.Branch(SmilesKinds.RingBond, pieces, SmilesRoles.Ring);
            return trouble is null ? ring : ring.Saying(trouble);
        }

        /// <summary>
        /// A bare symbol: one of the organic subset, which is the only thing that may be written without brackets.
        /// </summary>
        private ContentNode OrganicAtom(ref bool atom)
        {
            var c = s[_at];

            var two = _at + 1 < s.Length ? s.Substring(_at, 2) : null;
            if (two is "Cl" or "Br")
            {
                _at += 2;
                atom = true;
                return Atom(two);
            }

            var one = c.ToString();
            if (Elements.IsOrganic(one))
            {
                _at++;
                atom = true;
                return Atom(one);
            }

            // An element that needs brackets, written without them. Held as the element rather than as its first
            // letter, so the reason names what was meant.
            if (two is not null && char.IsAsciiLetterUpper(c) && char.IsAsciiLetterLower(two[1]) && Elements.IsElement(two))
            {
                _at += 2;
                return ContentNode.Shown(two, $"{two} is written in brackets: [{two}].");
            }

            _at++;
            return Elements.IsElement(one)
                ? ContentNode.Shown(one, $"{one} is written in brackets: [{one}].")
                : ContentNode.Shown(one, $"'{one}' is not an atom SMILES can write without brackets.");
        }

        private static ContentNode Atom(string symbol) =>
            ContentNode.Branch(SmilesKinds.Atom, [ContentNode.Leaf(SmilesKinds.Symbol, symbol, SmilesRoles.Symbol)],
                               SmilesRoles.Atom);

        /// <summary>
        /// An atom in square brackets: <c>[</c> isotope? symbol chirality? hydrogens? charge? class? <c>]</c>. One
        /// that will not read is held whole, bracket to bracket, with the reason.
        /// </summary>
        private ContentNode BracketAtom()
        {
            var close = s.IndexOf(']', _at + 1);
            if (close < 0)
            {
                var rest = s[_at..];
                _at = s.Length;
                return ContentNode.Shown(rest, "This atom is never closed — end it with ].");
            }

            var text = s[_at..(close + 1)];
            _at = close + 1;

            return Bracket(text, out var trouble) is { } atom
                ? atom
                : ContentNode.Shown(text, trouble);
        }

        private static ContentNode? Bracket(string text, out string? trouble)
        {
            trouble = null;
            var inside = text[1..^1];
            var i = 0;

            var pieces = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, "[", Roles.Open) };

            var digits = Run(inside, i, char.IsAsciiDigit);
            if (digits > 0)
            {
                pieces.Add(ContentNode.Leaf(SmilesKinds.Isotope, inside.Substring(i, digits), SmilesRoles.Isotope));
                i += digits;
            }

            var symbol = Symbol(inside, i);
            if (symbol is null)
            {
                trouble = i >= inside.Length
                    ? "An atom in brackets needs an element."
                    : $"'{Word(inside, i)}' is not an element.";
                return null;
            }

            pieces.Add(ContentNode.Leaf(SmilesKinds.Symbol, symbol, SmilesRoles.Symbol));
            i += symbol.Length;

            if (i < inside.Length && inside[i] == '@')
            {
                var start = i++;
                if (i < inside.Length && inside[i] == '@') i++;
                else if (i + 1 < inside.Length && inside.Substring(i, 2) is "TH" or "AL" or "SP" or "TB" or "OH")
                {
                    i += 2;
                    i += Run(inside, i, char.IsAsciiDigit);
                }

                pieces.Add(ContentNode.Leaf(SmilesKinds.Chirality, inside[start..i], SmilesRoles.Chirality));
            }

            if (i < inside.Length && inside[i] == 'H')
            {
                var start = i++;
                i += Run(inside, i, char.IsAsciiDigit);
                pieces.Add(ContentNode.Leaf(SmilesKinds.Hydrogens, inside[start..i], SmilesRoles.Hydrogens));
            }

            if (i < inside.Length && inside[i] is '+' or '-')
            {
                var start = i;
                var sign = inside[i++];
                var more = Run(inside, i, char.IsAsciiDigit);
                i += more > 0 ? more : Run(inside, i, ch => ch == sign);
                pieces.Add(ContentNode.Leaf(SmilesKinds.Charge, inside[start..i], SmilesRoles.Charge));
            }

            if (i < inside.Length && inside[i] == ':')
            {
                var start = i++;
                var number = Run(inside, i, char.IsAsciiDigit);
                if (number == 0)
                {
                    trouble = "An atom class is a number after the colon: [CH3:1].";
                    return null;
                }

                i += number;
                pieces.Add(ContentNode.Leaf(SmilesKinds.Class, inside[start..i], SmilesRoles.Class));
            }

            if (i < inside.Length)
            {
                trouble = $"'{inside[i..]}' cannot stand there in an atom.";
                return null;
            }

            pieces.Add(ContentNode.Leaf(Kinds.Token, "]", Roles.Close));
            return ContentNode.Branch(SmilesKinds.Atom, pieces, SmilesRoles.Atom);
        }

        /// <summary>
        /// The element symbol starting at <paramref name="i"/>: two letters where they name an element, else one.
        /// Lowercase only for the symbols that may be aromatic.
        /// </summary>
        private static string? Symbol(string inside, int i)
        {
            if (i >= inside.Length) return null;
            if (inside[i] == '*') return "*";

            var two = i + 1 < inside.Length ? inside.Substring(i, 2) : null;

            if (char.IsAsciiLetterUpper(inside[i]))
            {
                if (two is not null && char.IsAsciiLetterLower(two[1]) && Elements.IsElement(two)) return two;
                var one = inside[i].ToString();
                return Elements.IsElement(one) ? one : null;
            }

            if (char.IsAsciiLetterLower(inside[i]))
            {
                if (two is not null && Elements.IsAromaticSymbol(two)) return two;
                var one = inside[i].ToString();
                return Elements.IsAromaticSymbol(one) ? one : null;
            }

            return null;
        }

        private static int Run(string text, int from, Func<char, bool> holds)
        {
            var n = 0;
            while (from + n < text.Length && holds(text[from + n])) n++;
            return n;
        }

        private static string Word(string text, int from) =>
            text[from..(from + Math.Max(1, Run(text, from, char.IsAsciiLetter)))];
    }
}
