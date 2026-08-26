using System;

namespace XamlMath.Exceptions;

public sealed class TexParseException : TexException
{
    internal TexParseException(string message)
        : base(message)
    {
    }

    internal TexParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal TexParseException(string message, SourceSpan? at)
        : base(message)
    {
        At = at;
    }

    internal TexParseException(string message, SourceSpan? at, Exception innerException)
        : base(message, innerException)
    {
        At = at;
    }

    /// <summary>
    /// Where in the input the parser gave up, when it could say — so a caller can show the reader which
    /// part of what they wrote is the trouble rather than colouring the whole of it.
    /// </summary>
    /// <remarks>
    /// Named <c>At</c> rather than <c>Source</c> because <see cref="Exception"/> already has a
    /// <see cref="Exception.Source"/>, and it means something else entirely.
    /// </remarks>
    public SourceSpan? At { get; }
}
