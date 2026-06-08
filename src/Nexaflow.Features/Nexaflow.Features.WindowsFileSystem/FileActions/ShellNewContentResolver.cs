using System;
using System.IO;
using System.Text;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>How an HKCR <c>ShellNew</c> entry seeds the new file's content.</summary>
public enum ShellNewKind
{
    /// <summary>Create an empty file (the <c>NullFile</c> value).</summary>
    NullFile,
    /// <summary>Copy a template file named by the <c>FileName</c> value.</summary>
    FileName,
    /// <summary>Write inline bytes/text from the <c>Data</c> value.</summary>
    Data,
}

/// <summary>
/// A parsed HKCR <c>ShellNew</c> entry, independent of the registry so the
/// create logic is unit-testable. <c>Command</c>-based entries are intentionally
/// not represented — they require invoking a shell handler, which is out of scope.
/// </summary>
public sealed record ShellNewSpec(
    ShellNewKind Kind,
    string?      FileName   = null,
    byte[]?      Data       = null,
    string?      DataString = null);

/// <summary>
/// The resolved content to materialise for a <see cref="ShellNewSpec"/>:
/// <see cref="Bytes"/> to write, a <see cref="TemplatePath"/> to copy, or
/// neither (an empty file).
/// </summary>
public sealed record ShellNewContent(byte[]? Bytes, string? TemplatePath);

/// <summary>
/// Pure mapping from a <see cref="ShellNewSpec"/> to the bytes/template that
/// should be written. The registry walk lives in <c>ShellNewRegistry</c>; this
/// is kept side-effect-free (except the filesystem probe in
/// <see cref="ResolveTemplatePath"/>) so it can be tested without HKCR.
/// </summary>
public static class ShellNewContentResolver
{
    public static ShellNewContent Resolve(ShellNewSpec spec) => spec.Kind switch
    {
        ShellNewKind.Data when spec.Data is { Length: > 0 }
            => new ShellNewContent(spec.Data, null),
        ShellNewKind.Data when !string.IsNullOrEmpty(spec.DataString)
            => new ShellNewContent(Encoding.UTF8.GetBytes(spec.DataString), null),
        ShellNewKind.FileName when !string.IsNullOrWhiteSpace(spec.FileName)
            => new ShellNewContent(null, ResolveTemplatePath(spec.FileName!)),
        _   => new ShellNewContent(null, null), // NullFile / empty / unresolvable
    };

    /// <summary>
    /// Locates the template file named by a ShellNew <c>FileName</c> value, mirroring how
    /// Explorer resolves it: an absolute path as-is, otherwise the per-user Templates folder
    /// then <c>%SystemRoot%\ShellNew</c>. Returns null when nothing is found.
    /// </summary>
    public static string? ResolveTemplatePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var expanded = Environment.ExpandEnvironmentVariables(fileName);

        if (Path.IsPathRooted(expanded))
            return File.Exists(expanded) ? expanded : null;

        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Templates),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ShellNew"),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ];

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, expanded);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
