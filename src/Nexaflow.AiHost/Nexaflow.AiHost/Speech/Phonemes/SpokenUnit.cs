namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>A piece of what is said, as the phonemiser reads it: a word, or a pause.</summary>
internal abstract record SpokenUnit;

/// <summary>
/// A word to say, as written. Its phonemes are looked up for <see cref="Text"/>; a word made of <see cref="Parts"/> — a
/// German number, one word however long — is said as its parts run together when it is not found whole; a word whose
/// <see cref="Phonemes"/> are already known — a spelled acronym — is said as they are.
/// </summary>
internal sealed record SpokenWord(string Text) : SpokenUnit
{
    public IReadOnlyList<string>? Parts { get; init; }
    public string? Phonemes { get; init; }
}

/// <summary>A pause the voice makes, as the punctuation it reads: <c>, . ; : ! ? — …</c>.</summary>
internal sealed record SpokenPause(string Mark) : SpokenUnit;
