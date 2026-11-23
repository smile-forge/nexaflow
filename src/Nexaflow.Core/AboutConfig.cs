using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core;

/// <summary>
/// "About" section in Options — app version and update status, source link, system components and third-party notices.
/// Global (one instance). Registered in App.xaml.cs; rendered by <see cref="AboutControl"/>
/// (no editable properties, so it has nothing to save).
/// </summary>
[CustomControl(typeof(AboutControl))]
public sealed class AboutConfig : IFeatureConfig
{
    public string ConfigName   => "about";
    public string FriendlyName => Str.Get("Shell.Config.About");
}
