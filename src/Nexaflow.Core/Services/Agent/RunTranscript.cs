using System.IO;
using System.Text;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// One agent run as the model saw it — every message it was sent and everything it wrote back, whole — written to a
/// file of its own in the folder <see cref="Variable"/> names. For reading what the model was actually given when a
/// run goes wrong; nothing is written unless the variable is set.
/// </summary>
internal sealed class RunTranscript
{
    public const string Variable = "NEXAFLOW_AI_TRANSCRIPTS";

    private readonly string _path;
    private int _written;

    private RunTranscript(string path) => _path = path;

    /// <summary>A transcript for a run begun by <paramref name="input"/>, or null when transcripts are off.</summary>
    public static RunTranscript? Begin(string input)
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } folder) return null;
        Directory.CreateDirectory(folder);
        var name = string.Concat(input.Where(char.IsLetterOrDigit).Take(40));
        return new RunTranscript(Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{name}.txt"));
    }

    /// <summary>A step of the run: the messages sent that were not in an earlier step, and what the model wrote.</summary>
    public void Step(IReadOnlyList<LlmMessage> sent, string written)
    {
        var text = new StringBuilder();
        for (; _written < sent.Count; _written++)
            text.Append(">>> ").Append(sent[_written].Role).Append('\n').Append(sent[_written].Text).Append("\n\n");
        text.Append("<<< written\n").Append(written).Append("\n\n");
        try { File.AppendAllText(_path, text.ToString()); }
        catch (IOException) { /* a transcript is a diagnostic: the run goes on without it */ }
    }
}
