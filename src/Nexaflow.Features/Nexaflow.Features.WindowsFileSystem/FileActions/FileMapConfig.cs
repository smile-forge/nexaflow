using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Controls;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

[CustomControl(typeof(FileMapEditorControl))]
public sealed class FileMapConfig : IFeatureConfig
{
    public string ConfigName   => "filemap";
    public string FriendlyName => "File Type Actions";

    /// <summary>
    /// When true, a background HKCR scan populates registry-derived file type
    /// mappings on startup. When false, all mappings are user-managed only.
    /// </summary>
    public bool UseRegistryMapping { get; set; } = true;
}
