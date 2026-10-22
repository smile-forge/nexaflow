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
public readonly record struct DocumentBudget(int Characters)
{
    /// <summary>The least any model is taken to read: a page, and the instructions round it.</summary>
    private const int Least = 4096;

    /// <summary>The most a question is sized to, in tokens, however much more the model reads.</summary>
    public const int MostTokens = 16_384;

    /// <summary>What <paramref name="model"/> is handed at once.</summary>
    public static DocumentBudget Of(IDocumentModel model) =>
        new(Math.Max(Least, (int)((long)Math.Min(model.ContextTokens, MostTokens) * model.CharsPerToken * 9 / 10)));

    /// <summary><paramref name="part"/> of it, as characters.</summary>
    public int Share(double part) => (int)(Characters * part);

    /// <summary>
    /// What is left of it for an answer to <paramref name="asked"/>, told <paramref name="instructions"/> — what a model reads holds
    /// its own answer too — and never less than a quarter of it, so a question filled to the brim can still be answered.
    /// </summary>
    public int Left(string instructions, string asked) => Math.Max(Characters / 4, Characters - instructions.Length - asked.Length);
}
