# System Info

Reports what this machine is: its Windows build, hardware, displays, what it can run AI models on, disks and
security settings.

---

## The cards

- Facts are grouped into cards, in this order: Operating System, Hardware, Display, Local AI, Storage, Windows Security.
- The Local AI card says where each model Nexaflow runs itself in this workspace runs: which graphics card and how
  much of its memory it holds, how long a conversation a language model is given there, and whether the models kept
  loaded make way for a picture while it is drawn. A model with no room on any card runs on the processor, more
  slowly, and one that cannot run there either is marked as too large. The card also names the other language models
  this machine fits, then each graphics card with how much of its memory goes to models, any NPU, and the CUDA driver
  version on NVIDIA machines.
- A fact the machine will not answer for reads as a dash rather than being left blank.
- A volume with less than 10% free is flagged on the Storage card.
- Windows Security items are marked healthy or worth attention as well as reported: Secure Boot, Virtualization-based Security (VBS), Memory Integrity (HVCI), Credential Guard, Kernel DMA Protection and TPM.

## Where the facts come from

- Most are read from WMI. The Windows display version (24H2 and the like), the build number and the registered owner come from the registry.
- Secure Boot is read from the registry, and the TPM through the TPM Base Services API, so both are reported without administrator rights.
- Graphics memory is read from the display driver's registry key, because the WMI figure caps at 4 GB.
- The Local AI card reads graphics cards and NPUs from DirectX's adapter list, which every vendor reports to, and the
  CUDA version from NVIDIA's driver itself. Once Nexaflow's AI host is running, the graphics cards are the ones it
  found, each with the memory that was free when it started, less what it keeps for itself.
- Where Windows does not report Device Guard at all, VBS and Kernel DMA Protection read Unknown instead of being dropped.

## Refreshing

- The page gathers once when the tab opens. Nothing on it updates by itself afterwards.
- Read everything again with Refresh. [Show me](locate:SysInfo_Refresh)
- Gathering appears next to the title while it runs, and Refresh does nothing until it finishes.

## Checking this machine

- Where the workspace runs models of Nexaflow's own, the Local AI card offers Check this machine. [Show me](locate:SysInfo_SectionAction)
- It loads each model where the card says it runs, then has each do its work once: a reply, a sentence spoken and
  heard back, and a picture drawn. The button says which step it is on.
- Throughout, it reads how much memory each graphics card holds, and compares that with what the models were expected to hold.
- It ends by opening its report in a tab — the machine, where each model ran, how long each step took, and the memory.
  The report and its picture are kept in the reports folder beside the AI host's logs, ready to be sent to whoever
  looks into the machine.

## Searching the facts

- Type `?` and what you are looking for into the input box. [Show me](locate:AiInputBox)
- Both the label and the value of every fact are matched, and the ones that hit are marked where they sit — no card is hidden and there is nothing to step through.
- Refreshing the page clears the marks.

## Asking about this machine

- To send these facts with a question, turn on Include page context and ask in the same box. [Show me](locate:AiInputBox,AiBar_ContextToggle)
- Services and environment variables are separate pages: [Services](help:SystemServices) and [Environment Variables](help:SystemEnvVars).
- Open either by typing `/` and the page name into the same box.
