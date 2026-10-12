using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Nexaflow.AiHost;

/// <summary>
/// Which ONNX Runtime and onnxruntime-genai this process runs on. The install carries the CPU runtime; a runtime pack
/// carries the same versions built with their CUDA layers, beside NVIDIA's libraries. ONNX Runtime loads its CUDA
/// provider from the folder <c>onnxruntime.dll</c> came from, so the pack's own copies are loaded first, by path, and
/// both managed wrappers are bound to them — before anything touches either runtime.
/// </summary>
internal static class NativeRuntime
{
    public const string OrtLibrary   = "onnxruntime";
    public const string GenAiLibrary = "onnxruntime-genai";

    /// <summary>The pack the runtime came from, or null for the install's.</summary>
    public static string? Pack { get; private set; }

    /// <summary>Binds the runtime to <paramref name="pack"/>; with none, the install's runtime loads as usual.
    /// Called once, first thing.</summary>
    /// <exception cref="DllNotFoundException">The pack is incomplete.</exception>
    public static void Load(string? pack)
    {
        if (pack is null) return;

        // The pack's folder first on PATH: NVIDIA's libraries are loaded by name, by the CUDA layers.
        Environment.SetEnvironmentVariable("PATH", pack + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));

        var ort   = NativeLibrary.Load(Path.Combine(pack, OrtLibrary + ".dll"));
        var genai = NativeLibrary.Load(Path.Combine(pack, GenAiLibrary + ".dll"));
        IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path) => name switch
        {
            OrtLibrary   => ort,
            GenAiLibrary => genai,
            _            => IntPtr.Zero,
        };
        NativeLibrary.SetDllImportResolver(typeof(OrtEnv).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(OgaHandle).Assembly, Resolve);
        Pack = pack;
    }
}
