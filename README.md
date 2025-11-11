All credit to https://gitlab.com/KevinJClark/busiestbox#invoke - this is just a fork for my convinience 

# BusiestBox

BusiestBox is a single-EXE, interactive Swiss-army shell for working with the local filesystem, **an in-memory VFS (`vfs://`)**, and the network. Most commands understand URLs, so you can pipe data to/from HTTP(S) without extra tooling. No third-party libraries required; targets **.NET Framework 4.7.2**.

```
BusiestBox [vfs://]> help

Available Commands:
  cd <path>         - Change directory
  pwd               - Print working directory
  ls [path]         - List directory contents
  mkdir             - Make a directory
  copy|cp           - Copy files or directories
  mv|move           - Move files or directories
  rm|del            - Remove files or directories
  load              - Load an assembly or resource
  invoke            - Invoke a method or function
  assemblies        - List loaded assemblies
  bof               - Execute a BOF
  upload            - Upload a file via HTTP POST
  encrypt           - AES encrypt a file
  decrypt           - AES decrypt a file
  zip               - Zip up files into a ZIP archive
  unzip             - Unzip a ZIP archive
  hash              - Calculate Sha256 hash of files
  netopts           - Show or modify WebClient options
  hostinfo          - Show basic info about running computer
  help              - Show this help menu
  exit|quit         - Exit BusiestBox

Use vfs:// to access the in memory filesystem
```

---

## Table of contents

- [Key ideas](#key-ideas)
  - [VFS (`vfs://`) in-memory filesystem](#vfs-vfs-inmemory-filesystem)
  - [Path resolution, globs, and URLs](#path-resolution-globs-and-urls)
  - [Networking options](#networking-options)
- [Command reference](#command-reference)
  - [cd](#cd)
  - [pwd](#pwd)
  - [ls](#ls)
  - [mkdir](#mkdir)
  - [cat](#cat)
  - [copy / cp](#copy--cp)
  - [move / mv](#move--mv)
  - [rm / del](#rm--del)
  - [load](#load)
  - [assemblies](#assemblies)
  - [invoke](#invoke)
  - [bof](#bof)
  - [upload](#upload)
  - [encrypt / decrypt](#encrypt--decrypt)
  - [zip / unzip](#zip--unzip)
  - [hash](#hash)
  - [netopts](#netopts)
  - [hostinfo](#hostinfo)
  - [help, exit, quit](#help-exit-quit)
- [Examples](#examples)
- [Notes & limitations](#notes--limitations)
- [Building](#building)

---

## Key ideas

### VFS (`vfs://`) in-memory filesystem

- `vfs://` is a hierarchical, case-insensitive, in-memory filesystem.
- You can `cd vfs://`, `ls vfs://`, `copy` to/from it, `zip` from it, etc.
- Paths are written as `vfs://folder/file.bin`. The *current directory* can also be a VFS path; relative paths resolve against it.
- Files live as byte arrays. To reduce forensic leftovers in memory, **VFS deletes zero the byte array before unlinking** (best effort in managed memory).

### Path resolution, globs, and URLs

- **Globs** (`*`, `?`) are expanded for FS and VFS paths.
- **URLs are never glob-expanded or normalized.** Tokens that start with `http://` or `https://` are passed to commands verbatim.
- Some commands do their **own** parsing and intentionally bypass global globbing for parts of their syntax (see each command’s “quirks”).

### Networking options

Use `netopts` to control the built-in `WebClient`:

- User-Agent string
- Proxy mode & credentials
- Timeouts
- Allow/deny redirects
- Enable/disable gzip/deflate auto-decompression
- Insecure TLS mode (ignore certificate errors)
- Updog Mode

These options affect all commands that use `NetUtils.CreateWebClient()` (e.g., `cat` on a URL, `upload`, `hash` for a URL, `unzip` from a URL, `bof` when reading binary args from URLs, etc.).

---

## Command reference

### `cd`
**Usage:** `cd <path>`  
Changes the current directory. `path` can be FS or VFS. `cd ~` goes to your user profile on FS (or VFS root if you’re already in VFS).

### `pwd`
Print current directory (FS or VFS).

### `ls`
**Usage:** `ls [path]`  
Lists a directory. Works on FS or VFS. If omitted, lists the current directory.

### `mkdir`
**Usage:** `mkdir <path> [more...]`  
Creates one or more directories on FS or VFS. Intermediate directories are created as needed.

### `cat`
**Usage:** `cat <pathOrUrl> [more...]`  
- For FS/VFS: reads and prints the file as text (BOM/UTF-16/strict UTF-8; heuristics avoid spewing binary).
- For URLs: downloads and prints the response body.
- If the content looks binary, prints a warning rather than garbage.

### `copy` / `cp`
**Usage:**
```
copy <src> <dest>
copy [-r] <src1> [src2 ...] <destDir>
```
- Copies files between FS and VFS in any direction.
- `-r` required for directories (bash semantics).
- **HTTP(S) sources** are downloaded first, then written to FS/VFS.
- When copying multiple sources, `dest` must be a directory.

### `move` / `mv`
Exactly like `copy`, then deletes the source (files or directories with `-r`). Reuses the copy engine but prints only “move” messages.

### `rm` / `del`
**Usage:** `rm [-f] [-r] [-v] <path1> [path2] ...`  
- `-r` for directories; `-f` to suppress errors; `-v` verbose.
- **VFS files are wiped (bytes set to zero) before unlinking.**  
  This is best-effort and affects *only* the in-memory VFS, not real FS.

### `load`
**Usage:** `load <pathOrUrl>`  
Loads a .NET assembly/resource into the current AppDomain from FS, VFS, or URL. Useful before `invoke` or `assemblies`.

### `assemblies`
Lists loaded assemblies with simple name, short MVID, instance index, size, and path.

### `invoke`
**Usage:**
```
invoke [--outfile <path>] [--method <name>] [--type <fullType>] [--bg]
       <assembly_name|short_mvid[#N]> [arg1 arg2 ...]
```
- Select assembly by simple name or **short MVID** (first GUID chunk). `#N` selects the N-th match (1-based).
- Default method is `Main`. It prefers signatures `(string[] args)`, then `()`, then “all string parameters”.
- `--outfile <path>`: **tee** all `Console.Out` to a file (FS or VFS) while still printing to the console.
- `--bg`: run in background; `Ctrl+C` cancels foreground jobs only.
- **Quirk:** all options must come **before** the selector; everything after the selector becomes method args. Program routes raw tokens so your `--outfile` path isn’t glob-expanded.

### `bof`
Execute a COFF/BOF object.

**Usage:**
```
bof [--entrypoint <name>] [--outfile <path>] <coffPath> [formatString] [arg1 ...]
```
- `--entrypoint` defaults to **`go`**.
- `--outfile` (optional): save Beacon output to FS/VFS while still echoing to console.
- `coffPath`: FS/VFS/URL to a COFF object file.
- `formatString` (optional): pack args like Cobalt Strike’s `bof_pack`.
  - `b` = binary **file/url** (content is inlined)
  - `i` = 4-byte int
  - `s` = 2-byte short
  - `z` = UTF-8 C-string (len+1, zero-terminated)
  - `Z` = UTF-16LE C-string (len+2, zero-terminated)
- **Quirk:** If `formatString` is omitted, no args are packed. The command itself resolves FS/VFS and downloads URLs for both the COFF and any `b` arguments.

### `upload`
**Usage:** `upload <url> <src1> [src2 ...]`  
Uploads one or more sources via `multipart/form-data` to the given URL.

- Sources may be **FS**, **VFS**, or **URLs** (URL sources are downloaded first).
- Field name is `file`; multiple files are sent as multiple parts.
- Large sources are streamed; VFS sources are read from memory.
- Program preserves the URL token; only sources are glob-expanded.

> Pair with the included Python example server (supports plain and HTML-smuggled POSTs).

### `encrypt` / `decrypt`
**Usage:**
```
encrypt <passphrase> <inputPath> <outputPath>
decrypt <passphrase> <inputPath> <outputPath>
```
- Works with **FS** and **VFS** paths. (URL inputs are also supported if your Encrypt/Decrypt wrappers fetch them.)
- Passphrase-based AES (PBKDF2 under the hood). Encryption/decryption is streaming; output is written directly to FS/VFS.

### `zip` / `unzip`
- **zip**: `zip [-r] <output.zip> <input1> [input2 ...]`  
  `-r` includes directories recursively. Inputs can be FS or VFS.
- **unzip**: `unzip <zipfile_or_url> [destination]`  
  If `destination` is omitted, extracts into the current directory (FS or VFS). URL zips are downloaded and streamed to the extractor.
- Availability depends on `System.IO.Compression`; BusiestBox detects support at startup and hides the feature if unavailable.

### `hash`
**Usage:** `hash <pathOrUrl> [more...]`  
Computes **SHA-256**:
- FS/VFS files: hashed from disk/memory (streams for FS).
- URLs: hashed from the HTTP response stream.
- Directories are not supported.

### `netopts`
Show or change networking behavior (affects all `WebClient` calls).

**Usage:**
```
netopts # show current settings
netopts ua <string...>
netopts proxy off|system|<url>
netopts proxy-cred <user> <password> | clear
netopts timeout <milliseconds>
netopts insecure on|off
netopts redirect on|off
netopts decompress on|off
netopts updog_mode on|off
```

### `hostinfo`
Prints basic host telemetry: hostname, OS, IP, elevation marker, username, PID, process path, process architecture, and current working directory.

### `help`, `exit`, `quit`
Self-explanatory.

---

## Examples

```powershell
# Work in-memory
BusiestBox [~]> cd vfs://
BusiestBox [vfs://]> copy C:\Windows\win.ini .
BusiestBox [vfs://]> ls

# Hash a local file and a URL
BusiestBox [vfs://]> hash win.ini https://example.com/file.bin

# Zip a folder from VFS
BusiestBox [vfs://]> zip -r vfs://archive.zip vfs://

# Unzip a URL directly into VFS
BusiestBox [~> unzip https://example.com/payload.zip vfs://payloads/

# Encrypt/decrypt in VFS
BusiestBox [vfs://]> encrypt s3cret vfs://notes.txt vfs://notes.enc
BusiestBox [vfs://]> decrypt s3cret vfs://notes.enc vfs://notes.txt

# Upload a mix of FS/VFS sources to a server
BusiestBox [vfs://]> upload vfs://notes.txt C:\Temp\doc.pdf http://192.168.1.100/

# Load & invoke a method from a loaded assembly, teeing output
BusiestBox [~]> load vfs://mylib.dll
BusiestBox [~]> assemblies
BusiestBox [~]> invoke --outfile vfs://run.log mylib --method Run arg1 arg2

# Run a BOF (no args); save output
BusiestBox [~]> bof --outfile vfs://bof.out vfs://whoami.x64.o

# Run a BOF straight from github
BusiestBox [~]> bof https://github.com/trustedsec/CS-Situational-Awareness-BOF/raw/refs/heads/master/SA/env/env.x64.o

# Run a BOF with args. See [this blog](https://www.trustedsec.com/blog/operators-guide-to-the-meterpreter-bofloader) for working with BOF arguments

BusiestBox [~]> BusiestBox [vfs://]> bof dir.x64.o Zs C:\ 0
```

---

## Notes & limitations

- **Globbing**: FS/VFS tokens with `*`/`?` expand; **URLs never expand**.  
  The shell preserves raw tokens for commands that parse their own flags/paths (e.g., `invoke`, `bof`, `upload`).
- **Zip support** is auto-detected. If the platform lacks `System.IO.Compression`, `zip`/`unzip` are disabled with a clear message.
- **VFS wiping**: `rm` zeroes file byte arrays before unlinking in the VFS. It does **not** shred data on the real filesystem.
- **TLS**: `netopts insecure on` installs a global callback to ignore cert errors for all requests in-process. Use with care.
- **Background jobs**: `invoke --bg` runs on a background thread; `Ctrl+C` cancels only foreground jobs.
- **Updog Mode**: Enable HTML smuggling mode to be used for uploads/downloads with [not_updog.py](https://gitlab.com/KevinJClark/ops-scripts/-/blob/main/whats_updog/not_updog.py)

---

## Building

- **Target:** .NET Framework **4.7.2**  
- **Dependencies:** none (uses BCL only).
- Build as a console app. All functionality is in-process—no external tools required.
- Required to specify the /unsafe checkbox for compiling
- Best practice: Build it as a Release, not a Debug!
- Best practice: Compile as AnyCPU but do not prefer 32-bit (Defaults to x64 on modern systems)

---

## Bugs

Report bugs to the repo. This is 100% vibe coded. Use this code at your own risk also it's for educational use only kthxbye.
