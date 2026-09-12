# Environment Variables

Shows the User and Machine environment variables, and lets you change, add and delete them.

---

## Choosing a scope

- Scope selects which set you are looking at, User or Machine. One at a time, and the page opens on User.
- The box beside it narrows the list by variable name or by value.
- Switching scope drops whatever the box or a `?` search had narrowed the list to.
- Values are shown as they are stored, so a `%VAR%` reference stays written as it is.
- Both scopes are read once in the background when the tab opens, so switching between them does not read them again. Refresh does. [Show me](locate:TabItem_SystemEnvVars)

## Editing a value

- Pick a name on the left. Until you do, the value box is read-only and Save and Delete are unavailable.
- What you type is held until you press Save; nothing is written before that.
- Add asks for a name, then creates that variable in the scope on screen with an empty value.
- Delete asks you to confirm, naming the scope and the variable, before it removes anything.

## PATH and other lists

- When the value contains a `;`, a list of its parts appears below, headed Entries (one per ';' segment).
- Add entry appends what you typed in the box next to it; the arrows move an entry up or down; the cross removes one.
- Each of those rewrites the value above, and still has to be saved.

## Administrator rights

- User-scope changes are written by Nexaflow itself and need no approval.
- Every Machine-scope Save, Delete and Add is carried out by a separate elevated helper, so Windows shows its own User Account Control prompt each time, for that one change.
- Decline that prompt and nothing is written and no error appears; the page is left as it was.
- A program that is already running keeps the environment it started with. Start it again for it to see a change.

## Searching and asking

- Type `?` and what you are looking for into the input box to search names and values with a quoted phrase, a prefix wildcard or a regular expression. The result drives the same filter box. [Show me](locate:AiInputBox)
- To send the variables with a question, turn on Include page context. [Show me](locate:AiBar_ContextToggle)
- [Services](help:SystemServices) is changed through the same elevated helper; [System Info](help:SystemInfo) reports the machine itself.
