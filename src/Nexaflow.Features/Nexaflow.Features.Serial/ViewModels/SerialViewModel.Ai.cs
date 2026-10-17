using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Serial.ClientTools;

namespace Nexaflow.Features.Serial.ViewModels;

public sealed partial class SerialViewModel
{
    /// <summary>Lines of transcript the context carries; serial_read reaches further back.</summary>
    const int ContextLines = 30;

    /// <summary>The port, how it is set, whether it is open, and the end of the transcript.</summary>
    public string GetContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine(SelectedPort is { } port
            ? $"Serial page on {port.DisplayName}: {Settings.Summary}, handshake {Handshake}, DTR {(Dtr ? "on" : "off")}, RTS {(Rts ? "on" : "off")} — {(IsConnected ? "connected" : "not connected")}."
            : "Serial page with no port chosen.");
        sb.AppendLine($"Ports on this machine: {string.Join(", ", Ports.Select(p => p.DisplayName))}.");
        sb.AppendLine($"{BytesIn} byte(s) in, {BytesOut} out. Last {Math.Min(ContextLines, Transcript.Count)} transcript line(s) (← in, → out, · note):");
        sb.Append(TranscriptText(ContextLines, withHex: false));
        sb.AppendLine("Tools: serial_ports, serial_read, serial_connect, serial_send, serial_disconnect.");
        return sb.ToString();
    }

    public IReadOnlyList<IClientTool> GetClientTools() => SerialTools.For(this);

    /// <summary>Each port is its own boundary: a device on COM5 is not the device on COM7.</summary>
    public string? GetSecurityContext() => SelectedPort is { } port ? $"serial:{port.Name}" : "serial";

    public string? GetAiSystemPromptGuidance() =>
        "This page is a serial terminal. Reading the transcript is free; connecting (which can reset a microcontroller through " +
        "DTR) and sending both reach the device, and the user approves each. Send text with a line ending, or bytes as hex; " +
        "serial_send can wait for the reply.";
}
