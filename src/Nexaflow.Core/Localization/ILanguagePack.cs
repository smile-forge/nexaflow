using System.IO;

namespace Nexaflow.Core.Localization;

/// <summary>One language's content — in the app a <c>Nexaflow.Language.&lt;code&gt;.dll</c> resource-only assembly
/// (<see cref="AssemblyLanguagePack"/>), in tests an in-memory set.</summary>
internal interface ILanguagePack
{
    string Code { get; }

    /// <summary>Every logical name the pack holds: <c>&lt;ProjectFolder&gt;/&lt;path under Localization/&lt;code&gt;/&gt;</c>,
    /// forward-slashed — <c>Nexaflow.Core/strings.json</c>, <c>Nexaflow.Features.Markdown/help/Markdown.md</c>.</summary>
    IReadOnlyCollection<string> ResourceNames { get; }

    /// <summary>A fresh read of <paramref name="logicalName"/>, or null when this pack does not hold it.</summary>
    Stream? Open(string logicalName);
}
