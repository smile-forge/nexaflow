using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace Nexaflow.AiHost;

/// <summary>
/// Which ONNX Runtime this process runs on. The install carries the CPU runtime; a runtime pack carries the same version
/// built with its CUDA layer, beside NVIDIA's libraries. ONNX Runtime loads its CUDA provider from the folder
/// <c>onnxruntime.dll</c> came from, so the pack's own copy is loaded first, by path, and the managed wrapper is bound to
/// it — before anything touches the runtime.
/// </summary>
internal static class NativeRuntime
{
    public const string OrtLibrary = "onnxruntime";

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

        var ort = NativeLibrary.Load(Path.Combine(pack, OrtLibrary + ".dll"));
        NativeLibrary.SetDllImportResolver(typeof(OrtEnv).Assembly, (name, _, _) => name == OrtLibrary ? ort : IntPtr.Zero);
        Pack = pack;
    }
}
