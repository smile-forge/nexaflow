using System.Runtime.InteropServices;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// The part of llama.cpp's C API the host calls — <c>llama.h</c>, and from <c>ggml-backend.h</c> the backends' registry
/// (<c>ggml.dll</c>: loading, the devices found) and a device's own answers (<c>ggml-base.dll</c>) — at the build named
/// in <c>llama-cpp.build</c>. The structs mirror that build's headers field for field: moving to
/// another build means comparing its headers with these first (the model tests load a real model through them).
/// </summary>
internal static unsafe partial class LlamaNative
{
    private const string Llama = "llama";
    private const string Ggml = "ggml";
    private const string GgmlBase = "ggml-base";

    [StructLayout(LayoutKind.Sequential)]
    public struct ModelParams
    {
        public IntPtr Devices;
        public IntPtr TensorBufferTypeOverrides;
        public int GpuLayers;
        public SplitMode Split;
        public LoadMode LoadMode;
        public int LazyMode;
        public int MainGpu;
        public float* TensorSplit;
        public IntPtr ProgressCallback;
        public IntPtr ProgressCallbackUserData;
        public IntPtr KvOverrides;
        public byte VocabOnly;
        public byte CheckTensors;
        public byte UseExtraBufferTypes;
        public byte NoHost;
        public byte NoAlloc;
        public byte LoadMtp;
    }

    public enum SplitMode
    {
        None = 0,
        Layer = 1,
        Row = 2,
        Tensor = 3,
    }

    /// <summary>How llama.cpp reads a model's file (llama_load_mode).</summary>
    public enum LoadMode
    {
        /// <summary>Mapped, where every device the model runs on can read a mapping.</summary>
        Auto = -1,
        /// <summary>Read through the page cache into each tensor's buffer; no mapping outlives the load.</summary>
        None = 0,
        Mmap = 1,
        Mlock = 2,
        MmapMlock = 3,
        /// <summary>Read past the page cache, where the drive allows it.</summary>
        DirectIo = 4,
    }

    public enum FlashAttention
    {
        Auto = -1,
        Disabled = 0,
        Enabled = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContextParams
    {
        public uint Context;
        public uint Batch;
        public uint MicroBatch;
        public uint MaxSequences;
        public uint RecurrentSnapshots;
        public uint MaxOutputs;
        public uint MaxOutputsPerSequence;
        public int Threads;
        public int BatchThreads;
        public int ContextType;
        public int RopeScalingType;
        public int PoolingType;
        public int AttentionType;
        public FlashAttention FlashAttention;
        public float RopeFrequencyBase;
        public float RopeFrequencyScale;
        public float YarnExtensionFactor;
        public float YarnAttentionFactor;
        public float YarnBetaFast;
        public float YarnBetaSlow;
        public uint YarnOriginalContext;
        public float DefragThreshold;
        public IntPtr EvalCallback;
        public IntPtr EvalCallbackUserData;
        public int KeyType;
        public int ValueType;
        public nuint MoeCacheSize;
        public IntPtr AbortCallback;
        public IntPtr AbortCallbackData;
        public byte Embeddings;
        public byte OffloadKqv;
        public byte NoPerf;
        public byte OpOffload;
        public byte SwaFull;
        public byte KvUnified;
        public IntPtr Samplers;
        public nuint SamplerCount;
        public IntPtr ContextOther;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Batch
    {
        public int TokenCount;
        public int* Token;
        public float* Embedding;
        public int* Position;
        public int* SequenceIdCount;
        public int** SequenceId;
        public sbyte* Logits;
    }

    public enum LogLevel
    {
        None = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Continue = 5,
    }

    [LibraryImport(Ggml, EntryPoint = "ggml_backend_load_all_from_path", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void LoadBackendsFrom(string directory);

    [LibraryImport(Ggml, EntryPoint = "ggml_backend_load", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr LoadBackend(string path);

    [LibraryImport(Ggml, EntryPoint = "ggml_backend_dev_count")]
    public static partial nuint DeviceCount();

    [LibraryImport(Ggml, EntryPoint = "ggml_backend_dev_get")]
    public static partial IntPtr Device(nuint index);

    [LibraryImport(GgmlBase, EntryPoint = "ggml_backend_dev_get_props")]
    public static partial void DeviceProperties(IntPtr device, DeviceProps* properties);

    [LibraryImport(GgmlBase, EntryPoint = "ggml_backend_dev_memory")]
    public static partial void DeviceMemory(IntPtr device, nuint* free, nuint* total);

    /// <summary><c>ggml_backend_dev_props</c>: what a device is, and its memory as it stands.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceProps
    {
        public IntPtr Name;
        public IntPtr Description;
        public nuint MemoryFree;
        public nuint MemoryTotal;
        public DeviceKind Kind;
        /// <summary>The PCI bus id, lower case <c>domain:bus:device.function</c>; null when unknown.</summary>
        public IntPtr BusId;
        public byte Async;
        public byte HostBuffer;
        public byte BufferFromHostPointer;
        public byte Events;
        public byte MmapSupport;
    }

    /// <summary><c>ggml_backend_dev_type</c>.</summary>
    public enum DeviceKind
    {
        Cpu = 0,
        Gpu = 1,
        IntegratedGpu = 2,
        Accelerator = 3,
        Meta = 4,
    }

    [LibraryImport(Llama, EntryPoint = "llama_backend_init")]
    public static partial void BackendInit();

    [LibraryImport(Llama, EntryPoint = "llama_backend_free")]
    public static partial void BackendFree();

    [LibraryImport(Llama, EntryPoint = "llama_log_set")]
    public static partial void LogSet(delegate* unmanaged[Cdecl]<LogLevel, byte*, void*, void> callback, void* userData);

    [LibraryImport(Llama, EntryPoint = "llama_model_default_params")]
    public static partial ModelParams ModelDefaults();

    [LibraryImport(Llama, EntryPoint = "llama_context_default_params")]
    public static partial ContextParams ContextDefaults();

    [LibraryImport(Llama, EntryPoint = "llama_model_load_from_file", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr ModelLoad(string path, ModelParams parameters);

    [LibraryImport(Llama, EntryPoint = "llama_model_free")]
    public static partial void ModelFree(IntPtr model);

    [LibraryImport(Llama, EntryPoint = "llama_model_chat_template")]
    public static partial byte* ModelChatTemplate(IntPtr model, byte* name);

    [LibraryImport(Llama, EntryPoint = "llama_model_get_vocab")]
    public static partial IntPtr ModelVocab(IntPtr model);

    [LibraryImport(Llama, EntryPoint = "llama_model_n_ctx_train")]
    public static partial int ModelTrainedContext(IntPtr model);

    [LibraryImport(Llama, EntryPoint = "llama_init_from_model")]
    public static partial IntPtr ContextInit(IntPtr model, ContextParams parameters);

    [LibraryImport(Llama, EntryPoint = "llama_free")]
    public static partial void ContextFree(IntPtr context);

    [LibraryImport(Llama, EntryPoint = "llama_n_ctx")]
    public static partial uint ContextSize(IntPtr context);

    [LibraryImport(Llama, EntryPoint = "llama_get_memory")]
    public static partial IntPtr ContextMemory(IntPtr context);

    [LibraryImport(Llama, EntryPoint = "llama_memory_seq_rm")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool MemoryRemove(IntPtr memory, int sequence, int from, int to);

    [LibraryImport(Llama, EntryPoint = "llama_memory_seq_pos_max")]
    public static partial int MemoryLastPosition(IntPtr memory, int sequence);

    [LibraryImport(Llama, EntryPoint = "llama_vocab_n_tokens")]
    public static partial int VocabSize(IntPtr vocab);

    [LibraryImport(Llama, EntryPoint = "llama_vocab_bos")]
    public static partial int VocabBos(IntPtr vocab);

    [LibraryImport(Llama, EntryPoint = "llama_vocab_eos")]
    public static partial int VocabEos(IntPtr vocab);

    [LibraryImport(Llama, EntryPoint = "llama_vocab_is_eog")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool VocabIsEndOfGeneration(IntPtr vocab, int token);

    [LibraryImport(Llama, EntryPoint = "llama_tokenize")]
    public static partial int Tokenize(IntPtr vocab, byte* text, int length, int* tokens, int maxTokens,
                                       [MarshalAs(UnmanagedType.U1)] bool addSpecial, [MarshalAs(UnmanagedType.U1)] bool parseSpecial);

    [LibraryImport(Llama, EntryPoint = "llama_token_to_piece")]
    public static partial int TokenToPiece(IntPtr vocab, int token, byte* buffer, int length, int leftStrip,
                                           [MarshalAs(UnmanagedType.U1)] bool special);

    [LibraryImport(Llama, EntryPoint = "llama_batch_init")]
    public static partial Batch BatchInit(int tokens, int embedding, int maxSequences);

    [LibraryImport(Llama, EntryPoint = "llama_batch_free")]
    public static partial void BatchFree(Batch batch);

    [LibraryImport(Llama, EntryPoint = "llama_decode")]
    public static partial int Decode(IntPtr context, Batch batch);

    [LibraryImport(Llama, EntryPoint = "llama_sampler_init_greedy")]
    public static partial IntPtr SamplerGreedy();

    [LibraryImport(Llama, EntryPoint = "llama_sampler_sample")]
    public static partial int SamplerSample(IntPtr sampler, IntPtr context, int index);

    /// <summary>The scores the last read left for its <paramref name="index"/>-th output, -1 the last: one per token of
    /// the vocabulary, written in place to steer the sample that follows.</summary>
    [LibraryImport(Llama, EntryPoint = "llama_get_logits_ith")]
    public static partial float* Logits(IntPtr context, int index);

    /// <summary>llama.cpp's <c>llama_sampler_chain_params</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SamplerChainParams
    {
        public byte NoPerf;
    }

    [LibraryImport(Llama, EntryPoint = "llama_sampler_chain_default_params")]
    public static partial SamplerChainParams SamplerChainDefaults();

    [LibraryImport(Llama, EntryPoint = "llama_sampler_chain_init")]
    public static partial IntPtr SamplerChain(SamplerChainParams parameters);

    /// <summary>Adds <paramref name="sampler"/> to the end of <paramref name="chain"/>, which owns it from then on.</summary>
    [LibraryImport(Llama, EntryPoint = "llama_sampler_chain_add")]
    public static partial void SamplerChainAdd(IntPtr chain, IntPtr sampler);

    [LibraryImport(Llama, EntryPoint = "llama_sampler_init_top_k")]
    public static partial IntPtr SamplerTopK(int k);

    [LibraryImport(Llama, EntryPoint = "llama_sampler_init_top_p")]
    public static partial IntPtr SamplerTopP(float p, nuint minKeep);

    [LibraryImport(Llama, EntryPoint = "llama_sampler_init_temp")]
    public static partial IntPtr SamplerTemperature(float temperature);

    [LibraryImport(Llama, EntryPoint = "llama_sampler_init_dist")]
    public static partial IntPtr SamplerDistribution(uint seed);

    [LibraryImport(Llama, EntryPoint = "llama_sampler_init_penalties")]
    public static partial IntPtr SamplerPenalties(int lastN, float repeat, float frequency, float presence);

    /// <summary>The seed llama.cpp takes to mean "a different one each time".</summary>
    public const uint DefaultSeed = 0xFFFFFFFF;

    [LibraryImport(Llama, EntryPoint = "llama_sampler_free")]
    public static partial void SamplerFree(IntPtr sampler);
}
