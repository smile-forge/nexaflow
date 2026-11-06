using System.Runtime.InteropServices;
using System.Text;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// A GGUF model llama.cpp has loaded — on one GPU, every layer, or on the CPU — and its vocabulary:
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
        TrainedContext = LlamaNative.ModelTrainedContext(model);
        byte* template = LlamaNative.ModelChatTemplate(model, null);
        ChatTemplate = template is null ? null : Marshal.PtrToStringUTF8((IntPtr)template);
        BosText = TokenText(LlamaNative.VocabBos(Vocab));
        EosText = TokenText(LlamaNative.VocabEos(Vocab));
        HoldsPartialState = LlamaNative.ModelIsRecurrent(model) || LlamaNative.ModelIsHybrid(model) || LlamaNative.ModelSlidingWindow(model) > 0;
    }

    public IntPtr Handle => _model != IntPtr.Zero ? _model : throw new ObjectDisposedException(nameof(LlamaModel));

    public IntPtr Vocab { get; }

    public int VocabSize { get; }

    /// <summary>The positions the model was trained to read: past them its position encoding means nothing it learned.</summary>
    public int TrainedContext { get; }

    /// <summary>The chat template the GGUF carries, or null when it carries none.</summary>
    public string? ChatTemplate { get; }

    /// <summary>The text of the tokens a template writes as <c>bos_token</c> and <c>eos_token</c>.</summary>
    public string BosText { get; }

    public string EosText { get; }

    /// <summary>
    /// Whether what a sequence holds is more than its cells, position by position: a recurrent layer's state, which every token
    /// read changes, or a sliding window's cells, of which only the last window are kept. Such a sequence is cut back only to where
    /// that state was saved (<see cref="LlamaContext.SavePartial"/>); any other is cut back anywhere.
    /// </summary>
    public bool HoldsPartialState { get; }

    /// <summary>The tensors of a mixture-of-experts model's routed experts, as llama.cpp names them in every architecture it reads —
    /// its own pattern for keeping them on the CPU: each layer's banks of up, gate and down projections, one slice an expert.</summary>
    internal const string RoutedExperts = @"\.ffn_(up|down|gate|gate_up)_(ch|)exps";

    /// <param name="device">The GPU it runs on, or null for the CPU.</param>
    /// <param name="gpuLayers">How many of its layers run on <paramref name="device"/>, the rest on the CPU; null for all of them.</param>
    /// <param name="expertsInHost">Keep a mixture-of-experts model's routed experts (<see cref="RoutedExperts"/>) in this machine's
    /// memory, every layer's, and the rest of it on <paramref name="device"/>.</param>
    /// <exception cref="HostRequestException">The file would not load.</exception>
    public static LlamaModel Load(string path, IntPtr? device, int? gpuLayers = null, bool expertsInHost = false)
    {
        var parameters = LlamaNative.ModelDefaults();
        // One GPU, the one whose budget it was admitted against, as for every model the host runs.
        var devices = stackalloc IntPtr[] { device ?? IntPtr.Zero, IntPtr.Zero };
        parameters.Devices = (IntPtr)devices;
        parameters.GpuLayers = device is null ? 0 : gpuLayers ?? -1;
        parameters.Split = LlamaNative.SplitMode.None;
        parameters.MainGpu = 0;
        bool partlyOnCpu = gpuLayers is not null || expertsInHost;
        // A model wholly on a GPU is read into it: mapped, its file would stay in this process's working set long after its
        // weights are on the device, every gigabyte counted as the host's memory. Where any of it is on the CPU, the mapping is
        // those weights.
        parameters.LoadMode = device is not null && !partlyOnCpu ? LlamaNative.LoadMode.None : LlamaNative.LoadMode.Auto;

        var overrides = stackalloc LlamaNative.TensorBufferTypeOverride[2];
        IntPtr pattern = IntPtr.Zero;
        try
        {
            if (device is not null && expertsInHost)
            {
                IntPtr cpu = LlamaNative.DeviceByType(LlamaNative.DeviceKind.Cpu);
                if (cpu == IntPtr.Zero) throw new HostRequestException(HostErrorKind.Runtime, "llama.cpp offers no CPU device to keep a model's experts on.");
                pattern = Marshal.StringToCoTaskMemUTF8(RoutedExperts);
                overrides[0] = new LlamaNative.TensorBufferTypeOverride { Pattern = pattern, BufferType = LlamaNative.DeviceBufferType(cpu) };
                overrides[1] = default;
                parameters.TensorBufferTypeOverrides = (IntPtr)overrides;
            }
            IntPtr model = LlamaNative.ModelLoad(path, parameters);
            return model == IntPtr.Zero
                ? throw new HostRequestException(HostErrorKind.Runtime, $"llama.cpp could not load {path}; see the host's log.")
                : new LlamaModel(model);
        }
        finally
        {
            if (pattern != IntPtr.Zero) Marshal.FreeCoTaskMem(pattern);
        }
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

    /// <summary>The bytes a token stands for in the reply — none for a control token, unless <paramref name="control"/>.</summary>
    public byte[] Piece(int token, bool control = false)
    {
        Span<byte> buffer = stackalloc byte[128];
        fixed (byte* bytes = buffer)
        {
            int count = LlamaNative.TokenToPiece(Vocab, token, bytes, buffer.Length, 0, special: control);
            if (count >= 0) return buffer[..count].ToArray();
            var larger = new byte[-count];
            fixed (byte* more = larger)
                count = LlamaNative.TokenToPiece(Vocab, token, more, larger.Length, 0, special: control);
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
