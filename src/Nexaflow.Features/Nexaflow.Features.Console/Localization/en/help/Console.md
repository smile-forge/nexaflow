# Console

Runs a Windows command prompt in a tab, and sends anything that isn't a command to the assistant.

---

## Running commands
- Each Console tab runs its own cmd.exe for as long as the tab is open, with its own folder, history and environment. Open as many as you need.
- Arrow keys, Tab, Home, End, Page Up, Page Down and Ctrl-combinations go to the shell, so interactive programs work as they do in any terminal.
- Ctrl+C stops a command. When a command ignores it, Ctrl+Break force-stops that command and everything it started, and leaves the shell running.
- Ctrl+V pastes into the shell. Right-click Paste puts the clipboard in the AI bar instead, to read over before you run it.
- Ctrl+Shift+C, or right-click and Copy, copies the whole session including the scrollback.
- Open a console on a folder or a whole drive with Cmd Here, in the folder's actions on the [File System](help:FileSystem) page. It starts in that folder.

## With the assistant
- At a prompt, Enter runs your line if it starts with a cmd built-in, a program on your PATH or the path of a file that exists. Anything else is taken off the line and sent to the assistant. Answers appear in a banner under the console, which clears when you start typing.
- While a program is running, Enter goes to that program, so a `y` or a password is never treated as a question.
- The assistant knows the console's folder and that it is cmd.exe, so it uses cmd syntax rather than PowerShell.
- With your approval it can run commands in this console, read the output and carry on. Plain output reaches it as text; a full-screen program reaches it as its screen.
- A command that keeps going is watched in the background, and you are told the result when it finishes. If you have moved to another tab, a notification with an Open button brings you back.
- Ask a follow-up while a command it started is still going and it knows what that command has printed so far.
- A process that will never return to a prompt, such as a web server, is reported as long-running. One waiting for input is answered, or you are asked.
- It can put a suggested command in the AI bar with a `>` in front, for you to edit and run.
- Type into the console or press Ctrl+C and the background watch stops.

## The AI bar
- Start a line in [the AI bar](locate:AiInputBox) with `>` to run it in the console — `>git status`. A bare line the console recognises as a command runs there too; anything else goes to the assistant.
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
