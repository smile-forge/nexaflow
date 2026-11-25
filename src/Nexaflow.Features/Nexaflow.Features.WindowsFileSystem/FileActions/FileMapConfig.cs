using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Controls;
using Nexaflow.Localization;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;


public sealed class FileMapConfig : IFeatureConfig
{
    public string ConfigName   => "filemap";
    public string FriendlyName => Str.Get("WindowsFileSystem.Config.FileMap");
}
