# Remote locations and shells — v2.0 spec

v2.0 makes every machine you work on a location in Nexaflow: WSL, Linux disks, SMB shares, SFTP and FTP servers and NFS
exports browse, open, search and edit exactly like `C:\`, and PowerShell, SSH and WSL shells open in the terminal at the
folder you are looking at. The assistant works on them under the same rules it has on local files.

Phase 0 lands in 1.9.0; WSL, SMB and SFTP with SSH shells ship in 2.0; FTP, NFS and Linux disks follow in 2.1; cloud
object stores are targeted for 2.2 or 2.3. Where this names a type, check the graph for collisions before introducing
it — the names below were unused when this was written.

## Goals

- One location model: a remote folder is a first-class path in tabs, breadcrumbs, saved tabsets, `?` search, file
  actions, transfers and AI grants.
- Every existing viewer opens a remote file without knowing it is remote.
- A backend is a plug-in behind one contract, so adding the next protocol touches no feature.
- Remote is never slower to start than local: listings stream, nothing blocks the UI thread, everything is cancellable.
- Credentials never touch disk in clear and never reach the assistant.

**Non-goals for 2.0**

- Mounting remote locations as Windows drive letters for other apps.
- Two-way sync or offline copies of a remote folder.
- Cloud object stores (S3, Azure Blob, Google Drive). They are planned for 2.2 or 2.3, and the 2.0 contract is shaped
  to admit them ([Decisions](#decisions)).
- Hosting a server: Nexaflow is a client only.

## What exists, and what stops it carrying remote

The groundwork is real but synchronous and Windows-path shaped, so the first job is to lift it, not to add backends to it.

| Piece | Where | What it gives | What stops it carrying remote |
| --- | --- | --- | --- |
| `IVirtualFileSystem` | `IO.Common/IVirtualFileSystem.cs` | Transparent over archives, `::{id}` mounts and connected locations; async, cancellable members beside the sync ones; breadcrumbs, parent paths, display names and display forms | Most callers still use the sync members, which serve local paths only |
| `VirtualBacking` | `IO.Common/VirtualMount.cs` | Actions ask how a path's bytes are obtained before handing it to a process; `Remote` marks a path on a location | A remote path has no materialised copy until the content cache lands, so an action needing a native path is suppressed |
| `LocationEntry` | `IO.Common/Locations/LocationEntry.cs` | Name, size, modified, hidden and read-only, symlink target, POSIX mode, owner, group, content hash, archive extras | — |
| WindowsFileSystem feature | `Features.WindowsFileSystem` | The browser, This PC, file actions, transfers | Much of it calls `File.*` / `Directory.*` directly rather than the VFS |
| `FileTransferEngine` | `IO.Common` | Progress, throughput, ETA, cancel | Assumes both ends are Windows paths |
| `IThisPcItemProvider` | `Features.Common/ThisPc` | How OneDrive puts a row on This PC; connections use the same | — |
| `PathScope` | `IO.Common/PathScope.cs` | Assistant reach equals what the user has open | Normalises Windows paths only |
| `PseudoConsoleHostService` + `TerminalScreen` | `IO.Terminal` | ConPTY host for cmd/pwsh and a VT100 screen buffer | The view is a scrolling list of finished lines, so full-screen programs (vim, htop, tmux) are not drawn |
| `ProtectedData` | `Core/ConfigManager.cs` | DPAPI protects stored secrets | Per-file, not a credential store with per-host entries |

## Locations and addressing

A remote folder keeps the `::{id}\…` path form mounts already use, so tab state, breadcrumbs and every string-path caller
carry it unchanged; what changes is that a mount can be backed by a provider rather than a real directory.

- **Connection.** A saved, named endpoint: provider kind, host, port, user, root, options, and a credential reference.
  Its `Id` is stable and path-segment-safe and never changes on rename, exactly like `VirtualMount.Id`.
- **Internal path.** `::{connectionId}\home\adam\notes.md`. The `::` prefix guarantees no real Windows path can collide
  and that `File.Exists` answers false without touching the disk.
- **Segment escaping.** POSIX names may hold `\`, `:`, `*`, `?`, `"`, `<`, `>`, `|` or control characters, or end in a
  dot or space. `LocationPath` percent-encodes exactly those characters in a segment — and `%` itself, so decoding is
  never ambiguous — and decodes them at the provider boundary, so the round trip is lossless. A listing through the VFS
  names each entry by its segment, so it combines into a path with `\`; `GetDisplayName` gives the name back as people
  read it.
- **Case.** Each provider declares whether its names are case-sensitive. Comparisons (selection, `PathScope`, dedup of
  open tabs) ask the location, never `StringComparison.OrdinalIgnoreCase` directly.
- **Display form.** People and the assistant see a URI: `sftp://adam@nas/home/adam/notes.md`, `wsl://Ubuntu/home/adam`,
  `smb://nas/share/docs`. `IVirtualFileSystem.GetDisplayPath` produces it, `TryParseDisplayPath` reads it back for the
  address bar, so typing or pasting one opens it, and `GetDisplayName` / `GetBreadcrumbs` label each segment by its name.
- **WSL and SMB need no escaping when Windows can already reach them.** `\\wsl.localhost\Ubuntu\…` and `\\nas\share\…`
  are real paths; their providers add what Windows lacks (POSIX metadata, a native shell, faster listing), not a new name.

## The provider contract

A backend implements one async contract in `Nexaflow.IO.Common.Locations`, and `IVirtualFileSystem`'s async members
route a `::{id}` path to it; features keep talking to the VFS only.

```csharp
public interface ILocationProvider            // one per protocol, discovered by reflection
{
    string Kind { get; }                       // "sftp", "smb", "wsl", …
    LocationCapabilities Capabilities { get; }
    ValueTask<ILocationSession> ConnectAsync(ConnectionProfile profile,
        ICredentialSource credentials, CancellationToken ct);
}

public interface ILocationSession : IAsyncDisposable          // paths are the provider's: /home/adam/notes.md
{
    IAsyncEnumerable<LocationEntry> EnumerateAsync(string path, CancellationToken ct);
    ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct);
    ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct);
    ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode,
        LocationEntry? expected, CancellationToken ct);       // expected: the version read, checked at commit
    ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct);
    ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct);
    ValueTask CreateDirectoryAsync(string path, CancellationToken ct);
    ILocationWatch? Watch(string path, bool recursive);          // null when Capabilities lacks Watch
    ILocationShell? Shell { get; }                               // a shell on the same host, if any
}

public interface ILocationSessionSource                          // the pool, per connection
{
    ValueTask<ILocationSession> GetSessionAsync(CancellationToken ct);
}
```

- **One file system per runtime.** Sessions belong to a `WorkspaceRuntime`, so each runtime has its own
  `LocationFileSystem` (`WorkspaceRuntime.Vfs`): the shared `VirtualFileSystem` for real, archive and mounted paths,
  plus a `LocationMount` per connected location that routes `::{connectionId}` to its `ILocationSessionSource`.
  `FeatureManager` injects it for an `IVirtualFileSystem` constructor parameter; `VirtualFileSystem.Instance` cannot
  see a runtime's locations, so a feature never reaches for it.

- **Async first, all the way down.** Every member takes a `CancellationToken`. Listing is an `IAsyncEnumerable` so a
  50,000-entry folder paints its first rows at once, as Windows Search hits stream.
- **Sync members refuse a remote path.** `IVirtualFileSystem`'s sync IO members keep serving real, mounted and archive
  paths and throw a typed `LocationRequiresAsyncException`, naming the member, on a remote one. The pure path members
  (`GetParentPath`, `GetBreadcrumbs`, `GetBacking`, …) stay sync and serve every path. A new analyzer flags a sync VFS
  or `System.IO` call in a feature, following the `NXUI001` pattern, and the existing call sites move to the async
  members in phase 0; code that only touches the app's own state is exempted by an attribute that names its reason.
- **Reads say what they need.** `ReadIntent` is `Sequential`, `Random` or `Whole`. A provider with ranged reads (SFTP,
  SMB, FTP `REST`) returns a seekable stream that fetches blocks on demand, so a 4 GB log or a PDF opens at its first
  page. One without spools to the cache and returns the cached file.
- **Writes are atomic where the protocol allows.** `BeginWriteAsync` writes to a sibling temp name and renames on
  commit, so a dropped connection never leaves half a file; without a rename it commits as one whole upload. `WriteMode`
  distinguishes create, replace and append. A write given the entry it read refuses to commit with `Conflict` when the
  target is no longer that version — the check behind conflict-checked saves. Inside an archive the bytes are gathered
  and the archive rewritten in one piece at commit.
- **`LocationEntry` carries more than `VirtualEntry`:** POSIX mode, owner and group, symlink target, hidden and read-only
  flags, and an optional content hash when the server offers one. `VirtualEntry` stays the archive model; an archive
  entry listed through the async VFS carries its archive facts in `LocationEntry.Archive`.
- **Capabilities are flags the UI reads, not errors it discovers:** `RandomRead`, `AtomicRename`, `Watch`, `Symlinks`,
  `Permissions`, `ServerSideCopy`, `CaseSensitive`, `Shell`. A file action whose need is missing is hidden, as
  `VirtualBacking` hides actions for archive entries.
- **`VirtualBacking.Remote`.** An action that would hand the path to an external process gets a materialised copy
  or is suppressed — the rule that protects archive entries, read through `IsFullyOnDisk`. Until the content cache
  lands such an action is suppressed, and a remote file is not browsed as an archive: archive work runs over local
  bytes, so the archive members refuse a remote path.
- **Errors are one typed family** (`LocationException` with `NotFound`, `AccessDenied`, `Disconnected`, `AuthFailed`,
  `HostKeyChanged`, `Conflict`), so every page reports a dropped share the same way.

## Connections and credentials

Connections are saved per `Workspace`, sessions live per `WorkspaceRuntime`, and secrets live only in Windows Credential
Manager; none of this needs admin rights.

| Thing | Scope | Stored |
| --- | --- | --- |
| `ConnectionProfile` (kind, host, port, user, root, options) | per `Workspace`, beside provider configs | `Contexts\<name>\connections\config_<version>.json`, through `ConfigManager` like every per-workspace config, so it migrates the same way |
| Secret (password, key passphrase) | per user | Credential Manager, target `nexaflow:{connectionId}`, via `CredWrite` / `CredRead` |
| SSH host keys | per user | `%USERPROFILE%\.ssh\known_hosts`, read and appended, so OpenSSH and Nexaflow agree |
| Live `ILocationSession` | per `WorkspaceRuntime` | memory only, pooled and ref-counted like provider instances |

- **One session per connection, shared.** The browser, a viewer, a transfer and a terminal on the same host share one
  SSH or SMB session through a pool modelled on `ProviderManager.AcquireProviderSet`, released when the last tab lets go
  and after an idle timeout.
- **SSH auth order:** the Windows OpenSSH agent, then Pageant, then key files named in the profile or `~/.ssh/config`,
  then keyboard-interactive (passwords and 2FA prompts). `~/.ssh/config` host aliases are read, so `ssh nas` and the
  connection named `nas` are the same thing.
- **Host keys:** an unknown key asks once and shows the fingerprint; a changed key is refused with `HostKeyChanged` and no
  button to accept it in the same dialog.
- **SMB** authenticates through Windows (`WNetAddConnection2`), so a share reachable in Explorer needs no Nexaflow
  credential.
- **The assistant never sees a secret and never creates a connection.** It lists connections by label and opens paths on
  ones the user has connected, held by `PathScope` like any other path.

## Backends

Each backend is its own assembly, `Nexaflow.IO.Location.<Kind>`, referencing `IO.Common` only, as the Compressed codec
backends do. Package choices are to be confirmed against NuGet in phase 0.

| Backend | Phase | Built on | Ranged read | Watch | Shell | Main risk |
| --- | --- | --- | --- | --- | --- | --- |
| WSL | 1 | `\\wsl.localhost` for bytes; a small helper run with `wsl.exe -d <distro>` for POSIX metadata and inotify | yes | inotify via the helper | `wsl.exe` in ConPTY | 9P listing is slow on big folders, so the helper also lists |
| SMB | 1 | Windows' SMB client through UNC paths | yes | `ReadDirectoryChangesW` over SMB | none | Credentials, This PC rows and the async path |
| SFTP | 2 | SSH.NET (MIT) | yes | polling, adaptive | SSH shell on the same session | Polling cost on busy folders |
| FTP / FTPS | 3 | FluentFTP (MIT) | yes, `REST` | polling | none | Servers that lie about listings; `MLSD` first, `LIST` parsing as fallback |
| NFS | 4 | Windows' NFS client | yes | polling | none | The client is an optional Windows feature that needs admin to add; Nexaflow uses it where present and explains how to add it where not |
| Linux disks (ext4, btrfs) | 4 | `wsl --mount` through `PrivilegeBridge`, then the WSL provider | yes | via WSL | via WSL | Elevation once per mount, which the bridge handles |

WSL and SMB go first because Windows already reaches their bytes, so they prove the async VFS, POSIX metadata and This PC
rows without a new protocol. SFTP is the headline and lands second.

## Shells

The terminal opens a shell on whatever host the current folder lives on, through one transport contract, and draws
full-screen programs, because over SSH that is half of what people run.

- **`ITerminalTransport`** (`IO.Common/Terminal`, beside the location contracts that hand one out) sits under the
  terminal view-model: bytes in, bytes out, resize, interrupt, exit.
  `PseudoConsoleHostService` becomes the ConPTY transport; an SSH channel with a `pty-req` is the second.
  `ILocationSession.Shell` hands out the right one for its host.
- **Shell profiles are discovered, not configured.** cmd, Windows PowerShell, `pwsh` if installed, each WSL distro from
  `wsl.exe --list`, each saved SSH connection and each `Host` in `~/.ssh/config`. A profile can set its starting folder,
  environment and colours.
- **"Open terminal here" follows the folder.** On `::{sftp-nas}\srv\app` it opens an SSH shell on `nas` at `/srv/app`; on
  a WSL path, that distro at that path.
- **Shell integration keeps browser and terminal in step.** OSC 7 reports the working folder and OSC 133 marks prompts and
  command output, so `cd` can move a linked browser, and the assistant can be given one command's output rather than the
  scrollback.
- **Full-screen programs get a grid view (2.0, phase 2).** `TerminalScreen` keeps a correct VT100 grid; when a program
  switches to the alternate screen buffer (vim, htop, less, tmux), the view draws that grid with cursor, colours and mouse
  reporting, and returns to the line view when it leaves. This is the largest single piece of new UI in 2.0.
- **PowerShell remoting rides SSH.** `pwsh` over SSH is an SSH profile whose command is `pwsh`; WinRM is out of scope.

## Viewers, search and the assistant

Viewers never learn a file is remote: they get a stream from the VFS and a save that goes back through it, and the cache
and grants do the rest.

- **One content cache** under `%LOCALAPPDATA%\Smile\nexaflow\cache\locations\{connectionId}`, keyed by path plus modified
  time, size and server hash when offered. Ranged reads fill it block by block; a whole-file read fills it once.
  Size-capped with least-recently-used eviction, cleared per connection from Options.
- **Save is conflict-checked.** Before a write commits, the provider re-reads the entry; if it changed since the viewer
  opened it, the save stops with `Conflict` and offers compare, overwrite or save as. The same check protects edits the
  assistant makes.
- **Anything that needs a native path** (video playback, Open with, anything run as a process) receives a materialised
  cache path through `VirtualBacking.Remote`, as archive entries do.
- **`?` search on a remote folder** runs on the server where it can — `grep -rn` or `find` over the SSH or WSL shell,
  streamed back as hits. Where it cannot (FTP, SMB without an index), Nexaflow walks and reads files itself, the fallback
  used for unindexed local folders, with the same offer-first prompt.
- **Folder viewlets work remotely.** Git and .NET detection runs through the VFS; `git` runs over the remote shell when
  there is one, so status is the server's answer, not a slow walk of `.git`.
- **Assistant grants are location-aware.** `PathScope` compares through the location, so case sensitivity and escaping are
  the provider's, and a grant on `::{nas}\srv\app` never reaches `::{nas}\srv`. Pages holding a remote path add the host
  to the high-risk check that asks before context leaves the PC, so a folder on a work server is not sent to a cloud
  model by surprise.
- **Transfers are VFS to VFS.** `FileTransferEngine` copies between any two locations, server-side when both ends share a
  session and the capability allows, keeping its progress, throughput, ETA and cancel.

## Performance and failure

A remote location feels local because nothing waits on the network in front of the user, and a dropped link costs a
reconnect, never lost work.

- **Nothing on the UI thread touches the network.** Listings stream in pages of a few hundred rows into the virtualised
  file list; metadata not yet shown (icons by content, Git status) is fetched for visible rows only.
- **Listings are cached and revalidated.** Re-opening a folder shows the cached listing at once and replaces it when the
  fresh one arrives; a watch or the poll keeps it current.
- **Polling adapts.** Without notifications, a visible folder polls every few seconds, a hidden one stops, and an unchanged
  one slows down. Intervals are set from measurement in phase 2.
- **Reconnect is automatic and visible.** A dropped session shows a bar on the affected tabs, retries with backoff, and
  re-opens streams at their offset where ranged reads allow. Unsaved edits stay in the editor and save when the link is
  back.
- **Every operation is cancellable** and reports through the progress shown above the tree for long copies, moves and
  deletes.
- **Latency is a test input.** Test servers run behind an injected delay (for example 80 ms and 250 ms round trip), so
  regressions in perceived speed show in CI rather than on a user's VPN.

## Release slicing and testing

| Release | Phase | Scope |
| --- | --- | --- |
| 1.9.0 | 0 · Foundation | Async VFS, analyzer, location registry, connections, terminal transport — no user-visible feature |
| 2.0 | 1 · WSL and SMB | Async listing, POSIX metadata and This PC rows on paths Windows can reach |
| 2.0 | 2 · SFTP and SSH | SSH.NET sessions, SSH shells, full-screen terminal grid, remote `?` search |
| 2.1 | 3 · FTP and FTPS | FluentFTP, `MLSD` listings with `LIST` as fallback, polling watch |
| 2.1 | 4 · NFS and Linux disks | Windows' NFS client; ext4 and btrfs through `wsl --mount` and the privilege bridge |

Each phase is usable on its own, so the 2.0 date can slip phase 2 without stranding phase 1.

**Testing**

- **Contract suite.** One set of tests every `ILocationProvider` passes (listing, ranged reads, atomic write, rename,
  delete, conflict, cancel, reconnect), run against each backend, so a new protocol is proven by the same bar.
- **Real servers in containers** — OpenSSH, vsftpd, Samba and an NFS server, with latency injected — in a
  `Tests.IO.Locations` suite. They are the fixtures; WSL is the dogfood location. WSL tests run on Windows runners only.
- **`ArchitectureRulesTests`** gains the rule that a `Nexaflow.IO.Location.*` assembly references `IO.Common` only, and
  the new analyzer joins the build.
- **Journeys** for connect, browse, open, edit and save, open terminal here, and a dropped connection, so every new
  AutomationId is named by one.

**Product tree.** The capability lives under Common / Shared: `io-vfs-async` and `io-locations` (the provider contract,
location paths and the runtime file system) beneath `iocommon-vfs`, with a node per backend, the connections, the
terminal transport and the grid view added as each lands. The use cases — connect, browse, open terminal here — get a
`locations` node under `features` with the first backend a person can reach. Each is snaplinked to its code as it lands
and committed in the branch's pending file.

## Decisions

| Question | Decision |
| --- | --- |
| When phase 0 lands | In 1.9.0, alongside the polish work |
| Where connections live | Per `Workspace`, beside provider configs |
| Full-screen terminal grid | In 2.0, as part of phase 2 |
| NFS client | Windows' NFS client where installed, and say so when it is not; a managed client only if users ask |
| Cloud storage (S3, Azure Blob, others) | Targeted for 2.2 or 2.3, so the 2.0 contract admits it |
| Test fixtures | Containerised OpenSSH, Samba, vsftpd and NFS servers; WSL is the dogfood location |

**What the cloud decision asks of the 2.0 contract.** Object stores have no real folders, no atomic rename, no ranged
writes, and listings that can lag a write. So `AtomicRename` and `Watch` are optional capabilities, a provider may report
folders it synthesises from key prefixes, and `BeginWriteAsync` commits as a whole-object upload when there is no rename.
Nothing else in 2.0 changes for it.
