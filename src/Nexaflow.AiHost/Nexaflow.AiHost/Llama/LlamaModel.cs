using System.Runtime.InteropServices;
using System.Text;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// A GGUF model llama.cpp has loaded — on the GPU when its CUDA backend is loaded, every layer — and its vocabulary:
/// text to tokens and back, which tokens end a turn, and the chat template the file carries.
/// </summary>
internal sealed unsafe class LlamaModel : IDisposable
{
    private IntPtr _model;

    private LlamaModel(IntPtr model)
    {
        _model = model;
        Vocab = LlamaNative.ModelVocab(model);
        VocabSize = LlamaNative.VocabSize(Vocab);
        byte* template = LlamaNative.ModelChatTemplate(model, null);
        ChatTemplate = template is null ? null : Marshal.PtrToStringUTF8((IntPtr)template);
        BosText = TokenText(LlamaNative.VocabBos(Vocab));
        EosText = TokenText(LlamaNative.VocabEos(Vocab));
    }

    public IntPtr Handle => _model != IntPtr.Zero ? _model : throw new ObjectDisposedException(nameof(LlamaModel));

    public IntPtr Vocab { get; }

    public int VocabSize { get; }

    /// <summary>The chat template the GGUF carries, or null when it carries none.</summary>
    public string? ChatTemplate { get; }

    /// <summary>The text of the tokens a template writes as <c>bos_token</c> and <c>eos_token</c>.</summary>
    public string BosText { get; }

    public string EosText { get; }

    /// <exception cref="HostRequestException">The file would not load.</exception>
    public static LlamaModel Load(string path, bool onGpu)
    {
        var parameters = LlamaNative.ModelDefaults();
        parameters.GpuLayers = onGpu ? -1 : 0;
        // One GPU, the one the VRAM budget is drawn from, as for every model the host runs.
        parameters.Split = LlamaNative.SplitMode.None;
        parameters.MainGpu = 0;
        IntPtr model = LlamaNative.ModelLoad(path, parameters);
        return model == IntPtr.Zero
            ? throw new HostRequestException(HostErrorKind.Runtime, $"llama.cpp could not load {path}; see the host's log.")
            : new LlamaModel(model);
    }

    /// <summary>The tokens of <paramref name="text"/>, its special tokens read as such and nothing added before it — a
    /// template writes the start of the conversation itself.</summary>
    public int[] Tokenize(string text)
    {
        if (text.Length == 0) return [];
        var utf8 = Encoding.UTF8.GetBytes(text);
        var tokens = new int[utf8.Length + 8];
        fixed (byte* bytes = utf8)
        fixed (int* output = tokens)
        {
            int count = LlamaNative.Tokenize(Vocab, bytes, utf8.Length, output, tokens.Length, addSpecial: false, parseSpecial: true);
            if (count < 0)
            {
                tokens = new int[-count];
                fixed (int* larger = tokens)
                    count = LlamaNative.Tokenize(Vocab, bytes, utf8.Length, larger, tokens.Length, addSpecial: false, parseSpecial: true);
            }
            return tokens[..count];
        }
    }

    /// <summary>The one special token <paramref name="text"/> is, or null when the vocabulary has no such token.</summary>
    public int? SpecialToken(string text) => Tokenize(text) is [var token] && TokenText(token) == text ? token : null;

    /// <summary>The bytes a token stands for in the reply — none for a control token.</summary>
    public byte[] Piece(int token)
    {
        Span<byte> buffer = stackalloc byte[128];
        fixed (byte* bytes = buffer)
        {
            int count = LlamaNative.TokenToPiece(Vocab, token, bytes, buffer.Length, 0, special: false);
            if (count >= 0) return buffer[..count].ToArray();
            var larger = new byte[-count];
            fixed (byte* more = larger)
                count = LlamaNative.TokenToPiece(Vocab, token, more, larger.Length, 0, special: false);
            return larger[..count];
        }
    }

    /// <summary>A token's own text, special tokens included.</summary>
    public string TokenText(int token)
    {
        if (token < 0) return "";
        Span<byte> buffer = stackalloc byte[256];
        fixed (byte* bytes = buffer)
        {
            int count = LlamaNative.TokenToPiece(Vocab, token, bytes, buffer.Length, 0, special: true);
            return count > 0 ? Encoding.UTF8.GetString(buffer[..count]) : "";
        }
    }

    /// <summary>Whether the model's turn ends at <paramref name="token"/>.</summary>
    public bool EndsTurn(int token) => LlamaNative.VocabIsEndOfGeneration(Vocab, token);

    public void Dispose()
    {
        if (_model == IntPtr.Zero) return;
        LlamaNative.ModelFree(_model);
        _model = IntPtr.Zero;
    }
}
