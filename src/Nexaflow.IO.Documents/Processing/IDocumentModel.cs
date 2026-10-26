using System.Security.Cryptography;
using System.Text;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// The model a whole-document job is done with — held for the whole job, so every part of a document is read by the same
/// model and what it worked out is kept under one name. What it reads at once is what every pass is sized by
/// (<see cref="DocumentBudget"/>), which is what lets a small model work through a book a piece at a time.
/// </summary>
public interface IDocumentModel
{
    /// <summary>Which model this is, as its notes are kept under: what another model worked out is not this one's.</summary>
    string Key { get; }

    /// <summary>How many tokens it reads at once, what it is asked and what it answers together.</summary>
    int ContextTokens { get; }

    /// <summary>About how many characters a token comes to.</summary>
    int CharsPerToken { get; }

    /// <summary>
    /// What the model answers <paramref name="asked"/> with, told <paramref name="instructions"/> first, and how its answer ended —
    /// stopped once it runs past <paramref name="longest"/> characters, ending <see cref="DocumentReplyEnd.LengthLimit"/>. A model
    /// can say the same thing over and over in ways nothing catches, and an answer longer than its question could need is that.
    /// </summary>
    Task<DocumentReply> AnswerAsync(string instructions, string asked, int longest, CancellationToken ct);

    /// <summary>
    /// How many answers it writes at once, every one of them in the one <see cref="ContextTokens"/> it shares between them — a model
    /// run on this machine; null where each answer has the whole of it to itself.
    /// </summary>
    int? RepliesAtOnce => null;

    /// <summary>
    /// Whether the answers it writes at once share <see cref="ContextTokens"/> between them; where they do not, each has as much to
    /// itself. Asked only where <see cref="RepliesAtOnce"/> says how many it writes.
    /// </summary>
    bool SharesContext => true;
}

/// <summary>Asking a document model where the question sets no length of its own for the answer.</summary>
public static class DocumentModelAnswers
{
    /// <summary>
    /// What <paramref name="model"/> answers <paramref name="asked"/> with, held to what is left for an answer of what it is handed
    /// at once (<see cref="DocumentBudget.Left"/>).
    /// </summary>
    public static Task<DocumentReply> AnswerAsync(this IDocumentModel model, string instructions, string asked, CancellationToken ct) =>
        model.AnswerAsync(instructions, asked, DocumentBudget.Of(model).Left(instructions, asked), ct);
}

/// <summary>How a model's answer ended.</summary>
public enum DocumentReplyEnd
{
    Finished,

    /// <summary>It reached the most it may write in one answer.</summary>
    LengthLimit,

    /// <summary>What it was asked and what it wrote filled all it can read.</summary>
    ContextFull,

    /// <summary>It was saying the same thing over and over, and was stopped.</summary>
    Circling,

    /// <summary>It could not be asked: the model is gone, or refused.</summary>
    Failed,
}

/// <summary>What a model answered, and how its answer ended.</summary>
/// <param name="Failure">Why, for an answer that <see cref="DocumentReplyEnd.Failed"/>, where the model said.</param>
public sealed record DocumentReply(string Text, DocumentReplyEnd Ended, string? Failure = null)
{
    /// <summary>Whether it ran out of room before it finished — what it was asked was too much at once.</summary>
    public bool RanOut => Ended is DocumentReplyEnd.LengthLimit or DocumentReplyEnd.ContextFull;
}

/// <summary>What a whole-document job is doing.</summary>
public enum DocumentStep
{
    /// <summary>Saying what the document is, before any part of it is worked on.</summary>
    Gist,

    /// <summary>Finding the names and terms a translation has to give the same way every time, and how it gives them.</summary>
    Glossary,

    /// <summary>Summarising it, a section at a time.</summary>
    Summary,

    /// <summary>Translating it, a passage at a time.</summary>
    Translation,

    /// <summary>Answering a question about it: finding the parts that bear on it, then reading them.</summary>
    Question,
}

/// <summary>How far a whole-document job has got: what it is doing, and how much of that is done — said by whatever shows it.</summary>
public readonly record struct DocumentProgress(DocumentStep Step, int Done, int Of);

/// <summary>A whole-document job that cannot finish, saying where and why.</summary>
public sealed class DocumentWorkException(string message) : Exception(message)
{
    /// <summary>A job stopped by a model that could not be asked <paramref name="what"/>, saying why where its reply did.</summary>
    public static DocumentWorkException NotAsked(string what, DocumentReply reply) =>
        new(reply.Failure is { Length: > 0 } why ? $"The model could not be asked {what}: {why}" : $"The model could not be asked {what}.");
}

/// <summary>The keys notes are kept under: a short hash of everything a note was worked out from.</summary>
public static class DocumentKeys
{
    /// <summary>A key standing for <paramref name="parts"/>, in order: any change to any of them is another key.</summary>
    public static string Of(params string[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u0001', parts)));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
