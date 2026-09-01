using System.Diagnostics;

namespace Nexaflow.Maths.Latex;

/// <summary>
/// Reads LaTeX into a tree that prints back exactly what was read.
///
/// <para>
/// It never throws and never drops a character. Everything it cannot make sense of — a <c>}</c> that
/// closes nothing, a <c>\frac</c> whose second argument has not been typed yet — is held as it stands
/// and the reading carries on around it. That is not leniency for its own sake: half-finished input is
/// what an editor holds all day, and a parser that refuses it is a parser the editor cannot use.
/// </para>
/// <para>
/// What it knows about meaning is entirely in <see cref="TexCommands"/>. Everything else here is
/// syntax, and syntax is the part that cannot be wrong without the source coming back different.
/// </para>
/// </summary>
public static class TexParser
{
    /// <summary>The formula, read.</summary>
    public static TexNode Parse(string latex)
    {
        ArgumentNullException.ThrowIfNull(latex);

        var reader = new Reader(TexLexer.Scan(latex));
        return TexNode.Branch(TexKind.Sequence, reader.Run(Until.Input));
    }

    /// <summary>What brings a run of things to an end, besides running out of input.</summary>
    [Flags]
    private enum Until
    {
        Input = 0,
        CloseBrace = 1,
        End = 2,
        Right = 4,

        /// <summary>An <c>&amp;</c> or a <c>\\</c> — and <c>\end</c>, which ends the cell holding it too.</summary>
        Cell = 8,
    }

    private sealed class Reader(List<TexToken> tokens)
    {
        private int _at;

        private bool Done => _at >= tokens.Count;

        private TexToken Peek => tokens[_at];

        private TexToken Take() => tokens[_at++];

        // ── Runs ────────────────────────────────────────────────────────────

        public List<TexNode> Run(Until until)
        {
            var nodes = new List<TexNode>();

            while (!this.Done && !this.Stops(until))
            {
                var before = _at;
                var item = this.Item(until);
                nodes.Add(this.Scripted(item, until));

                // A script written after something that cannot carry one is a *prefix* on what comes
                // after it — the `_{\wedge}` of `\int C ~ _{\wedge} d T` is the dT's, and the 14 and 6
                // of carbon-14 are the C's. Something has to be there and be unable to take it: a tie,
                // a space, a mark. A script with nothing at all before it, first inside a group, is a
                // different case and keeps its empty base.
                if (!Carries(item) && !this.Done && !this.Stops(until) && this.NextIsScript())
                    nodes.Add(this.ScriptOnWhatFollows(until));

                // Every branch of Item consumes at least one token. If one ever stops doing so this
                // would spin forever on a formula somebody typed, so it is asserted rather than trusted.
                Debug.Assert(_at > before, "the reader made no progress");
            }

            return nodes;
        }

        private bool Stops(Until until)
        {
            var token = this.Peek;

            if (token.Kind == TexTokenKind.CloseBrace && until.HasFlag(Until.CloseBrace)) return true;
            if (token.Kind == TexTokenKind.Ampersand && until.HasFlag(Until.Cell)) return true;
            if (token.Symbol(@"\\") && until.HasFlag(Until.Cell)) return true;
            if (token.Is(@"\right") && until.HasFlag(Until.Right)) return true;

            // \end closes the environment, and with it the row and the cell it was reached through.
            if (token.Is(@"\end") && (until.HasFlag(Until.End) || until.HasFlag(Until.Cell))) return true;

            return false;
        }

        private TexNode Item(Until until)
        {
            var token = this.Peek;

            switch (token.Kind)
            {
                case TexTokenKind.OpenBrace:
                    return this.Group(until);

                case TexTokenKind.Space:
                    return TexNode.Leaf(TexKind.Space, this.Take().Text);

                case TexTokenKind.Comment:
                    return TexNode.Leaf(TexKind.Comment, this.Take().Text);

                case TexTokenKind.Character:
                    return TexNode.Leaf(TexKind.Char, this.Take().Text);

                // Machinery that turned up where content goes: a brace closing nothing, an alignment tab
                // outside a table. Held rather than read, and rather than thrown.
                case TexTokenKind.CloseBrace:
                case TexTokenKind.Ampersand:
                    return TexNode.Leaf(TexKind.Verbatim, this.Take().Text);

                case TexTokenKind.Superscript:
                case TexTokenKind.Subscript:
                    return this.Script(null, until);

                case TexTokenKind.ControlWord when token.Text == @"\begin":
                    return this.Environment(until);

                case TexTokenKind.ControlWord when token.Text == @"\left":
                    return this.Fence(until);

                default:
                    return this.Command(until);
            }
        }

        // ── Scripts ─────────────────────────────────────────────────────────

        /// <summary>
        /// Whatever was just read, with whatever was written onto it: its scripts, and its marks.
        /// <para>
        /// This is where a unit stops being one token. <c>f'</c> is one thing to select and to move, and
        /// <c>x''_{i}</c> is one thing whose subscript is on the <c>x</c> — neither is decidable until
        /// what follows has been read, which is why it is decided here, once what follows is known, and
        /// not by anything downstream working it out again from a run of siblings.
        /// </para>
        /// </summary>
        private TexNode Scripted(TexNode node, Until until)
        {
            if (!Carries(node)) return node;

            return this.NextIsMark() || this.NextIsScript() ? this.Script(node, until) : node;
        }

        /// <summary>
        /// Whether what is written after this would be set <em>on</em> it.
        /// <para>
        /// A script attaches to the atom before it, and not everything written before one is an atom.
        /// Space is not — the space in <c>x ^2</c> belongs to the script that swallows it, not the other
        /// way round — and neither is a tie, which is a space written as a character. Nothing is set on
        /// those: what follows starts on a base of its own, which is what <c>~^{\nu}</c> means.
        /// </para>
        /// </summary>
        private static bool Carries(TexNode node) =>
            node.Kind is not (TexKind.Space or TexKind.Comment)
            && node.Text is not ("~" or "'");   // nor a mark, which is written onto something itself

        private bool NextIsScript() => Next() is { } token
                                       && token.Kind is TexTokenKind.Superscript or TexTokenKind.Subscript;

        /// <summary>Whether a mark follows — an apostrophe, which is written onto what precedes it.</summary>
        private bool NextIsMark() => Next() is { } token
                                     && token.Kind == TexTokenKind.Character && token.Text == "'";

        /// <summary>The next token that is not trivia, without taking it.</summary>
        private TexToken? Next()
        {
            var at = _at;
            while (at < tokens.Count && tokens[at].IsTrivia) at++;

            return at < tokens.Count ? tokens[at] : null;
        }

        /// <summary>
        /// A base and everything written onto it, as one thing. Both of <c>x^2_i</c> belong to the same
        /// x, so they are gathered here rather than left as a script wrapping a script — and so do the
        /// primes of <c>x''_{i}</c>, which is the same rule and the reason the subscript lands on the x
        /// rather than on the prime standing immediately before it.
        /// </summary>
        /// <summary>
        /// A base and everything written onto it, where the base is written <em>after</em> the scripts:
        /// the <c>_{\wedge} d T</c> of <c>\int C ~ _{\wedge} d T</c>.
        /// <para>
        /// A script has to be set on something, and where nothing before it can carry one — a tie is a
        /// space, and a space is not an atom — what follows takes it instead. So the base is read here,
        /// last, and the node holds it last: the children of this tree are in the order they were
        /// written, always, which is what lets it print back as itself without anything knowing that the
        /// base of this particular one came at the end. Its <em>role</em> says what it is; its
        /// <em>position</em> says where it was typed. Those are different questions and this is the one
        /// construct where the answers disagree.
        /// </para>
        /// </summary>
        private TexNode ScriptOnWhatFollows(Until until)
        {
            var script = this.Script(null, until);

            if (this.Done || this.Stops(until)) return script;

            var children = new List<TexNode>(script.Children);
            this.Trivia(children);

            if (this.Done || this.Stops(until)) return script.With(children);

            children.Add(this.Item(until).As(TexRole.Base));
            return script.With(children);
        }

        private TexNode Script(TexNode? baseNode, Until until)
        {
            var children = new List<TexNode>();
            if (baseNode is not null) children.Add(baseNode.As(TexRole.Base));

            while (true)
            {
                if (this.NextIsMark())
                {
                    this.Trivia(children);
                    children.Add(TexNode.Leaf(TexKind.Char, this.Take().Text, TexRole.Mark));
                    continue;
                }

                if (!this.NextIsScript()) break;

                this.Trivia(children);

                var written = this.Take();
                children.Add(TexNode.Leaf(TexKind.Token, written.Text, TexRole.Name));
                var role = written.Kind == TexTokenKind.Superscript
                    ? TexRole.Superscript
                    : TexRole.Subscript;

                this.Trivia(children);
                if (this.Argument(until) is { } argument) children.Add(argument.As(role));
            }

            return TexNode.Branch(TexKind.Script, children);
        }

        // ── Groups, commands, arguments ─────────────────────────────────────

        private TexNode Group(Until until, bool grid = false)
        {
            var children = new List<TexNode> { TexNode.Leaf(TexKind.Token, this.Take().Text, TexRole.Open) };

            // Braces are a fresh context for everything except \end. An & inside them is not a cell
            // boundary, and a \right inside them closes nothing — but an \end still has to be able to
            // finish the environment that got here, or an unclosed brace would swallow the rest of it.
            var inner = Until.CloseBrace;
            if (until.HasFlag(Until.Cell) || until.HasFlag(Until.End)) inner |= Until.End;

            children.AddRange(grid ? this.Rows(inner) : this.Run(inner));

            if (!this.Done && this.Peek.Kind == TexTokenKind.CloseBrace)
                children.Add(TexNode.Leaf(TexKind.Token, this.Take().Text, TexRole.Close));

            return TexNode.Branch(TexKind.Group, children);
        }

        private TexNode Command(Until until)
        {
            var name = this.Take().Text;

            // A starred command is one command, not a command and a times sign — but only where the
            // table says a starred form exists. Absorbing every asterisk after every control word would
            // quietly take the multiplication out of `\alpha * \beta`.
            if (!this.Done
                && this.Peek.Kind == TexTokenKind.Character
                && this.Peek.Text == "*"
                && TexCommands.Lookup(name + "*") is not null)
                name += this.Take().Text;

            var children = new List<TexNode> { TexNode.Leaf(TexKind.Token, name, TexRole.Name) };

            // Nothing in the table takes arguments *and* is shorthand for something, so a macro is only
            // ever looked for here, where a command turns out to take none.
            if (TexCommands.Lookup(name) is not { } command)
                return Resolved(TexNode.Branch(TexKind.Command, children), name);

            if (command.Option is { } option) this.Optional(children, option, until);

            foreach (var role in command.Arguments)
            {
                // The trivia is taken on approval. A command that never got its argument does not own
                // the space after it either — that space is between two things, not inside one.
                var mark = _at;
                var trivia = new List<TexNode>();
                this.Trivia(trivia);

                if (this.Argument(until, command.Grid) is not { } argument) { _at = mark; break; }

                children.AddRange(trivia);
                children.Add(argument.As(role));
            }

            return TexNode.Branch(TexKind.Command, children);
        }

        /// <summary>
        /// The same command with what it is shorthand for hung underneath it, where it is shorthand for
        /// anything.
        ///
        /// <para>
        /// The expansion is parsed here rather than anywhere later because it is a reading, and this is
        /// the reader. It stands for no source — see <see cref="TexRole.Expansion"/> — so hanging it on
        /// changes nothing about what the tree prints back, only about what can be asked of it.
        /// </para>
        /// <para>
        /// Bounded, because a definition may name another macro and nothing stops a table from one day
        /// naming itself. Six is well past the deepest real chain (<c>\iff</c> reaches a strut in three)
        /// and shallow enough that a cycle stops rather than fills the stack.
        /// </para>
        /// </summary>
        private static TexNode Resolved(TexNode command, string name)
        {
            if (_depth >= 6 || TexMacros.Lookup(name) is not { } definition) return command;

            _depth++;
            try
            {
                var expansion = TexParser.Parse(definition).As(TexRole.Expansion);
                return command.With([.. command.Children, expansion]);
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>How many macros deep this reading already is. Per thread, like the reading itself.</summary>
        [System.ThreadStatic]
        private static int _depth;

        /// <summary>The one thing written as an argument: a braced group, a command, or a character.</summary>
        private TexNode? Argument(Until until, bool grid = false)
        {
            if (this.Done || this.Stops(until)) return null;

            var token = this.Peek;

            return token.Kind switch
            {
                TexTokenKind.OpenBrace => this.Group(until, grid),
                TexTokenKind.Character => TexNode.Leaf(TexKind.Char, this.Take().Text),
                TexTokenKind.ControlWord when token.Text == @"\begin" => this.Environment(until),
                TexTokenKind.ControlWord when token.Text == @"\left" => this.Fence(until),
                TexTokenKind.ControlWord or TexTokenKind.ControlSymbol => this.Command(until),
                _ => null,
            };
        }

        private void Optional(List<TexNode> children, string role, Until until)
        {
            var mark = _at;
            var trivia = new List<TexNode>();
            this.Trivia(trivia);

            if (!this.IsBracket("[")) { _at = mark; return; }

            var inner = new List<TexNode> { TexNode.Leaf(TexKind.Token, this.Take().Text, TexRole.Open) };

            while (!this.Done && !this.IsBracket("]") && !this.Stops(until))
            {
                var before = _at;
                inner.Add(this.Scripted(this.Item(until), until));
                if (_at == before) break;
            }

            if (this.IsBracket("]"))
                inner.Add(TexNode.Leaf(TexKind.Token, this.Take().Text, TexRole.Close));

            children.AddRange(trivia);
            children.Add(TexNode.Branch(TexKind.Group, inner, role));
        }

        private bool IsBracket(string bracket) =>
            !this.Done && this.Peek.Kind == TexTokenKind.Character && this.Peek.Text == bracket;

        private void Trivia(List<TexNode> into)
        {
            while (!this.Done && this.Peek.IsTrivia)
            {
                var token = this.Take();
                var kind = token.Kind == TexTokenKind.Space ? TexKind.Space : TexKind.Comment;
                into.Add(TexNode.Leaf(kind, token.Text, TexRole.Trivia));
            }
        }

        // ── Fences ──────────────────────────────────────────────────────────

        private TexNode Fence(Until until)
        {
            var children = new List<TexNode> { this.Command(until).As(TexRole.Open) };

            // What is between the delimiters is one thing — the fence's contents — rather than however
            // many things happen to be written there. Unlike a brace, a delimiter is drawn and carries
            // meaning, so a fence is a construct with a part, not a run with punctuation at each end.
            // Everything that turns on that distinction then reads it off the roles.
            children.Add(TexNode.Branch(TexKind.Sequence, this.Run(until | Until.Right), TexRole.Body));

            if (!this.Done && this.Peek.Is(@"\right"))
                children.Add(this.Command(until).As(TexRole.Close));

            return TexNode.Branch(TexKind.Fence, children);
        }

        // ── Environments ────────────────────────────────────────────────────

        private TexNode Environment(Until until)
        {
            var begin = this.Command(until);
            var definition = TexCommands.Environment(NameOf(begin));
            var children = new List<TexNode> { begin.As(TexRole.Begin) };

            if (definition.Spec is { } spec) this.Specification(children, spec, until);

            children.AddRange(definition.Grid
                ? this.Rows(until)
                : this.Run(until | Until.End));

            if (!this.Done && this.Peek.Is(@"\end"))
                children.Add(this.Command(until).As(TexRole.End));

            return TexNode.Branch(TexKind.Environment, children);
        }

        /// <summary>The <c>{cc}</c> of <c>\begin{array}{cc}</c> — the shape, not the contents.</summary>
        private void Specification(List<TexNode> children, string role, Until until)
        {
            var mark = _at;
            var trivia = new List<TexNode>();
            this.Trivia(trivia);

            if (this.Done || this.Peek.Kind != TexTokenKind.OpenBrace) { _at = mark; return; }

            children.AddRange(trivia);
            children.Add(this.Group(until).As(role));
        }

        private List<TexNode> Rows(Until until)
        {
            var rows = new List<TexNode>();
            var body = until | Until.Cell;

            while (true)
            {
                var cells = new List<TexNode>();
                bool more;

                do
                {
                    var cell = this.Run(body);
                    more = !this.Done && this.Peek.Kind == TexTokenKind.Ampersand;
                    if (more) cell.Add(TexNode.Leaf(TexKind.Token, this.Take().Text, TexRole.Separator));

                    cells.Add(TexNode.Branch(TexKind.Cell, cell, TexRole.Cell));
                }
                while (more);

                var row = new List<TexNode>(cells);
                var broken = !this.Done && this.Peek.Symbol(@"\\");
                if (broken) row.Add(this.Command(until).As(TexRole.Separator));

                rows.Add(TexNode.Branch(TexKind.Row, row, TexRole.Row));

                if (!broken || this.Done) break;
            }

            Trailing(rows);
            return rows;
        }

        /// <summary>
        /// A <c>\\</c> ends the row it is written on; it does not start another. What is left after the
        /// last one — the space before <c>\end</c>, and nothing else — is given back to the environment
        /// rather than made into a line nobody wrote.
        /// </summary>
        private static void Trailing(List<TexNode> rows)
        {
            if (rows.Count == 0) return;

            var last = rows[^1];
            if (last.Children.Count != 1) return;
            if (last.Part(TexRole.Separator) is not null) return;

            var only = last.Children[0];
            if (only.Part(TexRole.Separator) is not null) return;

            // On the text rather than on the kind: a cell with nothing in it has no children, so it is
            // its own only "leaf", and asking that leaf what kind it is answers Cell.
            if (only.Leaves().Any(leaf => leaf.Text.Length > 0
                                          && leaf.Kind is not (TexKind.Space or TexKind.Comment))) return;

            rows.RemoveAt(rows.Count - 1);
            foreach (var child in only.Children) rows.Add(child.As(TexRole.Element));
        }
    }

    /// <summary>The name an environment was begun with — <c>matrix</c> for <c>\begin{matrix}</c>.</summary>
    public static string NameOf(TexNode command) =>
        command.Part(TexRole.Argument) is { } argument ? Named(argument.Print()) : string.Empty;

    /// <inheritdoc cref="NameOf(TexNode)"/>
    public static string NameOf(ITexPart command) =>
        command.Part(TexRole.Argument) is { } argument ? Named(argument.Print()) : string.Empty;

    private static string Named(string argument)
    {
        var text = argument.Trim();
        return text.Length >= 2 && text[0] == '{' && text[^1] == '}'
            ? text[1..^1].Trim()
            : text;
    }
}
