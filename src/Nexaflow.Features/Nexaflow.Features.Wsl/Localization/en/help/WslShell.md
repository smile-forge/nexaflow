# WSL shell

Runs a WSL distro's own shell in a tab — bash, zsh or whichever shell your Linux account logs in with — in a folder of the distro. The Windows command prompt is the [Console](help:Console).

---

## Opening a shell
- On the [File System](help:FileSystem) page, open a distro from This PC, go to a folder and choose Shell Here in its actions. The shell starts in that folder; on the distro's row in This PC it starts at the distro's root.
- The tab is named after the distro, and the path bar shows the folder as Linux names it, such as `/home/adam`. With bash or zsh it follows the shell as it moves.
- What you type in the console goes to the shell. Arrow keys, Tab, Ctrl-combinations and full-screen programs' keys go to it as in any terminal. Ctrl+V pastes; Ctrl+Shift+C copies the whole session.

## Files
- [The Files tab](locate:Console_FilesTab) lists the shell's folder through the distro, with its files named as Linux names them — `at 12:30.txt` too.
- Double-click a folder to change into it. Drag an entry onto the console to type its Linux path, or out to another program, which is given its `\\wsl.localhost` path.

## Variables
- [The Environment tab](locate:Console_EnvironmentTab) lists the distro's variables, never Windows'. [Live and Startup](locate:Console_EnvLive,Console_EnvStartup) switch between the variables this shell holds now, updated at each prompt, and the ones a new shell starts with once its startup files have run.
- Right-click a variable to copy or edit it, or add one with ＋. Saving asks how far it reaches: Live, an `export` in this shell; or Startup, a line kept in your startup file so every new shell has it. That file is the one your login shell reads — `.bash_profile`, `.bash_login` or `.profile` for bash, `.zshenv` for zsh — and Nexaflow's lines go between its own marks at the end, leaving yours as they are.
- Live is listed only for bash and zsh, whose prompts report it; another shell lists what a new one starts with.

## The AI bar and the assistant
- A line that is clearly a command runs in the shell, and no model sees it; a request goes to the assistant; one that could be either is put to your workspace's Disambiguation model. Start a line with `>` to run it whatever it reads like.
- Files dropped onto the bar are given as their Linux paths.
- With your approval the assistant can run commands in this shell, read the output and carry on, using the distro's own syntax. It may reach the shell's folder and everything under it — where Linux says the shell is, never what the screen shows.
