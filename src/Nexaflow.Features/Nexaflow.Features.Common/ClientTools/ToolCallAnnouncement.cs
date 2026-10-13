namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// A call put to the user in a few fixed words, where it is announced rather than shown for approval: a voice
/// conversation says "Ok. I will delete file test.zip.", does it unless told to stop, then says "File deleted." — or
/// "Aborting delete. The file is unchanged." Every part is in the app's language.
/// </summary>
/// <param name="Action">The verb, as it follows "I will": "delete".</param>
/// <param name="Target">What it acts on, as it follows the verb: "file test.zip".</param>
/// <param name="Doing">What is under way while it runs, for the line that shows it: "Deleting file test.zip".</param>
/// <param name="Done">The sentence said once it is done: "File deleted."</param>
/// <param name="Unchanged">The sentence said when it is stopped: "The file is unchanged."</param>
public sealed record ToolCallAnnouncement(string Action, string Target, string Doing, string Done, string Unchanged);
