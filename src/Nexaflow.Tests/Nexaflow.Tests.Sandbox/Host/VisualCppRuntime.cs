namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>
/// The Visual C++ runtime the app's native libraries load. Windows Sandbox boots a clean Windows image without it, so the
/// host stages its own copy in the session folder and the agent puts it in place before anything runs.
/// </summary>
internal static class VisualCppRuntime
{
    /// <summary>The DLLs of the x64 Visual C++ 2015–2022 redistributable.</summary>
    private static readonly string[] Files =
    [
        "vcruntime140.dll", "vcruntime140_1.dll", "vcruntime140_threads.dll",
        "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll", "msvcp140_atomic_wait.dll", "msvcp140_codecvt_ids.dll",
        "concrt140.dll", "vccorlib140.dll", "vcomp140.dll",
    ];

    /// <summary>Copies the host's runtime into <paramref name="folder"/>.</summary>
    public static void Stage(string folder)
    {
        foreach (var file in Files)
        {
            var source = Path.Combine(Environment.SystemDirectory, file);
            if (File.Exists(source)) File.Copy(source, Path.Combine(folder, file), overwrite: true);
        }
        if (!File.Exists(Path.Combine(folder, Files[0])))
            throw new SandboxException(
                "This machine has no Visual C++ runtime to give the sandbox. Install the x64 Visual C++ redistributable.");
    }

    /// <summary>Puts what was staged in <paramref name="folder"/> into the system directory; returns what it added.</summary>
    public static IReadOnlyList<string> Install(string folder)
    {
        var added = new List<string>();
        foreach (var source in Directory.EnumerateFiles(folder, "*.dll"))
        {
            var target = Path.Combine(Environment.SystemDirectory, Path.GetFileName(source));
            if (File.Exists(target)) continue;
            File.Copy(source, target);
            added.Add(Path.GetFileName(source));
        }
        return added;
    }
}
