using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using Nexaflow.Features.Font.ViewModels;

namespace Nexaflow.Features.Font;

/// <summary>
/// Loads the renderable families of ONE font file — file-scoped, unlike
/// <c>Fonts.GetFontFamilies(fileUri)</c> / <c>Fonts.GetTypefaces(fileUri)</c>, which actually return
/// every font in the file's <em>directory</em> (e.g. all ~200 families under <c>C:\Windows\Fonts</c>),
/// not just the opened file. <see cref="GlyphTypeface"/> is the only file-scoped reader, so we read the
/// family name(s) from it and build a <see cref="FontFamily"/> per family, referenced against the file's
/// own folder so it still renders (installed or not).
/// </summary>
internal static class FontFileLoader
{
    public static IReadOnlyList<FontFamily> LoadFamilies(Uri fileUri)
    {
        var result = new List<FontFamily>();
        var dir = Path.GetDirectoryName(fileUri.LocalPath);
        if (string.IsNullOrEmpty(dir)) return result;

        var baseUri = new Uri(dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in FamilyNamesInFile(fileUri))
            if (seen.Add(name))
                result.Add(new FontFamily(baseUri, "./#" + name));
        return result;
    }

    private static List<string> FamilyNamesInFile(Uri fileUri)
    {
        var names = new List<string>();
        // A .ttc holds several faces (index via #n); other formats hold one.
        bool collection = string.Equals(Path.GetExtension(fileUri.LocalPath), ".ttc", StringComparison.OrdinalIgnoreCase);
        int max = collection ? 64 : 1;

        for (int i = 0; i < max; i++)
        {
            GlyphTypeface gt;
            try
            {
                var u = i == 0 ? fileUri : new Uri(fileUri.OriginalString + "#" + i);
                gt = new GlyphTypeface(u);
            }
            catch { break; }   // no more faces in the collection

            var name = FontNames.Pick(gt.Win32FamilyNames) ?? FontNames.Pick(gt.FamilyNames);
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name!);
        }
        return names;
    }
}
