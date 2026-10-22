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

    /// <summary>What the model answers <paramref name="asked"/> with, told <paramref name="instructions"/> first, and how its answer ended.</summary>
    Task<DocumentReply> AnswerAsync(string instructions, string asked, CancellationToken ct);
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
public sealed record DocumentReply(string Text, DocumentReplyEnd Ended)
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
public sealed class DocumentWorkException(string message) : Exception(message);

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
