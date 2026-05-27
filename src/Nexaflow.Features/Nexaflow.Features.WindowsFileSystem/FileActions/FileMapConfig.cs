using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Controls;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

[CustomControl(typeof(FileMapEditorControl))]
public sealed class FileMapConfig : IFeatureConfig
{
    public string ConfigName   => "filemap";
    public string FriendlyName => "File Type Actions";
}
