# Console

Runs a Windows command prompt in a tab — or the shell of a location, such as a WSL distro. Type in the console to work in it yourself; type in the AI bar to say what you want done, or to run a command from there.

---

## Running commands
- Each Console tab runs its own shell for as long as the tab is open, with its own folder, history and environment. Open as many as you need.
- What you type in the console goes to the shell, whatever it reads like. None of it is sent to the assistant when you press Enter.
- Arrow keys, Tab, Home, End, Page Up, Page Down and Ctrl-combinations go to the shell, so interactive programs work as they do in any terminal.
- Ctrl+C stops a command. When a command ignores it, Ctrl+Break force-stops that command and everything it started, and leaves the shell running.
- Ctrl+V pastes into the shell. Right-click Paste puts the clipboard in the AI bar instead, to read over before you run it.
- Ctrl+Shift+C, or right-click and Copy, copies the whole session including the scrollback.
- Open a console on a folder or a whole drive with Cmd Here, in the folder's actions on the [File System](help:FileSystem) page. It starts in that folder.
- On a folder of a location with a shell of its own — a WSL distro — Cmd Here opens that shell instead, starting in the folder. With bash or zsh the path bar follows it as it moves. A location with no shell offers no Cmd Here.
- In a distro's shell the Files tab is empty, and the Environment tab shows and saves Windows' variables, not the distro's.

## With the assistant
- Say what you want done in [the AI bar](locate:AiInputBox) — `what is filling this disk?`. Answers appear in a banner under the console, which clears when you start typing.
- The assistant reads what the commands it runs print, and can see what else is on the screen then, so a password or token shown there is seen too.
- While a program is running, what you type goes to that program, so a `y` or a password is never treated as a question or kept in history.
- The assistant knows the console's folder and which shell runs there — cmd.exe, or a location's own — so it uses that shell's syntax.
- What the assistant may reach is the console's folder and everything under it. That folder is where the shell really is, asked of Windows — or of Linux in a distro — never read off the screen, so printing a file cannot move it.
- A shell started inside the console — PowerShell, another cmd, a nested bash — keeps a folder of its own. The path bar, the Files tab and the assistant stay on the console's own shell until you leave it.
- With your approval it can run commands in this console, read the output and carry on. Plain output reaches it as text; a full-screen program reaches it as its screen.
- A command that keeps going is watched in the background, and you are told the result when it finishes. If you have moved to another tab, a notification with an Open button brings you back.
- Ask a follow-up while a command it started is still going and it knows what that command has printed so far.
- A process that will never return to a prompt, such as a web server, is reported as long-running. One waiting for input is answered, or you are asked.
- It can put a suggested command in the AI bar with a `>` in front, for you to edit and run.
- Type into the console or press Ctrl+C and the background watch stops.

## The AI bar
- A line that is clearly a command runs in the console, and no model sees it: `git status`, `./build.sh`, anything with flags such as `ls -la`. In a distro it knows the distro's own programs.
- A line that is clearly a request goes to the assistant: `why did that fail`, `show me the largest files`.
- A line that could be either — `find large files`, `make install` — is put to your workspace's Disambiguation model, which may be an online one. It runs only if the model says it is a command; otherwise, or with no such model, the assistant has it.
- Start the line with `>` to run it in the console whatever it reads like, with no model seeing it — `>git status`. Use it for an unusual command holding a secret.
- A command from the bar or the assistant first clears anything half-typed at the prompt, so it runs exactly as written.
- With the bar empty or starting with `>`, Up and Down walk back through your commands, most recent first.
- Drop files onto the bar to insert their paths, quoted where they contain spaces. This works on every tab.

## Files and history
- [The Files tab](locate:Console_FilesTab) lists the console's current folder and follows the shell as it moves.
- Double-click a folder to change into it.
- Drag an entry onto the console to type its path, or onto the AI bar to use it there. [Show me](locate:Console_FilesTab,AiInputBox) Files dropped in from elsewhere work the same way.
- Every command you run is listed on the right, newest first. Click one to run it again; it moves back to the top instead of being listed twice.

## Environments
- An environment is a named shell set-up: a tab title, a command sent as soon as the first prompt appears, and variables applied as the shell starts.
- When you have more than one environment, Cmd Here asks which to use. Tick Always use this environment for this location and that folder stops asking.
- [The tab](locate:TabItem_Console) takes the environment's title.
- [The Configure button](locate:Console_Configure) opens this workspace's environments: add, rename or remove them, see which locations are pinned, and unpin them. Renaming an environment keeps its pinned locations.
- Environments and pins belong to the workspace, so separate workspaces keep separate shells.

## Environment variables
- [The Environment tab](locate:Console_EnvironmentTab,Console_EnvFilter,Console_AddEnvVar) lists the variables every shell you open inherits, sorted by name, with a filter that matches names and values.
- Right-click one to copy it as `NAME=value` or to edit it. Add a new one with ＋.
- Saving asks how far the change reaches: Session, this running shell only, gone when the tab closes; User, your Windows user environment, kept, with no elevation; or Machine, every account on the PC, which takes a Windows administrator prompt.
