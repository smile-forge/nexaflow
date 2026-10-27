namespace Nexaflow.Markdown.Typesetting.Tex.Exceptions;

public sealed class TexNotSupportedException : TexException
{
    public TexNotSupportedException(string message) : base(message)
    {
    }
}
