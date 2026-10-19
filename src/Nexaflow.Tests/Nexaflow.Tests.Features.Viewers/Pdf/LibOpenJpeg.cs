using System.Runtime.InteropServices;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// libopenjp2, the C library <c>opj_decompress</c> is built on, called in process: the native side of
/// <see cref="JpxDecodeBench"/>. The library is loaded from the path the bench is given, so the baseline is
/// whichever OpenJPEG build the run names, and it decodes from memory, so a timing is the decode and nothing
/// around it.
/// </summary>
internal sealed class LibOpenJpeg : IDisposable
{
    private const int CodecJ2K = 0;
    private const int CodecJp2 = 2;
    private const int IgnorePaletteFlag = 0x0001;   // OPJ_DPARAMETERS_IGNORE_PCLR_CMAP_CDEF_FLAG
    private const int StreamChunk = 1 << 20;         // OPJ_J2K_STREAM_CHUNK_SIZE

    private readonly IntPtr _library;
    private readonly IntPtr _parameters;
    private readonly IntPtr _parametersIgnoringPalette;

    private readonly CreateDecompress _createDecompress;
    private readonly SetupDecoder _setupDecoder;
    private readonly SetThreads _setThreads;
    private readonly SetMessageHandler _setErrorHandler;
    private readonly StreamCreate _streamCreate;
    private readonly StreamSetFunction _streamSetRead;
    private readonly StreamSetFunction _streamSetSkip;
    private readonly StreamSetFunction _streamSetSeek;
    private readonly StreamSetUserData _streamSetUserData;
    private readonly StreamSetUserDataLength _streamSetUserDataLength;
    private readonly ReadHeader _readHeader;
    private readonly DecodeImage _decode;
    private readonly EndDecompress _endDecompress;
    private readonly Destroy _imageDestroy;
    private readonly Destroy _streamDestroy;
    private readonly Destroy _destroyCodec;

    // Held for as long as the library may call them.
    private static readonly StreamRead Reader = ReadSource;
    private static readonly StreamSkip Skipper = SkipSource;
    private static readonly StreamSeek Seeker = SeekSource;
    private static readonly MessageHandler ErrorHeard = HeardError;
    private static readonly IntPtr ReaderPointer = Marshal.GetFunctionPointerForDelegate(Reader);
    private static readonly IntPtr SkipperPointer = Marshal.GetFunctionPointerForDelegate(Skipper);
    private static readonly IntPtr SeekerPointer = Marshal.GetFunctionPointerForDelegate(Seeker);
    private static readonly IntPtr ErrorHeardPointer = Marshal.GetFunctionPointerForDelegate(ErrorHeard);

    public LibOpenJpeg(string libraryPath)
    {
        _library = NativeLibrary.Load(libraryPath);

        _createDecompress = Export<CreateDecompress>("opj_create_decompress");
        _setupDecoder = Export<SetupDecoder>("opj_setup_decoder");
        _setThreads = Export<SetThreads>("opj_codec_set_threads");
        _setErrorHandler = Export<SetMessageHandler>("opj_set_error_handler");
        _streamCreate = Export<StreamCreate>("opj_stream_create");
        _streamSetRead = Export<StreamSetFunction>("opj_stream_set_read_function");
        _streamSetSkip = Export<StreamSetFunction>("opj_stream_set_skip_function");
        _streamSetSeek = Export<StreamSetFunction>("opj_stream_set_seek_function");
        _streamSetUserData = Export<StreamSetUserData>("opj_stream_set_user_data");
        _streamSetUserDataLength = Export<StreamSetUserDataLength>("opj_stream_set_user_data_length");
        _readHeader = Export<ReadHeader>("opj_read_header");
        _decode = Export<DecodeImage>("opj_decode");
        _endDecompress = Export<EndDecompress>("opj_end_decompress");
        _imageDestroy = Export<Destroy>("opj_image_destroy");
        _streamDestroy = Export<Destroy>("opj_stream_destroy");
        _destroyCodec = Export<Destroy>("opj_destroy_codec");

        Version = Marshal.PtrToStringAnsi(Export<GetVersion>("opj_version")()) ?? "";

        // The parameters never change between decodes, so they are laid out once, natively, as the library
        // fills them — and the palette flag is the only field this bench sets.
        var setDefaults = Export<SetDefaultDecoderParameters>("opj_set_default_decoder_parameters");
        var size = Marshal.SizeOf<DecoderParameters>();
        var flags = (int)Marshal.OffsetOf<DecoderParameters>(nameof(DecoderParameters.Flags));

        _parameters = Marshal.AllocHGlobal(size);
        setDefaults(_parameters);

        _parametersIgnoringPalette = Marshal.AllocHGlobal(size);
        setDefaults(_parametersIgnoringPalette);
        Marshal.WriteInt32(_parametersIgnoringPalette, flags, Marshal.ReadInt32(_parametersIgnoringPalette, flags) | IgnorePaletteFlag);
    }

    /// <summary>What the library says it is — <c>opj_version()</c>.</summary>
    public string Version { get; }

    /// <summary>Decodes and lets the image go — what a timing measures.</summary>
    public void Run(JpxStream stream, int threads) => Decode(stream, threads, static _ => true);

    /// <summary>Decodes and keeps the samples, to set beside what OpenJPEG.net decoded.</summary>
    public JpxPlanes Read(JpxStream stream, int threads) => Decode(stream, threads, Planes);

    private T Decode<T>(JpxStream stream, int threads, Func<IntPtr, T> taken)
    {
        var source = new Source(stream.Data);
        var handle = GCHandle.Alloc(source);
        var codec = IntPtr.Zero;
        var input = IntPtr.Zero;
        var image = IntPtr.Zero;

        try
        {
            codec = _createDecompress(stream.IsRawCodestream ? CodecJ2K : CodecJp2);
            _setErrorHandler(codec, ErrorHeardPointer, GCHandle.ToIntPtr(handle));

            if (_setupDecoder(codec, stream.IgnorePalette ? _parametersIgnoringPalette : _parameters) == 0)
                throw new InvalidOperationException(source.Failure("set up the decoder"));
            // 0 runs every job on the calling thread; more is a pool of that many.
            if (_setThreads(codec, threads) == 0)
                throw new InvalidOperationException($"libopenjp2 refused {threads} thread(s)");

            input = _streamCreate((nuint)StreamChunk, 1);
            _streamSetUserData(input, GCHandle.ToIntPtr(handle), IntPtr.Zero);
            _streamSetUserDataLength(input, (ulong)stream.Data.Length);
            _streamSetRead(input, ReaderPointer);
            _streamSetSkip(input, SkipperPointer);
            _streamSetSeek(input, SeekerPointer);

            if (_readHeader(input, codec, out image) == 0) throw new InvalidOperationException(source.Failure("read the header"));
            if (_decode(codec, input, image) == 0) throw new InvalidOperationException(source.Failure("decode"));
            if (_endDecompress(codec, input) == 0) throw new InvalidOperationException(source.Failure("end the decode"));

            return taken(image);
        }
        finally
        {
            if (image != IntPtr.Zero) _imageDestroy(image);
            if (input != IntPtr.Zero) _streamDestroy(input);
            if (codec != IntPtr.Zero) _destroyCodec(codec);
            handle.Free();
        }
    }

    private static JpxPlanes Planes(IntPtr pointer)
    {
        var image = Marshal.PtrToStructure<Image>(pointer);
        var size = Marshal.SizeOf<ImageComponent>();
        var planes = new JpxPlane[image.NumComps];

        for (var at = 0; at < planes.Length; at++)
        {
            var component = Marshal.PtrToStructure<ImageComponent>(image.Comps + at * size);
            var samples = new int[checked((int)(component.W * component.H))];
            Marshal.Copy(component.Data, samples, 0, samples.Length);
            planes[at] = new JpxPlane((int)component.W, (int)component.H, (int)component.Prec, component.Sgnd != 0, samples);
        }

        return new JpxPlanes(planes);
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public void Dispose()
    {
        Marshal.FreeHGlobal(_parameters);
        Marshal.FreeHGlobal(_parametersIgnoringPalette);
        NativeLibrary.Free(_library);
    }

    /// <summary>The bytes the library reads, and the last error it reported while reading them.</summary>
    private sealed class Source(byte[] data)
    {
        public readonly byte[] Data = data;
        public long Position;
        public string? Error;

        public string Failure(string step) => $"libopenjp2 could not {step}" + (Error is null ? "" : $": {Error.Trim()}");
    }

    private static Source SourceOf(IntPtr user) => (Source)GCHandle.FromIntPtr(user).Target!;

    private static nuint ReadSource(IntPtr buffer, nuint count, IntPtr user)
    {
        var source = SourceOf(user);
        var left = source.Data.Length - source.Position;
        if (left <= 0) return nuint.MaxValue;   // (OPJ_SIZE_T)-1: the end

        var taken = (int)Math.Min((long)count, left);
        Marshal.Copy(source.Data, (int)source.Position, buffer, taken);
        source.Position += taken;
        return (nuint)taken;
    }

    private static long SkipSource(long count, IntPtr user)
    {
        var source = SourceOf(user);
        var to = Math.Clamp(source.Position + count, 0, source.Data.Length);
        var skipped = to - source.Position;
        source.Position = to;
        return skipped;
    }

    private static int SeekSource(long offset, IntPtr user)
    {
        var source = SourceOf(user);
        if (offset < 0 || offset > source.Data.Length) return 0;
        source.Position = offset;
        return 1;
    }

    private static void HeardError(IntPtr message, IntPtr user) => SourceOf(user).Error = Marshal.PtrToStringAnsi(message);

    // opj_dparameters_t, as openjpeg.h lays it out.
    [StructLayout(LayoutKind.Sequential)]
    private struct DecoderParameters
    {
        public uint CpReduce;
        public uint CpLayer;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4096)] public byte[] InFile;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4096)] public byte[] OutFile;
        public int DecodFormat;
        public int CodFormat;
        public uint DaX0, DaX1, DaY0, DaY1;
        public int Verbose;
        public uint TileIndex;
        public uint NbTileToDecode;
        public int JpwlCorrect;
        public int JpwlExpComps;
        public int JpwlMaxTiles;
        public uint Flags;
    }

    // opj_image_t
    [StructLayout(LayoutKind.Sequential)]
    private struct Image
    {
        public uint X0, Y0, X1, Y1;
        public uint NumComps;
        public int ColorSpace;
        public IntPtr Comps;
        public IntPtr IccProfileBuf;
        public uint IccProfileLen;
    }

    // opj_image_comp_t
    [StructLayout(LayoutKind.Sequential)]
    private struct ImageComponent
    {
        public uint Dx, Dy, W, H, X0, Y0, Prec, Bpp, Sgnd, ResnoDecoded, Factor;
        public IntPtr Data;
        public ushort Alpha;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateDecompress(int format);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetDefaultDecoderParameters(IntPtr parameters);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetupDecoder(IntPtr codec, IntPtr parameters);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetThreads(IntPtr codec, int threads);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetMessageHandler(IntPtr codec, IntPtr handler, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr StreamCreate(nuint bufferSize, int isInput);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StreamSetFunction(IntPtr stream, IntPtr function);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StreamSetUserData(IntPtr stream, IntPtr data, IntPtr free);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StreamSetUserDataLength(IntPtr stream, ulong length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadHeader(IntPtr stream, IntPtr codec, out IntPtr image);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DecodeImage(IntPtr codec, IntPtr stream, IntPtr image);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EndDecompress(IntPtr codec, IntPtr stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint StreamRead(IntPtr buffer, nuint count, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long StreamSkip(long count, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StreamSeek(long offset, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void MessageHandler(IntPtr message, IntPtr user);
}
