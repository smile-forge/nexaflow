# Executable Inspector

Shows what is inside a Windows `.exe`, `.dll` or `.sys` file and how it was signed.

---

## Opening files

- Select an `.exe`, `.dll` or `.sys` in [File System](help:FileSystem) and choose **Inspect** from the action strip. Double-clicking a program still runs it.
- A damaged or malformed file still opens. Whatever could not be read is listed under **Parse diagnostics**, with offsets where they are known.
- The dependency walk, the string sweep and the signature check start when you open their tab, and run in the background.

## Overview

- **Version info**: company, description, product and copyright, with debug and pre-release builds flagged. **Fingerprints**: SHA-256, MD5 and the import hash (ImpHash).
- **Headers**: the DOS, COFF and optional header fields, and every data directory the file carries. A reproducible build's timestamp is reported as a content hash, not as a build time.
- Permissions, addresses, sizes, entropy and a hash for each section, in [the section tree](locate:Executable_Tab_Overview,Executable_SectionTree).
- Right-click a row to copy it, or to open the bytes it points at in the [hex viewer](help:Hex). A hex tab already showing the file moves to the new offset instead of a second one opening.
- **Resources** lists every entry by type, name, language and size. Right-click to extract one — an icon group comes out as a rebuilt `.ico`. **Extract all…** saves the raw entries to a folder you choose.
- **Manifest** decodes the UAC level it asks for (`requireAdministrator` is flagged), `uiAccess`, the Windows versions it declares and whether compatibility shims apply, DPI awareness, long-path support, dependent assemblies and registration-free COM. Anything not decoded is still listed, and **Show raw XML** gives you the original.
- A managed binary gets a **.NET** tab: runtime and metadata versions, whether it is pure IL, strong-name signing, assembly identity, target framework, referenced assemblies and Windows Runtime metadata.

## Imports and dependencies

- [The import tree](locate:Executable_Tab_ImportsExports,Executable_ImportTree) separates ordinary, delay-loaded and bound imports, with each function's hint and import-address slot, and API sets marked. Exports show addresses, ordinals and forwarders, and a self-registering COM server or embedded type library is named.
- **Dependencies** starts from the direct imports. The **+** chip on a module expands it in place.
- Modules are resolved the way Windows resolves them: KnownDLLs from System32 first, then the importing module's own folder, System32, Windows and `PATH`.
- A module that cannot be found is drawn as a hexagon, an API set as a rounded stadium, and a delay-loaded link is dashed. A module already on the map is marked ↩ rather than drawn again, and imports beyond what the diagram can show fold behind one chip.
- Select a module on the diagram or in the tree and the side pane lists which functions its importer uses from it. Open a module in its own inspector tab from its body on the diagram, or by double-clicking it in the tree — a double-click expands a module that is not yet expanded.
- The mouse wheel zooms the diagram. [Switch to the tree, or collapse everything back](locate:Executable_Tab_Dependencies,Executable_DependencyTreeToggle,Executable_CollapseDependencies).
- **Locate**, in the side pane or on a module's right-click menu in either tree, opens the folder the module would load from.

## Strings

- **Strings** lists every readable ASCII and UTF-16 run with its offset, encoding and section. Right-click one to open it in the hex viewer.
- Change [the minimum length and rescan](locate:Executable_Tab_Strings,Executable_StringLength,Executable_RescanStrings). It starts at six characters.
- Type `?` and a word in the AI bar to cut the list down to matching strings.
- A long run is shortened in its row; copy it and you get every character. A file with more runs than the list can carry is capped, and the count says so.

## Signing and analysis

- **Signature**: valid, unsigned, untrusted, expired, revoked or malformed, with signer, issuer, thumbprint, validity dates, timestamp and every certificate in the chain. A file signed through a security catalogue is recognised and the catalogue named.
- [The entropy strip](locate:Executable_Tab_Analysis,Executable_EntropyHeatmap) plots the file from start to end with a line at the packing threshold. Hover for the exact value, click to open those bytes in hex. An executable section at 7 bits per byte or more is flagged.
- **Hardening**: ASLR, DEP, Control Flow Guard, SEH and high-entropy ASLR, with anything missing flagged, plus any section that is both writable and executable.
- **Debug and TLS**: the PDB path, flagged when it gives away the build machine's folders, embedded symbols, and TLS callbacks, which run before the entry point.
- The antivirus products registered with Windows Security Center are named with their status, and [Scan this file](locate:Executable_Tab_Analysis,Executable_ScanButton) asks the installed engine through AMSI. A file too large for one AMSI call is scanned as far as that reaches and says so; when nothing could scan it at all it says so, and never calls that clean. The same scan is offered for any file on the File System page, and takes a whole selection at once.

## With the assistant

- It is given a summary of the binary — format, version, hashes, sections and their entropy, imports and exports, manifest, .NET identity, signature and TLS callbacks — and every name and string from inside the file is treated as data, never as an instruction.
- `?` and a word in the AI bar searches the tab you are looking at: matches are highlighted, collapsed branches open to reveal them, and the search chip steps through them.
