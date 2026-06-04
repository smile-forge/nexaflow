using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;

namespace Nexaflow.Features.Dotnet.Viewlets;

/// <summary>
/// Folder viewlet for any directory that holds a .NET solution or project file. A SingleBar
/// toolbar offering one-click restore/build/run/test/clean plus a NuGet-update caution.
/// </summary>
public sealed class DotnetViewlet : IFolderViewlet
{
    private readonly IShellServices _shell;

    public DotnetViewlet(IShellServices shell) => _shell = shell;

    public string DisplayName    => ".NET";
    public bool   AppliesToDrives => false;

    public string[]? ContainsFileGlobs => ["*.slnx", "*.sln", "*.csproj", "*.fsproj", "*.vbproj"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.SingleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.SingleBar];

    public FrameworkElement CreateView(string folderPath, IViewletController controller)
        => new DotnetViewletView(_shell, folderPath);
}
