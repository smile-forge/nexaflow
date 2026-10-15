using System.Text.Json.Serialization;

namespace Nexaflow.AiHost.Contracts;

/// <summary>The constants both ends of the pipe agree on.</summary>
public static class AiHostProtocol
{
    /// <summary>Raised whenever a message changes shape; a host speaking another version is refused at
    /// <see cref="Hello"/>, which is what keeps an app and a host from different builds from misreading each other.</summary>
    public const int Version = 10;

    /// <summary>How long a started host has to connect and say <see cref="Hello"/>; also how long it waits for the
    /// app's pipe before giving up.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The host's command line: <c>--pipe &lt;name&gt; --ai-dir &lt;folder&gt; [--runtime-pack &lt;folder&gt;]</c>.
    /// The token arrives on standard input, so it never shows in a process listing.</summary>
    public const string PipeArgument = "--pipe";

    public const string AiDirArgument = "--ai-dir";

    /// <summary>A runtime pack to load ONNX Runtime and onnxruntime-genai from instead of the install's CPU runtime.</summary>
    public const string RuntimePackArgument = "--runtime-pack";
}

/// <summary>The serializer for <see cref="HostMessage"/>, generated at build time.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HostMessage))]
public sealed partial class AiHostJson : JsonSerializerContext;
