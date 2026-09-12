# Services

Lists the Windows services on this machine and lets you start, stop and reconfigure them.

---

## Finding a service

- The Service column shows the display name, with the key name Windows uses to control it underneath. The list is ordered by the display name.
- The box in the header filters on either of those two names. Resting the pointer on a service shows its description.
- Refresh reads every service again. The list does not update by itself while the tab is open.
- The list is read in the background when the tab opens, and dropped when the tab closes. [Show me](locate:TabItem_SystemServices)

## Starting and stopping

- Each row carries Start, Stop, Restart, Pause and Resume, enabled by what that service is doing now.
- Start is offered only on a stopped service; Stop and Restart only on one that is running or paused.
- Pause is offered only where the service accepts being paused, and Resume only on a paused one.
- After an action only that row is re-read, so its status reflects what actually happened.

## Startup type

- The Startup Type box on a row offers Automatic, AutomaticDelayed, Manual and Disabled.
- Choosing one applies it straight away; re-choosing the value already set does nothing.
- The box is then set back from what the service really reports, so it never shows a change that did not take.
- Driver entries can report Boot or System; those are shown as read and are not offered in the list.

## Administrator rights

- Start, Stop, Restart, Pause, Resume and a startup-type change are all carried out by a separate elevated helper, so Windows shows its own User Account Control prompt for each one.
- Decline that prompt and nothing is done and no error appears. The row is re-read either way, so it keeps showing the service's real state.
- If the action is attempted and fails, the reason is shown as an error.

## Searching and asking

- Type `?` and what you are looking for into the input box to filter the same list by service or display name, with a quoted phrase, a prefix wildcard or a regular expression. [Show me](locate:AiInputBox)
- To send the service list with a question, turn on Include page context. [Show me](locate:AiBar_ContextToggle)
- [Environment Variables](help:SystemEnvVars) uses the same elevated helper for machine-scope changes; [System Info](help:SystemInfo) reports the machine itself.
