using System.Text;
using Nexaflow.AiHost.Llama;

namespace Nexaflow.AiHost.Generation;

/// <summary>A reply's text, token by token: each token's bytes decoded as UTF-8 that may run across tokens, so a
/// character split between two arrives whole with the second. Control tokens are left out, unless the model writes
/// with them (<paramref name="controlTokens"/>).</summary>
internal sealed class ReplyText(LlamaModel model, bool controlTokens = false)
{
    private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();

    public string Decode(int token)
    {
        var bytes = model.Piece(token, controlTokens);
        if (bytes.Length == 0) return "";
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        int count = _utf8.GetChars(bytes, 0, bytes.Length, chars, 0, flush: false);
        return new string(chars, 0, count);
    }
}
