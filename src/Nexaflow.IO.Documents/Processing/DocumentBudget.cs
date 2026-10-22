namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// How much a model can be handed at once, in characters: what it reads, less a tenth for what a guess at tokens from
/// characters gets wrong. Every pass asks for a share of it, so a book is read in as many pieces as the model needs and no
/// more — one piece for a model that reads a great deal, many for one that reads a few pages.
/// </summary>
public readonly record struct DocumentBudget(int Characters)
{
    /// <summary>The least any model is taken to read: a page, and the instructions round it.</summary>
    private const int Least = 4096;

    /// <summary>What <paramref name="model"/> can be handed at once.</summary>
    public static DocumentBudget Of(IDocumentModel model) =>
        new(Math.Max(Least, (int)Math.Min(int.MaxValue, (long)model.ContextTokens * model.CharsPerToken * 9 / 10)));

    /// <summary><paramref name="part"/> of it, as characters.</summary>
    public int Share(double part) => (int)(Characters * part);
}
