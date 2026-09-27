using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Latex;

/// <summary>
/// A command as <see cref="Stages.ResolveCommands"/> leaves it: the command as written — its name, its arguments, where each
/// was written — and what it means. It prints as the command written.
/// </summary>
internal sealed class TexCommandNode : ContentNode
{
    internal TexCommandNode(ContentNode written, TexMeaning meaning) : base(written) => this.Meaning = meaning;

    /// <summary>Which construct it is, and whatever its name says about how that construct is set.</summary>
    public TexMeaning Meaning { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new TexCommandNode(shape, this.Meaning);
}

/// <summary>
/// An environment as <see cref="Stages.ResolveCommands"/> leaves it: its <c>\begin</c>, its rows and cells and its <c>\end</c>
/// as written, and how it arranges them. It prints as the environment written.
/// </summary>
internal sealed class TexGridNode : ContentNode
{
    internal TexGridNode(ContentNode written, TexArrangement arrangement) : base(written) => this.Arrangement = arrangement;

    /// <summary>A grid and how it is set, an <c>array</c> and its columns, or nothing but its contents.</summary>
    public TexArrangement Arrangement { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new TexGridNode(shape, this.Arrangement);
}

/// <summary>
/// A character that is not set as the letter it is (<see cref="Stages.ResolveCommands"/>): a symbol — <c>+</c> is
/// <c>plus</c>, a binary operator — a tie, which is a space written as a character, or a prime standing where there is
/// nothing for it to mark.
/// </summary>
internal sealed class TexCharNode : ContentNode
{
    internal TexCharNode(ContentNode written, TexSymbol? symbol, TexCharacter character) : base(written)
    {
        this.Symbol = symbol;
        this.Character = character;
    }

    /// <summary>The symbol it is set as, for a <see cref="TexCharacter.Symbol"/>.</summary>
    public TexSymbol? Symbol { get; }

    public TexCharacter Character { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new TexCharNode(shape, this.Symbol, this.Character);
}

/// <summary>What a character that is not a letter is.</summary>
internal enum TexCharacter
{
    /// <summary>A symbol by the table — set as its letter all the same in a face whose argument is words.</summary>
    Symbol,

    /// <summary><c>~</c>: a space that does not break.</summary>
    Tie,

    /// <summary><c>'</c> with nothing before it to mark, which nothing sets.</summary>
    Prime,
}
