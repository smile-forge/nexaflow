using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Latex.Stages;

/// <summary>
/// Says what every name in a formula means (<see cref="TexVocabulary"/>): each command — which construct it is, and whatever
/// its name and its values say about how it is set (<see cref="TexCommandNode"/>) — each environment and how it arranges what
/// it holds (<see cref="TexGridNode"/>), and each character that is not set as the letter it is (<see cref="TexCharNode"/>).
///
/// <para>
/// A node is said in place: the same piece, with the same parts in the same roles, standing for the same characters. So what a
/// construct holds is still where the parser put it, editing still finds every part where it was written, and a later stage
/// can gather these into bigger things — an operator over its operands — without anything here having to change. What sets
/// the formula reads the meanings off the tree and never a name.
/// </para>
/// <para>
/// A command nothing here knows is left as the parser read it, which is what says it is not a word LaTeX has
/// (<see cref="CheckDrawable"/>). One LaTeX has that nothing draws means <see cref="TexUnset"/>.
/// </para>
/// </summary>
public sealed class ResolveCommands : IAstStage
{
    public string Name => "latex:meanings";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, Resolved);

    private static ContentNode Resolved(ContentNode node) => node switch
    {
        TexCommandNode or TexGridNode or TexCharNode => node,

        // \begin and \end are the structure of an environment, not anything in it.
        { Kind: TexKinds.Command, Role: not (TexRole.Begin or TexRole.End) } when Meaning(node) is { } meaning
            => new TexCommandNode(node, meaning),

        { Kind: TexKinds.Environment } when Arrangement(node) is { } arrangement => new TexGridNode(node, arrangement),

        { Kind: Kinds.Char } when Character(node) is var (symbol, character) && (symbol is not null || character != TexCharacter.Symbol)
            => new TexCharNode(node, symbol, character),

        _ => node,
    };

    /// <summary>What a command means, or null where it is no command LaTeX has.</summary>
    private static TexMeaning? Meaning(ContentNode command)
    {
        if (command.Part(Roles.Name)?.Text is not { Length: > 1 } written || written[0] != '\\') return null;

        // What these mean is read from what is written with them as much as from the name.
        switch (written)
        {
            case @"\textcolor" or @"\color":
                return Colour(command) is { } colour ? new TexColour(colour, Switch: written == @"\color") : new TexUnset();

            case @"\mspace" or @"\hspace" or @"\hspace*" or @"\kern" or @"\mkern":
                return Value(command.Part(TexRole.Argument)) is { } amount && TexVocabulary.Length(amount) is { } length
                    ? new TexSpace(length.Unit, length.Value)
                    : new TexUnset();

            case @"\tag" or @"\tag*":
                return new TexTag(command.Part(TexRole.Argument) is { } number ? Value(number) ?? "" : null, Starred: written == @"\tag*");

            // Which way [l], [c] or [r] says the numerator leans; centred where nothing says.
            case @"\cfrac":
                return new TexContinuedFraction(command.Part(TexRole.Option)?.Said(TexRole.Value)?.Trim() switch
                {
                    "l" => TexAlignment.Left,
                    "r" => TexAlignment.Right,
                    _ => TexAlignment.Center,
                });

            case @"\left" or @"\right":
                return new TexDelimiter(TexVocabulary.DelimiterWritten(command));
        }

        var name = written[1..];

        if (TexVocabulary.Command(name) is { } meaning) return meaning;

        // A delimiter at a size of its own, where its argument is one.
        if (TexVocabulary.Size(name) is { } size)
            return TexVocabulary.DelimiterWritten(command) is { } delimiter
                ? new TexSizedDelimiter(delimiter, size.MinHeight, size.Class)
                : new TexUnset();

        if (TexVocabulary.IsFace(name)) return new TexFace(name == "mbox" ? TexVocabulary.Text : name, TexVocabulary.IsWords(name));
        if (TexVocabulary.Symbol(name) is { } symbol) return new TexNamedSymbol(symbol, TexVocabulary.SetsLimitsBeside(name));
        if (TexVocabulary.Strut(name) is { } mu) return new TexSpace(TexUnit.Mu, mu);

        return TexVocabulary.Knows(name) ? new TexUnset() : null;
    }

    /// <summary>How an environment arranges what it holds, or null where nothing here arranges it or its preamble will not read.</summary>
    private static TexArrangement? Arrangement(ContentNode environment)
    {
        if (environment.Part(TexRole.Begin) is not { } begin) return null;

        var arrangement = TexVocabulary.Environment(TexParser.NameOf(begin));
        if (arrangement is not TexArrayArrangement) return arrangement;

        // An array's preamble is its columns. One naming none is every column centred; one naming something that is not a
        // column is not a table anything here can set.
        if (environment.Part(TexRole.Option) is not { Kind: TexKinds.Group } option || Value(option) is not { } preamble) return null;
        if (!preamble.Any(c => c is 'l' or 'c' or 'r')) return new TexArrayArrangement(null);

        return TexColumns.Read(preamble) is { } columns ? new TexArrayArrangement(columns) : null;
    }

    /// <summary>What a character is, where it is not set as the letter it is.</summary>
    private static (TexSymbol? Symbol, TexCharacter Character) Character(ContentNode character)
    {
        // A name, a brace or a separator is structure, and a prime marking what it follows is the script's to set.
        if (character.Role is Roles.Name or Roles.Open or Roles.Close or Roles.Separator or TexRole.Mark) return default;
        if (character.Text is not [var written]) return default;

        return written switch
        {
            '~' => (null, TexCharacter.Tie),
            '\'' => (null, TexCharacter.Prime),
            _ => (TexVocabulary.SymbolOf(written), TexCharacter.Symbol),
        };
    }

    /// <summary>
    /// The colour a colour command's argument names — a colour name a browser knows, or <c>#</c> and hex digits — or null. A
    /// model written as its option other than <c>HTML</c> says the name is numbers in that model, which is not read here, so
    /// the command is shown as written rather than guessed at.
    /// </summary>
    private static HexColor? Colour(ContentNode command)
    {
        if (Value(command.Part(TexRole.Argument))?.Trim() is not { } name) return null;

        if (command.Part(TexRole.Option) is { } model)
        {
            if (Value(model)?.Trim().Equals("HTML", StringComparison.OrdinalIgnoreCase) is not true) return null;

            name = "#" + name;
        }

        return TexColours.Read(name);
    }

    /// <summary>
    /// What an argument holding a value says (<see cref="ReadValues"/>) — or, for one written without braces, the one piece it
    /// is. Null where it holds no value.
    /// </summary>
    private static string? Value(ContentNode? argument) =>
        argument is null ? null : argument.Said(TexRole.Value) ?? (argument.IsLeaf ? argument.Text : null);
}
