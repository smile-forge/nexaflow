# Virtual Disk

Shows what is inside a disk image — its volumes and the files they hold — without writing to it.

---

## What opens here
- Image formats: `.vhd`, `.vhdx`, `.vdi`, `.vmdk`, `.dmg`, `.img` and `.iso`.
- Filesystems read inside them: NTFS, FAT, exFAT, ext, HFS+, btrfs, XFS, SquashFS, and ISO 9660 or UDF in an `.iso`.
- The image file is opened read-only. Nothing on this page changes it.
- Open an image from the file browser with **As Disk**. Each image gets its own tab, titled with the file name. [Show me](locate:TabItem_VirtualDisk,VirtualDiskView)
- An image that cannot be opened says so instead of showing blanks: "No readable filesystem found in this image.", or "Couldn't read image:" with the reason.
- Nothing here asks for a password, so an encrypted volume reads as unreadable rather than prompting.

## The image and its volumes
- The left pane names the format, then CAPACITY, PARTITIONS (GPT, MBR or None) and VOLUMES.
- Each volume gets a row with its filesystem, label, and used of total size.

## Browsing the contents
- Double-click a folder, or click its chevron, to expand it. A folder's children are read the first time it is opened.
- An image with more than one volume lists `Volume 1`, `Volume 2` and so on at the top level; everything under that is inside the volume you opened.
- Double-clicking a file does nothing. This tab inspects the image and never launches anything out of it.
- To open a file that is inside the image, double-click the image in the file browser instead — it walks into the image like a folder, and files open in their usual viewers.
- The status line under the list says what the tab is doing — reading a directory, extracting — and carries the reason when one of those fails.

## Extract
- **Extract** writes the whole image out to a folder you pick. The status line then says where it went.
- Cancelling the folder picker extracts nothing.
- Extract is disabled until the image has been recognised.

## Mount
- **Mount** attaches the image as a real Windows drive letter. It is offered only for `.iso`, `.vhd` and `.vhdx`; the other formats can be browsed and inspected here but not mounted.
- Mounting a `.vhd` or `.vhdx` needs administrator approval, and nothing happens if you decline it. An `.iso` mounts without it.
- When the mount succeeds you are told which drive letter the image was given.
- **Unmount** is offered on the drive in the file browser, and only on drives an image was mounted to from here.

## Searching inside the image
- A `?` search over this page searches the files and folders inside the image, by name or by path within it. A filename glob such as `?*.dll` is matched against the name.
- The search walks the image itself, not just the folders you have opened, so a result does not depend on where you had browsed to.
- A long walk stops at a limit and marks its count with `+`, meaning there may be more. [Show me](locate:VirtualDisk_SearchMatchCount,VirtualDisk_SearchStatus)
- Results replace the list with the matches and the folders they sit in, expanded. Clearing the search re-reads the image root. [Show me](locate:VirtualDisk_SearchClear)

## Asking the assistant
- The assistant can list the volumes, list the entries in one in-disk folder, and read the text of one file inside the image. All three read through the image without extracting it.
- It cannot extract or mount — those are yours to run.
