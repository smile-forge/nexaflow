using Nexaflow.Providers.Common;
using System.ComponentModel.DataAnnotations;

namespace Nexaflow.Providers.Aria
{
    public sealed class AriaConfig : IProviderConfig
    {
        public string ConfigName => "aria";
        public string FriendlyName => "Aria";

        [Required]
        [ConfigDisplayName("API Key")]
        public string ApiKey { get; set; } = string.Empty;
    }
}
