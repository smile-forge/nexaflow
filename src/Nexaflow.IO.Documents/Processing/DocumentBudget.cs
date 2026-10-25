namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// How much a model is handed at once, in characters: what it reads, up to a working size (<see cref="MostTokens"/>), less a
/// tenth for what a guess at tokens from characters gets wrong. Every pass asks for a share of it, so a book is read in as many
/// pieces as the model needs — many for one that reads a few pages.
///
/// <para>
/// A model that reads a great deal is still handed a working size, not all it could read: a model writes well only so much
/// at once, an answer the length of a book is minutes the reader sees nothing of, and a question that fails then costs only its
/// own piece. Its notes are kept a piece at a time, so asking again goes on from the piece that failed.
/// </para>
/// </summary>
public readonly record struct DocumentBudget(int Characters, int AtOnce)
{
    /// <summary>The least any model is taken to read: a page, and the instructions round it.</summary>
    private const int Least = 4096;

    /// <summary>The most a question is sized to, in tokens, however much more the model reads.</summary>
    public const int MostTokens = 16_384;

    /// <summary>How many questions are asked at once of a model each of whose answers has its context to itself.</summary>
    public const int Separately = 4;

    /// <summary>The share of a context its answers share that one question may fill: the rest is left for the others.</summary>
    private const double Filled = 0.75;

    /// <summary>
    /// What <paramref name="model"/> is handed at once, and how many such questions it is asked at once. Where its answers share
    /// one context, a question is never larger than fits in most of it, and as many are asked at once as it writes answers, less
    /// one left for anyone else: the model fits them in its context as their real sizes allow, the rest waiting for room.
    /// </summary>
    public static DocumentBudget Of(IDocumentModel model)
    {
        if (model.RepliesAtOnce is not { } replies) return new(CharactersOf(model, Math.Min(model.ContextTokens, MostTokens)), Separately);

        var each = Math.Max(1, Math.Min((int)(model.ContextTokens * Filled), MostTokens));
        return new(CharactersOf(model, each), Math.Max(1, replies - 1));
    }

    /// <summary>What <paramref name="tokens"/> of <paramref name="model"/>'s come to in characters, less a tenth for what is not words.</summary>
    private static int CharactersOf(IDocumentModel model, int tokens) => Math.Max(Least, (int)((long)tokens * model.CharsPerToken * 9 / 10));

    /// <summary><paramref name="part"/> of it, as characters.</summary>
    public int Share(double part) => (int)(Characters * part);

    /// <summary>
    /// What is left of it for an answer to <paramref name="asked"/>, told <paramref name="instructions"/> — what a model reads holds
    /// its own answer too — and never less than a quarter of it, so a question filled to the brim can still be answered.
    /// </summary>
    public int Left(string instructions, string asked) => Math.Max(Characters / 4, Characters - instructions.Length - asked.Length);
}
