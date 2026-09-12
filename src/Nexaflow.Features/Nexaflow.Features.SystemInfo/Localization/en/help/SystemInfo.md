# System Info

Reports what this machine is: its Windows build, hardware, displays, disks and security settings.

---

## The cards

- Facts are grouped into cards, in this order: Operating System, Hardware, Display, Storage, Windows Security.
- A fact the machine will not answer for reads as a dash rather than being left blank.
- A volume with less than 10% free is flagged on the Storage card.
- Windows Security items are marked healthy or worth attention as well as reported: Secure Boot, Virtualization-based Security (VBS), Memory Integrity (HVCI), Credential Guard, Kernel DMA Protection and TPM.

## Where the facts come from

- Most are read from WMI. The Windows display version (24H2 and the like), the build number and the registered owner come from the registry.
- Secure Boot is read from the registry, and the TPM through the TPM Base Services API, so both are reported without administrator rights.
- Graphics memory is read from the display driver's registry key, because the WMI figure caps at 4 GB.
- Where Windows does not report Device Guard at all, VBS and Kernel DMA Protection read Unknown instead of being dropped.

## Refreshing

- The page gathers once when the tab opens. Nothing on it updates by itself afterwards.
- Read everything again with Refresh. [Show me](locate:SysInfo_Refresh)
- Gathering appears next to the title while it runs, and Refresh does nothing until it finishes.

## Searching the facts

- Type `?` and what you are looking for into the input box. [Show me](locate:AiInputBox)
- Both the label and the value of every fact are matched, and the ones that hit are marked where they sit — no card is hidden and there is nothing to step through.
- Refreshing the page clears the marks.

## Asking about this machine

- To send these facts with a question, turn on Include page context and ask in the same box. [Show me](locate:AiInputBox,AiBar_ContextToggle)
- Services and environment variables are separate pages: [Services](help:SystemServices) and [Environment Variables](help:SystemEnvVars).
- Open either by typing `/` and the page name into the same box.
