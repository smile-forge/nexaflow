using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Dotnet.Viewlets;

/// <summary>
/// Folder viewlet for any directory that holds a .NET solution or project file. A SingleBar
/// toolbar offering one-click restore/build/run/test/clean plus a NuGet-update caution. On a location
/// the commands run on the location's host, with its own <c>dotnet</c>.
/// </summary>
public sealed class DotnetViewlet : IFolderViewlet
{
    private readonly IShellServices     _shell;
    private readonly IVirtualFileSystem _files;

    public DotnetViewlet(IShellServices shell, IVirtualFileSystem files)
    {
        _shell = shell;
        _files = files;
    }

    public string DisplayName    => ".NET";
    public bool   AppliesToDrives => false;

    public string[]? ContainsFileGlobs => ["*.slnx", "*.sln", "*.csproj", "*.fsproj", "*.vbproj"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.SingleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.SingleBar];

    public object CreateViewModel(string folderPath, IViewletController controller)
        => new ViewModels.DotnetViewletViewModel(_shell, _files, folderPath);

    /// <summary>Building, restoring and cleaning write the project's output, so on a location they run on its host.</summary>
    public bool RunsOnLocationHost => true;
}
