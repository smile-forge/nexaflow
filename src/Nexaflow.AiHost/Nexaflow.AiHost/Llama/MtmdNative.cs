using System.Runtime.InteropServices;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// The part of llama.cpp's multimodal library the host calls — <c>mtmd.h</c> and <c>mtmd-helper.h</c> — mirrored, like
/// <see cref="LlamaNative"/>, from the headers of the build in <c>llama-cpp.build</c>.
/// </summary>
internal static unsafe partial class MtmdNative
{
    private const string Mtmd = "mtmd";

    [StructLayout(LayoutKind.Sequential)]
    public struct ContextParams
    {
        public byte UseGpu;
        public IntPtr Device;
        public byte PrintTimings;
        public int Threads;
        public IntPtr ImageMarker;
        public IntPtr MediaMarker;
        public LlamaNative.FlashAttention FlashAttention;
        public byte Warmup;
        public int ImageMinTokens;
        public int ImageMaxTokens;
        public IntPtr EvalCallback;
        public IntPtr EvalCallbackUserData;
        public int BatchMaxTokens;
        public IntPtr ProgressCallback;
        public IntPtr ProgressCallbackUserData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HelperOptions
    {
        public float FpsTarget;
        public IntPtr FfmpegFolder;
        public long TimestampIntervalMs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapWrapper
    {
        public IntPtr Bitmap;
        public IntPtr Video;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InputText
    {
        public byte* Text;
        public nuint Length;
        public byte AddSpecial;
        public byte ParseSpecial;
    }

    public enum ChunkType
    {
        Text = 0,
        Image = 1,
        Audio = 2,
    }

    [LibraryImport(Mtmd, EntryPoint = "mtmd_helper_log_set")]
    public static partial void LogSet(delegate* unmanaged[Cdecl]<LlamaNative.LogLevel, byte*, void*, void> callback, void* userData);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_context_params_default")]
    public static partial ContextParams ContextDefaults();

    [LibraryImport(Mtmd, EntryPoint = "mtmd_init_from_file", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr Init(string projectorPath, IntPtr model, ContextParams parameters);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_free")]
    public static partial void Free(IntPtr context);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_default_marker")]
    public static partial byte* DefaultMarker();

    [LibraryImport(Mtmd, EntryPoint = "mtmd_support_vision")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SupportsVision(IntPtr context);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_support_audio")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SupportsAudio(IntPtr context);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_helper_init_opt_default")]
    public static partial HelperOptions HelperDefaults();

    [LibraryImport(Mtmd, EntryPoint = "mtmd_helper_bitmap_init_from_buf")]
    public static partial BitmapWrapper BitmapFromBuffer(IntPtr context, byte* buffer, nuint length,
                                                         [MarshalAs(UnmanagedType.U1)] bool placeholder, HelperOptions options);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_bitmap_free")]
    public static partial void BitmapFree(IntPtr bitmap);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_input_chunks_init")]
    public static partial IntPtr ChunksInit();

    [LibraryImport(Mtmd, EntryPoint = "mtmd_input_chunks_free")]
    public static partial void ChunksFree(IntPtr chunks);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_input_chunks_size")]
    public static partial nuint ChunksSize(IntPtr chunks);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_input_chunks_get")]
    public static partial IntPtr ChunksGet(IntPtr chunks, nuint index);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_input_chunk_get_type")]
    public static partial ChunkType ChunkGetType(IntPtr chunk);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_input_chunk_get_tokens_text")]
    public static partial int* ChunkTextTokens(IntPtr chunk, nuint* count);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_input_chunk_get_n_tokens")]
    public static partial nuint ChunkTokens(IntPtr chunk);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_input_chunk_get_n_pos")]
    public static partial int ChunkPositions(IntPtr chunk);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_tokenize")]
    public static partial int Tokenize(IntPtr context, IntPtr output, InputText* text, IntPtr* bitmaps, nuint bitmapCount);

    [LibraryImport(Mtmd, EntryPoint = "mtmd_helper_eval_chunk_single")]
    public static partial int EvalChunk(IntPtr context, IntPtr llamaContext, IntPtr chunk, int pastPositions, int sequence,
                                        int batch, [MarshalAs(UnmanagedType.U1)] bool logitsLast, int* newPastPositions);
}
