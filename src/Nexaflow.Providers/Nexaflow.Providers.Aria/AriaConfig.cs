using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.Aria
{
    /// <summary>
    /// Aria has no remote credentials: the named pipe is guarded by OS ACLs, so there is nothing to
    /// configure today. (A former "API Key" field was collected and stored but never sent — removed;
    /// the lenient config load simply drops it from old files.)
    /// </summary>
    public sealed class AriaConfig : IProviderConfig
    {
        public string ConfigName => "aria";
        public string FriendlyName => "Aria";
    }
}
