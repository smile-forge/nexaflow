namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// How a distro's names and paths read under <c>\\wsl.localhost\&lt;distro&gt;</c>. WSL's file server stands in for each
/// character no Windows name may hold — a control character, or one of <c>\ : * ? " &lt; &gt; |</c> — with U+F000 plus its
/// code, so every Linux name has a Windows one and the mapping goes both ways.
/// </summary>
internal static class WslNames
{
    private const char Base = '\uF000';
    private const string Reserved = "\\:*?\"<>|";

    /// <summary>The share a distro's root is reachable as.</summary>
    public static string NativeRoot(string distro) => $@"\\wsl.localhost\{distro}";

    /// <summary>The share's older name, which Windows still reads.</summary>
    public static string LegacyRoot(string distro) => $@"\\wsl$\{distro}";

    /// <summary>The Windows name a Linux name has under the share.</summary>
    public static string ToNative(string name)
    {
        if (!name.Any(Stands)) return name;
        return string.Create(name.Length, name, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++) span[i] = Stands(source[i]) ? (char)(Base + source[i]) : source[i];
        });
    }

    /// <summary>The Linux name a name under the share stands for — the inverse of <see cref="ToNative"/>.</summary>
    public static string FromNative(string name)
    {
        if (!name.Any(StandsIn)) return name;
        return string.Create(name.Length, name, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++) span[i] = StandsIn(source[i]) ? (char)(source[i] - Base) : source[i];
        });
    }

    /// <summary>Where a distro's path is under its share — <c>/home/a:b</c> on Ubuntu is
    /// <c>\\wsl.localhost\Ubuntu\home\a&#xF03A;b</c>.</summary>
    public static string NativePath(string distro, string path)
    {
        var names = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return names.Length == 0 ? NativeRoot(distro) : NativeRoot(distro) + "\\" + string.Join('\\', names.Select(ToNative));
    }

    /// <summary><see cref="NativePath"/> in the extended-length form, which Windows neither trims nor shortens: the only
    /// form in which <c>notes.</c> opens <c>notes.</c> rather than <c>notes</c>. For this backend's own IO; what is handed
    /// to other programs is the plain form.</summary>
    public static string ExactNativePath(string distro, string path) => @"\\?\UNC\" + NativePath(distro, path)[2..];

    // A character Windows cannot hold in a name.
    private static bool Stands(char c) => c < ' ' || Reserved.Contains(c);

    // A character the file server put in a name's place.
    private static bool StandsIn(char c) => c > Base && c < Base + 0x80 && Stands((char)(c - Base));
}
