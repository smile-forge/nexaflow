using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

/// <summary>A combo-box choice: the value stored, and the words shown for it in the active language.</summary>
public sealed record ChoiceOption(string Value, string Label)
{
    public static IReadOnlyList<ChoiceOption> MultiFileModes() =>
    [
        new(nameof(MultiFileMode.SingleFileOnly),       Str.Get("WindowsFileSystem.MultiFile.SingleFileOnly")),
        new(nameof(MultiFileMode.SingleLaunchAllFiles), Str.Get("WindowsFileSystem.MultiFile.SingleLaunchAllFiles")),
        new(nameof(MultiFileMode.OneLaunchPerFile),     Str.Get("WindowsFileSystem.MultiFile.OneLaunchPerFile")),
    ];

    public static IReadOnlyList<ChoiceOption> CriteriaTypes(params CriteriaType[] types) =>
        [.. types.Select(t => new ChoiceOption(t.ToString(), LabelOf(t)))];

    private static string LabelOf(CriteriaType type) => type switch
    {
        CriteriaType.Extension         => Str.Get("WindowsFileSystem.CriteriaType.Extension"),
        CriteriaType.OptionalExtension => Str.Get("WindowsFileSystem.CriteriaType.OptionalExtension"),
        CriteriaType.PerceivedType     => Str.Get("WindowsFileSystem.CriteriaType.PerceivedType"),
        CriteriaType.ContentType       => Str.Get("WindowsFileSystem.CriteriaType.ContentType"),
        CriteriaType.MagicNumber       => Str.Get("WindowsFileSystem.CriteriaType.MagicNumber"),
        CriteriaType.PathPattern       => Str.Get("WindowsFileSystem.CriteriaType.PathPattern"),
        _                              => type.ToString(),
    };
}
