# jetlocktrace

Decodes the extended byte-range locks Jet/ACE places while working with a database, from a Process Monitor
trace. Turns raw offsets into pages, users and lock kinds so a trace can be read (and diffed) directly.

```
dotnet run --project tools/JetLockTrace -- trace.csv
```

| Option | |
|---|---|
| `--page-size <n>` | Database page size. Default 4096 (Jet 4 / ACE); 2048 for Jet 3. |
| `--canonical` | Omit the file column, so traces of two scenarios diff cleanly. |
| `--file <substr>` | Only rows whose path contains `<substr>`. |
| `--out [path]` | Write to a file rather than stdout. With no path, writes next to the CSV as `<trace>.locks.txt`, or `<trace>.canonical.txt` under `--canonical`. |

Output goes to stdout by default. `--out` avoids shell redirection — worth using, because `dotnet run`
can emit build output onto stdout and contaminate a redirected file (pass `--no-build` if you do redirect).
The "writing to…" confirmation goes to stderr, so it never ends up in the output.

Comparing two scenarios:

```
jetlocktrace open.CSV   --canonical --out
jetlocktrace update.CSV --canonical --out
diff open.canonical.txt update.canonical.txt
```

## Capturing a trace

In Process Monitor, filter to `Process Name is MSACCESS.EXE` and `Path contains <your database name>`, enable
**Filter > Drop Filtered Events**, then **File > Save > Comma-Separated Values** with the default columns.

For traces to be comparable across experiments, always start from an *identical copy* of one fixture file —
page numbers must mean the same thing in every run.

## What it decodes

Lock offsets pack three fields:

```
offset = (region << 28) | (page << 9) | userNumber
```

The `<< 9` gives each page a 512-byte window in lock space, because an exclusive lock spans 256–512 bytes and
must not reach into the next page's window. It is **not** the page size — Jet 3.5 used 2 KB pages with the same
shift, and ACE uses 4 KB. The locks sit beyond end-of-file, so no database bytes are ever really locked; the
ranges are pure semaphores.

| Region | Lock kind | Target |
|---|---|---|
| `0x1` | User | one byte per connected user, `0x10000001`–`0x100000FF` |
| `0x2` | Write (exclusive) / Read (shared, 1 byte) | data, index and long-value pages |
| `0x3` | Read / Commit — Jet 2.x only | index and long-value pages |
| `0x4` | Table-read | table header page (TBH) |
| `0x5` | Table-write | table header page |
| `0x6` | Table deny-write | table header page |

Width tells you the mode: `1` byte is shared, `256`–`512` is exclusive (256 to block and detect shared locks,
plus enough beyond that to identify the holder).

It also decodes I/O against the database file — page numbers, sub-page field writes, and the **commit-byte
table** at page 0 `0xE00`–`0xFFF` (256 users × 2 bytes), which Access writes immediately before and after a
batch of page writes. A nonzero commit byte with no matching user lock is what makes Access declare a file
suspect and demand a repair.

Region names are the Jet development team's own, from `docs/JetWhitePapers_UPDATE1/Jetlock.docx`.

## Open questions

Things the tool deliberately reports rather than explains, pending experiments:

- **Undocumented groups in region 1.** The white paper describes only the user-slot array
  (`0x10000001`–`0x100000FF`), which is group 0 here. Observed ACE 2010 traces also use groups 1 and 5, each
  following the same handshake as group 0: take the whole 256-byte group exclusively to test whether anyone else
  is present, release, then claim one's own byte within it. What groups 1 and 5 *mean* is unknown.
- **Shared locks whose low byte is not a user number.** Page 68 has been seen locked at `0x2000_882C` and
  `0x2000_882D` — low byte 44 and 45 — on a single-session file where every other lock reads user 1. So for at
  least some shared locks the low byte identifies something else, perhaps a sub-page item. The raw hex is printed
  on every lock line so this stays visible rather than looking like a plausible user number.
- **Which file a lock lands on.** The paper states locks are only ever placed on the lock file, never the
  database. One screenshot showed a region `0x6` lock on the `.accdb`; a later CSV export of the same scenario
  had it on the `.laccdb`. Unresolved — the `lck`/`db ` column exists to settle it.
- **Record index vs user slot** in the lock file. The paper says slot 1 writes "the first 64 bytes" (implying
  `(slot - 1) * 64`) but also that a lock at `0x10000040` writes "starting at 4096 bytes" (implying `slot * 64`).
  Reported as a record index until an experiment settles it.
