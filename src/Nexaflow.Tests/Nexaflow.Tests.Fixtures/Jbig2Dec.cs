using System.Runtime.InteropServices;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// jbig2dec — the JBIG2 decoder Ghostscript and MuPDF are built on — called in process: what Nexaflow.Media's JBIG2
/// decoder is held to and timed against. The library is loaded from the path given, the <c>jbig2.dll</c>
/// <c>tools/bench/Run-Jbig2Bench.ps1</c> builds from release 0.20; jbig2dec is AGPL, so it is built outside the
/// repository and only measured against.
/// </summary>
public sealed class Jbig2Dec : IDisposable
{
    /// <summary>The variable naming the <c>jbig2.dll</c> to load.</summary>
    public const string Variable = "NEXAFLOW_JBIG2DEC";

    // The release the context constructor is told to expect, as jbig2.h's JBIG2_VERSION_MAJOR and _MINOR say.
    private const int VersionMajor = 0, VersionMinor = 20;
    private const int OptionsEmbedded = 1;
    private const int SeverityWarning = 2, SeverityFatal = 3;

    // jbig2dec reports through a callback with a pointer of the caller's; it is a handle to the decode's messages.
    private static readonly ErrorCallback Report = OnMessage;
    private static readonly nint ReportPointer = Marshal.GetFunctionPointerForDelegate(Report);

    private readonly nint _library;
    private readonly CtxNew _ctxNew;
    private readonly CtxFree _ctxFree;
    private readonly DataIn _dataIn;
    private readonly MakeGlobalCtx _makeGlobalCtx;
    private readonly GlobalCtxFree _globalCtxFree;
    private readonly CompletePage _completePage;
    private readonly PageOut _pageOut;
    private readonly ReleasePage _releasePage;

    public Jbig2Dec(string libraryPath)
    {
        LibraryPath = libraryPath;
        _library = NativeLibrary.Load(libraryPath);
        _ctxNew = Export<CtxNew>("jbig2_ctx_new_imp");
        _ctxFree = Export<CtxFree>("jbig2_ctx_free");
        _dataIn = Export<DataIn>("jbig2_data_in");
        _makeGlobalCtx = Export<MakeGlobalCtx>("jbig2_make_global_ctx");
        _globalCtxFree = Export<GlobalCtxFree>("jbig2_global_ctx_free");
        _completePage = Export<CompletePage>("jbig2_complete_page");
        _pageOut = Export<PageOut>("jbig2_page_out");
        _releasePage = Export<ReleasePage>("jbig2_release_page");
    }

    /// <summary>The library loaded.</summary>
    public string LibraryPath { get; }

    /// <summary>The library <see cref="Variable"/> names, or null when it names none that exists.</summary>
    public static Jbig2Dec? FromEnvironment() =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path && File.Exists(path) ? new Jbig2Dec(path) : null;

    /// <summary>
    /// What a decode gave: the page's bits, rows of whole bytes, 1 for black, the bits past the width 0 — or why
    /// there are none — and the warnings jbig2dec raised on the way.
    /// </summary>
    public sealed record Decoded(byte[]? Bits, int Width, int Height, string? Error, IReadOnlyList<string> Warnings);

    /// <summary>The first page of an embedded stream, with the segments of its globals stream when it has one.</summary>
    public Decoded Decode(byte[] data, byte[]? globals)
    {
        var messages = new List<string>();
        var handle = GCHandle.Alloc(messages);
        var global = nint.Zero;
        var ctx = nint.Zero;
        try
        {
            var data0 = GCHandle.ToIntPtr(handle);
            if (globals is { Length: > 0 })
            {
                var globalsCtx = _ctxNew(nint.Zero, OptionsEmbedded, nint.Zero, ReportPointer, data0, VersionMajor, VersionMinor);
                if (globalsCtx == nint.Zero) return Refused("jbig2dec would not start: not release 0.20?", messages);
                _dataIn(globalsCtx, globals, (nuint)globals.Length);
                global = _makeGlobalCtx(globalsCtx);
            }

            ctx = _ctxNew(nint.Zero, OptionsEmbedded, global, ReportPointer, data0, VersionMajor, VersionMinor);
            if (ctx == nint.Zero) return Refused("jbig2dec would not start: not release 0.20?", messages);
            _dataIn(ctx, data, (nuint)data.Length);
            _completePage(ctx);

            var image = _pageOut(ctx);
            if (image == nint.Zero) return Refused(Fatal(messages) ?? "no page", messages);
            try
            {
                // Jbig2Image: uint32 width, height and stride, then the data pointer.
                var width = Marshal.ReadInt32(image, 0);
                var height = Marshal.ReadInt32(image, 4);
                var stride = Marshal.ReadInt32(image, 8);
                var source = Marshal.ReadIntPtr(image, 16);
                var rowBytes = (width + 7) >> 3;
                var bits = new byte[(long)rowBytes * height];
                var lastMask = (width & 7) == 0 ? (byte)0xFF : (byte)(0xFF << (8 - (width & 7)));
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(source + y * stride, bits, y * rowBytes, rowBytes);
                    if (rowBytes > 0) bits[y * rowBytes + rowBytes - 1] &= lastMask;
                }
                return new Decoded(bits, width, height, null, Warnings(messages));
            }
            finally
            {
                _releasePage(ctx, image);
            }
        }
        finally
        {
            if (ctx != nint.Zero) _ctxFree(ctx);
            if (global != nint.Zero) _globalCtxFree(global);
            handle.Free();
        }
    }

    private static Decoded Refused(string error, List<string> messages) => new(null, 0, 0, error, Warnings(messages));

    private static string? Fatal(List<string> messages)
    {
        lock (messages) return messages.FirstOrDefault(m => m.StartsWith("fatal: ", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Warnings(List<string> messages)
    {
        lock (messages) return [.. messages];
    }

    private static void OnMessage(nint data, nint message, int severity, uint segment)
    {
        if (severity < SeverityWarning || GCHandle.FromIntPtr(data).Target is not List<string> messages) return;
        var text = (severity == SeverityFatal ? "fatal: " : "") + Marshal.PtrToStringUTF8(message);
        lock (messages) messages.Add(text);
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public void Dispose() => NativeLibrary.Free(_library);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ErrorCallback(nint data, nint message, int severity, uint segment);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint CtxNew(nint allocator, int options, nint global, nint callback, nint data, int major, int minor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint CtxFree(nint ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DataIn(nint ctx, byte[] data, nuint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint MakeGlobalCtx(nint ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GlobalCtxFree(nint global);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CompletePage(nint ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint PageOut(nint ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleasePage(nint ctx, nint image);
}
