namespace Nexaflow.Features.Common;

/// <summary>
/// Marks a plain POCO as a feature configuration section.
/// <c>FeatureManager</c> discovers and instantiates implementors via reflection
/// when <c>FeatureManager.Register(Type)</c> is called.
/// </summary>
public interface IFeatureConfig
{
    /// <summary>Folder slug used as the directory name under %AppData%\Smile\nexaflow\.</summary>
    string ConfigName { get; }

    /// <summary>Human-readable label shown as a section tab in the Options panel.</summary>
    string FriendlyName { get; }
}
