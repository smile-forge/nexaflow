# Archive inspector

Lists what is inside an archive, and lets you read, test, extract, repack, encrypt and sign it.

---

## Opening an archive
- Opens zip and the zip-based document formats, 7z, RAR, the tar family, and the single-file codecs gzip, bzip2, xz, lzma, Zstandard and LZ4.
- Double-click an archive on the [File System](help:FileSystem) page to walk into it as if it were a folder.
- A `.docx`, `.odt` or `.epub` is a zip underneath, and can be browsed as one when you ask for that; opening it normally still gives you its usual viewer.
- Double-click a file inside the archive to open it in its own tab, in the viewer that suits it — text in the [Text viewer](help:Text), Markdown in the [Markdown page](help:Markdown). Nothing is extracted to read it.
- An archive inside an archive opens all the way down.
- The folder part of the breadcrumb goes back to the folder the archive came from. [Show me](locate:Chrome_BreadcrumbBar)

## Testing and extracting
- Test reads every entry through rather than trusting the headers, and reports what it found, such as *OK, 48 entries verified* or *3 of 48 entries failed*.
- Extract asks where to put the files and writes the whole archive there. An entry whose path tries to climb out of the folder you chose is refused.
- Long jobs run in the background, with progress in the status bar, so the tab stays usable. [Show me](locate:Compressed_Test,Compressed_Extract)
- Buttons a format cannot support are dimmed rather than hidden.

## Changing the archive
- Add file puts a file in at the archive's top level. A name already in the archive is replaced; anything else is added. [Show me](locate:Compressed_AddFile)
- Drag files onto the page to add them the same way.
- Edit a file you opened from a zip-family archive and saving writes it back into the archive. 7z, RAR and tar are read-only here and say so. Dropping files onto a read-only format is declined.
- An edit several archives deep is written back outward until the file on disk is rewritten. If any archive on the way is read-only you are told before anything is written.
- Recompress rebuilds the archive at Store, Fast, Optimal or Smallest.
- Convert rewrites it as a zip, a tar, or a compressed tar in gzip, bzip2, Zstandard, LZ4 or Brotli, and offers the single-file codecs when the archive holds exactly one file. Only formats that can actually be written are offered. [Show me](locate:Compressed_Recompress,Compressed_Convert)
- Convert is also the way out of a read-only format: 7z and RAR cannot be changed, but their contents can be written into a zip or tar.
- A conversion is written as a new file beside the original, at a name that is free, so the original is untouched.
- Adding a file, or saving an edited one, is written to a temporary file and swapped in at the end, so a write that fails cannot leave you a half-finished archive.
- On the File System page, Zip It compresses a folder, a file or a selection into one archive beside it, and Unzip here extracts into a new sibling folder with a name of its own. Both run on the file browser's operation queue with progress and a cancel, and run inline where there is no queue.

## Encrypting and signing
- Encrypt asks for a password and writes an AES-256 encrypted zip copy beside the original. Decrypt reverses it, and tells you when the password is wrong. [Show me](locate:Compressed_Encrypt,Compressed_Decrypt)
- Dismissing the password prompt, or leaving it empty, leaves the archive as it was.
- Sign hashes the archive and signs it with your own ECDSA P-256 key, created the first time you use it, and writes a detached `.sig` file beside the archive.
- Sig Check recomputes the hash and verifies it, showing the signer's fingerprint on a match and warning you when it does not match. [Show me](locate:Compressed_Sign,Compressed_Verify)

## Searching
- Type `?` and a word in [the AI bar](locate:AiInputBox) to search every entry at every depth, open folder or not.
- The search matches names and paths: `?docs/guide` finds what is under *docs*, and naming a folder gives you that folder with its contents.
- Filename patterns work: `?*.md` matches on the entry name.
- The tree narrows to the matches, opening the folders above them.
- Clear the search and the tree goes back as you had it, with the same folders open.

## With the assistant
- Ask it to list the whole contents, or to filter the list by a path prefix.
- Ask it to read an entry's text without extracting anything.
- Ask it to run the same integrity check that Test runs.
- Ask it to find something and it can narrow the tree to what it found.
