namespace Nexaflow.Markdown.Typesetting.Tex.Exceptions;

public sealed class TypeFaceNotFoundException : TexException
{
    public TypeFaceNotFoundException(string message) : base(message)
    {
    }
}
