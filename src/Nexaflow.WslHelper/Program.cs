using Nexaflow.WslHelper.Files;
using Nexaflow.WslHelper.Watching;

namespace Nexaflow.WslHelper;

/// <summary>Started by the WSL backend inside a distro, with stdin and stdout as the wire; anything said aside goes to
/// stderr.</summary>
internal static class Program
{
    private static Task<int> Main()
    {
        var accounts = new AccountNames();
        var replies = new ReplyChannel(Console.OpenStandardOutput());
        var session = new HelperSession(replies, new FileRequests(new EntryReader(accounts), replies), new InotifyWatcher(replies),
                                        accounts);
        return session.RunAsync(Console.OpenStandardInput());
    }
}
