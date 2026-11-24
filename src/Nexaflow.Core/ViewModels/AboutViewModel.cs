using Nexaflow.Core.Services;

namespace Nexaflow.Core.ViewModels;

/// <summary>What the About page (<see cref="Controls.AboutControl"/>) is bound to: the session's components it lists and re-checks.</summary>
/// <param name="dependencies">The third-party components the features declare, and what the last probe found of each.</param>
internal sealed class AboutViewModel(ExternalDependencyRegistry dependencies)
{
    /// <summary>The third-party components the features declare, and what the last probe found of each.</summary>
    public ExternalDependencyRegistry Dependencies => dependencies;
}
