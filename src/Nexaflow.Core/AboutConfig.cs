using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;

namespace Nexaflow.Core;

/// <summary>
/// Read-only "About" section in Options — app version, source link and third-party notices.
/// Global (one instance). Registered in App.xaml.cs; rendered by <see cref="AboutControl"/>
/// (no editable properties, so it has nothing to save).
/// </summary>
[CustomControl(typeof(AboutControl))]
public sealed class AboutConfig : IFeatureConfig
{
    public string ConfigName   => "about";
    public string FriendlyName => "About";
}
