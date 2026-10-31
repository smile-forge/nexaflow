using System.Globalization;

namespace Nexaflow.Icons;

/// <summary>
/// An icon kept in a Windows file's resources — a program, a DLL, an .ico — named the way the registry's <c>DefaultIcon</c>
/// values name one: the file, then a number that counts the file's icons from zero or, when negative, gives one icon's
/// resource id. The file is kept as written, environment variables and all, so a reference saved on one machine names the
/// same icon on another; it is expanded only when the icon is drawn.
/// <para>
/// It is saved as its <see cref="ToString"/>, <c>file,index</c> — the <c>DefaultIcon</c> form itself, which
/// <see cref="Parse"/> reads back.
/// </para>
/// </summary>
public readonly record struct ShellIconRef(string File, int Index)
{
    /// <summary>
    /// The icon a <c>DefaultIcon</c>-style <paramref name="spec"/> names (<c>%SystemRoot%\System32\shell32.dll,-4</c>,
    /// <c>"C:\Apps\app.exe"</c>), or null when it names none: empty, or <c>%1</c> — "each file draws its own icon", which is
    /// the file list's to draw rather than a reference to one.
    /// </summary>
    public static ShellIconRef? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;

        var file  = spec.Trim();
        var index = 0;
        var comma = file.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(file.AsSpan(comma + 1).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            index = parsed;
            file  = file[..comma].TrimEnd();
        }

        file = file.Trim('"');
        return file.Length == 0 || file == "%1" ? null : new ShellIconRef(file, index);
    }

    public override string ToString() => File + "," + Index.ToString(CultureInfo.InvariantCulture);
}
