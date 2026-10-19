using System.Runtime.InteropServices;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// libjpeg-turbo's TurboJPEG 3 library, called in process: what Nexaflow.Media's JPEG decoder is held to and timed
/// against, and the compressor its test fixtures are made with. The library is loaded from the path given — the
/// <c>turbojpeg.dll</c> <c>tools/bench/Run-DctBench.ps1</c> builds — so the reference is whichever build the run names.
/// </summary>
public sealed class LibJpegTurbo : IDisposable
{
    /// <summary>The variable naming the <c>turbojpeg.dll</c> to load.</summary>
    public const string Variable = "NEXAFLOW_TURBOJPEG";

    private const int InitCompress = 0, InitDecompress = 1;
    private const int ParamQuality = 3, ParamSubsampling = 4, ParamJpegWidth = 5, ParamJpegHeight = 6;
    private const int ParamColourspace = 8, ParamProgressive = 12, ParamArithmetic = 14;
    private const int ParamLossless = 15, ParamLosslessPsv = 16, ParamLosslessPt = 17, ParamRestartBlocks = 18, ParamRestartRows = 19;

    /// <summary>TurboJPEG's pixel formats (<c>TJPF_*</c>) the decoder's output comes in.</summary>
    public const int PixelRgb = 0, PixelGrey = 6, PixelCmyk = 11;

    /// <summary>TurboJPEG's JPEG colour spaces (<c>TJCS_*</c>).</summary>
    public const int ColourRgb = 0, ColourYcbcr = 1, ColourGrey = 2, ColourCmyk = 3, ColourYcck = 4;

    /// <summary>TurboJPEG's chroma subsamplings (<c>TJSAMP_*</c>).</summary>
    public const int Sampling444 = 0, Sampling422 = 1, Sampling420 = 2, SamplingGrey = 3, Sampling440 = 4, Sampling411 = 5, Sampling441 = 6;

    private readonly nint _library;
    private readonly Init _init;
    private readonly Destroy _destroy;
    private readonly GetErrorStr _errorText;
    private readonly SetParam _set;
    private readonly GetParam _get;
    private readonly DecompressHeader _header;
    private readonly Decompress8 _decompress;
    private readonly Compress8 _compress;
    private readonly Free _free;

    public LibJpegTurbo(string libraryPath)
    {
        LibraryPath = libraryPath;
        _library = NativeLibrary.Load(libraryPath);
        _init = Export<Init>("tj3Init");
        _destroy = Export<Destroy>("tj3Destroy");
        _errorText = Export<GetErrorStr>("tj3GetErrorStr");
        _set = Export<SetParam>("tj3Set");
        _get = Export<GetParam>("tj3Get");
        _header = Export<DecompressHeader>("tj3DecompressHeader");
        _decompress = Export<Decompress8>("tj3Decompress8");
        _compress = Export<Compress8>("tj3Compress8");
        _free = Export<Free>("tj3Free");
    }

    /// <summary>The library loaded.</summary>
    public string LibraryPath { get; }

    /// <summary>
    /// The library <see cref="Variable"/> names, or null when it names none that exists.
    /// </summary>
    public static LibJpegTurbo? FromEnvironment() =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path && File.Exists(path) ? new LibJpegTurbo(path) : null;

    /// <summary>
    /// What a decode gave: the samples, or why there are none, and whether libjpeg warned on the way. When a fatal
    /// error came after the output was written — libjpeg finds some faults only once the image is out — the buffer it
    /// wrote is <paramref name="Written"/>.
    /// </summary>
    public sealed record Decoded(byte[]? Samples, int Width, int Height, int Components, string? Error, bool Warned, byte[]? Written = null);

    /// <summary>
    /// Decodes as an application taking libjpeg's default output does: grey, RGB or CMYK by the JPEG's colour space,
    /// interleaved, every row whole. Warnings do not stop it; a fatal error does.
    /// </summary>
    public Decoded Decode(byte[] jpeg)
    {
        var handle = _init(InitDecompress);
        try
        {
            if (_header(handle, jpeg, (nuint)jpeg.Length) != 0 && !IsWarning(Error(handle)))
                return new Decoded(null, 0, 0, 0, Error(handle), false);

            var width = _get(handle, ParamJpegWidth);
            var height = _get(handle, ParamJpegHeight);
            var (format, components) = _get(handle, ParamColourspace) switch
            {
                ColourGrey => (PixelGrey, 1),
                ColourCmyk or ColourYcck => (PixelCmyk, 4),
                _ => (PixelRgb, 3),
            };

            var output = new byte[(long)width * height * components];
            if (_decompress(handle, jpeg, (nuint)jpeg.Length, output, 0, format) == 0)
                return new Decoded(output, width, height, components, null, false);

            // TurboJPEG reports a fatal error that follows a warning as a warning too, so the message decides: it is
            // the last one libjpeg emitted, and only its warnings are worded as these are.
            var message = Error(handle);
            return IsWarning(message)
                ? new Decoded(output, width, height, components, null, true)
                : new Decoded(null, width, height, components, message, false, output);
        }
        finally
        {
            _destroy(handle);
        }
    }

    /// <summary>libjpeg's warnings (jerror.h's <c>JWRN_*</c> messages), behind the function name TurboJPEG puts first.</summary>
    private static bool IsWarning(string message)
    {
        var text = message[(message.IndexOf("(): ", StringComparison.Ordinal) is var at and >= 0 ? at + 4 : 0)..];
        return text.StartsWith("Corrupt JPEG data", StringComparison.Ordinal) ||
               text.StartsWith("Premature end of JPEG file", StringComparison.Ordinal) ||
               text.StartsWith("Invalid SOS parameters for sequential JPEG", StringComparison.Ordinal) ||
               text.StartsWith("Warning: unknown JFIF revision", StringComparison.Ordinal) ||
               text.StartsWith("Unknown Adobe color transform code", StringComparison.Ordinal) ||
               text.StartsWith("Inconsistent progression sequence", StringComparison.Ordinal) ||
               text.StartsWith("Application transferred too many scanlines", StringComparison.Ordinal);
    }

    /// <summary>
    /// How <see cref="Encode"/> encodes: the samples' pixel format, the JPEG's colour space and subsampling, and the
    /// coding — lossless when <paramref name="LosslessPsv"/> names a predictor.
    /// </summary>
    public sealed record Encoding(int PixelFormat, int Colourspace, int Subsampling, int Quality = 85,
        bool Progressive = false, bool Arithmetic = false, int LosslessPsv = 0, int LosslessPt = 0,
        int RestartBlocks = 0, int RestartRows = 0);

    /// <summary>Encodes interleaved samples of <paramref name="encoding"/>'s pixel format with libjpeg-turbo's compressor.</summary>
    public byte[] Encode(byte[] samples, int width, int height, Encoding encoding)
    {
        var handle = _init(InitCompress);
        try
        {
            _set(handle, ParamColourspace, encoding.Colourspace);
            _set(handle, ParamSubsampling, encoding.Subsampling);
            _set(handle, ParamQuality, encoding.Quality);
            _set(handle, ParamProgressive, encoding.Progressive ? 1 : 0);
            _set(handle, ParamArithmetic, encoding.Arithmetic ? 1 : 0);
            if (encoding.LosslessPsv > 0)
            {
                _set(handle, ParamLossless, 1);
                _set(handle, ParamLosslessPsv, encoding.LosslessPsv);
                _set(handle, ParamLosslessPt, encoding.LosslessPt);
            }
            if (encoding.RestartBlocks > 0) _set(handle, ParamRestartBlocks, encoding.RestartBlocks);
            if (encoding.RestartRows > 0) _set(handle, ParamRestartRows, encoding.RestartRows);

            nint buffer = 0;
            nuint size = 0;
            if (_compress(handle, samples, width, 0, height, encoding.PixelFormat, ref buffer, ref size) != 0)
                throw new InvalidOperationException(Error(handle));
            try
            {
                var jpeg = new byte[checked((int)size)];
                Marshal.Copy(buffer, jpeg, 0, jpeg.Length);
                return jpeg;
            }
            finally
            {
                _free(buffer);
            }
        }
        finally
        {
            _destroy(handle);
        }
    }

    private string Error(nint handle) => Marshal.PtrToStringAnsi(_errorText(handle)) ?? "";

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public void Dispose() => NativeLibrary.Free(_library);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Init(int initType);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetErrorStr(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetParam(nint handle, int param, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetParam(nint handle, int param);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DecompressHeader(nint handle, byte[] jpeg, nuint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Decompress8(nint handle, byte[] jpeg, nuint size, byte[] output, int pitch, int pixelFormat);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Compress8(nint handle, byte[] source, int width, int pitch, int height, int pixelFormat, ref nint jpeg, ref nuint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint buffer);
}
