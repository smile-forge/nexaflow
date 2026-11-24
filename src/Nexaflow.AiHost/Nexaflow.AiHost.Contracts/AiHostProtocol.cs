using System.Text.Json.Serialization;

namespace Nexaflow.AiHost.Contracts;

/// <summary>The constants both ends of the pipe agree on.</summary>
public static class AiHostProtocol
{
    /// <summary>Raised whenever a message changes shape; a client speaking another version is turned away at its
    /// <see cref="Hello"/>, which is what keeps an app and a host from different builds from misreading each other.</summary>
    public const int Version = 18;

    /// <summary>How long a started host has to open its pipe for the app that started it, and how long it waits for that app to
    /// connect before giving up.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The host's command line: <c>--pipe &lt;name&gt; [--store &lt;folder&gt;] [--adopt &lt;folder&gt;]…
    /// [--memory-share &lt;percent&gt;] [--log-dir &lt;folder&gt;]</c>.
    /// The token arrives on standard input, so it never shows in a process listing. The pipe is the host's to open, under that name.</summary>
    public const string PipeArgument = "--pipe";

    /// <summary>The model store the host keeps, where not the machine's own (<c>%ProgramData%\Smile-Forge\Models</c>) — a test's.</summary>
    public const string StoreArgument = "--store";

    /// <summary>An earlier store whose files the host moves into its own, each when it is the same file the catalog names; given once
    /// for each.</summary>
    public const string AdoptArgument = "--adopt";

    /// <summary>Keeps the store to what is in it, or can be moved in from an earlier one: nothing is downloaded — a UI test's host.</summary>
    public const string NoDownloadsArgument = "--no-downloads";

    /// <summary>The share of this machine's memory, in percent, models on the host's CPU may hold.</summary>
    public const string MemoryShareArgument = "--memory-share";

    /// <summary>The folder the host keeps its log in — named only while the shell's safety logging is on. Without it the
    /// host keeps no log.</summary>
    public const string LogDirArgument = "--log-dir";

    /// <summary>The host's policy file, where not the machine's own (<c>%ProgramData%\Smile-Forge\AiHost\host.json</c>) — a test's.</summary>
    public const string PolicyArgument = "--policy";

    /// <summary>The share of memory models on the CPU hold unless the app says otherwise.</summary>
    public const int DefaultMemoryShare = 50;

    /// <summary>The shares, in percent, a host accepts: never so little a CPU model cannot run, never all of the machine.</summary>
    public static readonly Range MemoryShares = 10..90;
}

/// <summary>The serializer for <see cref="HostMessage"/>, generated at build time.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                             Converters = [typeof(ChannelBytesConverter), typeof(SharedBytesConverter)])]
[JsonSerializable(typeof(HostMessage))]
public sealed partial class AiHostJson : JsonSerializerContext;
