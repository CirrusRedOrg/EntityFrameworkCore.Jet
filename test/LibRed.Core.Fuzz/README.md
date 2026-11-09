# LibRed.Core.Fuzz

Coverage-guided fuzzing of `LibRed.Core` with [SharpFuzz](https://github.com/Metalnem/sharpfuzz) and libFuzzer.
SharpFuzz rewrites `LibRed.Core.dll` to report the branches each input reaches; libFuzzer keeps the inputs that
reach new ones and mutates those.

The contract is the one the fixed-seed sweep in `LibRed.Core.Tests` (`CorruptFileSweepProbeTests`) enforces, and
both read it from the same file, `test/LibRed.Shared/MalformedFile.cs`: damaged bytes may throw
`InvalidDataException`, `NotSupportedException` or `UnauthorizedAccessException`, and **anything else escaping is
a crash**. On top of that, libFuzzer reports what the sweep cannot see: a **hang** (`-timeout`, e.g. a page chain
that loops) and a **runaway allocation** (`-rss_limit_mb`, e.g. a length field taken at its word).

The sweep stays the regression guard; this is the search. Nothing here runs in CI.

## Targets

| `LIBRED_FUZZ_TARGET` | Input | What it drives |
| --- | --- | --- |
| `database` | a patch list (below) | open, catalog, every table's rows, every index's entries — `MalformedFile.Exercise` |
| `writes` | a patch list (below) | the same walk opened for writing: in each user table still listed, the first of its first 32 rows the write accepts is also rewritten in place, deleted and inserted again, then the operations the input selects run (below), before its indexes are walked |
| `properties` | raw bytes | `PropertyBlob.Read`, the `LvProp` blob decoder |
| `namemap` | raw bytes | `NameMap.ReadRow` and `NameMap.ReadProperty` |

**The `database` and `writes` input is not a file.** A database is hundreds of pages, libfuzzer-dotnet carries at most 1 MiB,
and mutating a whole file spends nearly every mutation on row data. Instead the input is
`[seed][page u16][offset u16][byte]…`, applied to one of six good databases built at startup: Northwind, the four
shapes `MalformedFile.Sources` authors (multi-page TDEF, long values, split index, altered columns), and the Agile
-encrypted fixture opened with its password. Every field is reduced modulo what it indexes, so every input decodes
to a database, page 0 included.

`writes` holds the write paths to the same contract with one widening: a write may also refuse with
`InvalidOperationException`. Damage to a schema routinely makes a row's own values break it — a Null key, a value
past its column, a record past the page, a write-once column — and the write API refuses those as ACE does, so
treating them as leaks only buries the real findings: an out-of-range index, a null reference, an overflow, a hang.
The cost is that damage which surfaces as `InvalidOperationException` goes unflagged here. The two targets take
the same inputs, so `corpus\database` is a good starting corpus for `corpus\writes`.

**The first byte also picks the `writes` workload.** Its remainder by six is the seed; its quotient is a set of
flags, bit *n* selecting entry *n* of `MalformedFile.Operations`, run on every user table in this order:

| Bit | Operation | What it reaches |
| --- | --- | --- |
| 0 | long values | a 9,000-byte value into every Memo and OLE column of the first row, then null: long-value chains allocated and freed |
| 1 | add and drop columns | a Long and a Text column added and dropped: four definition rewrites |
| 2 | create and drop an index | an index built from the table's rows over its first Long or Text column, then dropped |
| 3 | delete and roll back | the first 32 rows deleted by a statement that rolls back |
| 4 | rename and rename back | the table and its first column renamed and named back |
| 5 | drop the table | every page the table owns freed, then released at close |

Each runs as one statement, in a transaction of its own that a refusal rolls back, as `QueryEngine` runs a writing
statement. A byte holds at most 42 as a quotient, so a single input reaches every operation alone and many of their
combinations, not all of them at once. An input written before the workload existed keeps its patches: it simply
runs the operations its first byte now selects after the row rewrite it always ran.

## Running it

You need the SharpFuzz instrumenter and the libFuzzer driver. Both are third-party downloads, not restored by the
build:

- `dotnet tool install --global SharpFuzz.CommandLine`
- **libfuzzer-dotnet** from <https://github.com/Metalnem/libfuzzer-dotnet/releases> — the Windows build is
  `libfuzzer-dotnet-windows.exe`. On Linux, build it: `clang -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet`.

Publish, then instrument **only `LibRed.Core.dll`** — coverage from anything else is noise, and the harness
assembly itself must stay uninstrumented:

```powershell
dotnet publish test\LibRed.Core.Fuzz -c Release -o fuzz-bin
sharpfuzz fuzz-bin\LibRed.Core.dll
```

`sharpfuzz` rewrites the dll in place, so publish afresh rather than instrumenting a dll that already has been.

Then fuzz one target. libfuzzer-dotnet passes the child exactly one argument (the dll), so the target goes in the
environment. The corpus directory may start empty; libFuzzer grows it.

```powershell
$env:LIBRED_FUZZ_TARGET = "writes"
mkdir corpus\writes
libfuzzer-dotnet-windows.exe --target_path=dotnet --target_arg=fuzz-bin\LibRed.Core.Fuzz.dll `
    -timeout=10 -rss_limit_mb=4096 -max_len=256 `
    -use_value_profile=1 "-dict=test\LibRed.Core.Fuzz\database.dict" corpus\writes
```

The quotes matter in PowerShell: an argument that starts with `-` and contains a `.` is otherwise split at the dot,
and libFuzzer then takes `.Core.Fuzz\database.dict` for a corpus directory that does not exist.

`-use_value_profile=1` makes libFuzzer count a comparison that got closer as new coverage, which is what gets a
patch past a check on one exact value — a type code, a flag bit, a page number. `database.dict` gives the mutator the
multi-byte values it is slow to guess at random: the u16 page numbers and offsets of the fields a reader steers by
(page 0's map pointers, the TDEF counters, a column descriptor's type and flags) and the values they take. Both apply
to `database` as well; the blob targets want neither.

`-max_len=256` is about 50 patches, plenty for `database`; the blob targets want more (`-max_len=4096`). Every
`database` run copies and writes a seed file, so it is the slow target — `replay` gets through 300 random inputs
in about four seconds, startup and authoring the seeds included, uninstrumented — and the blob targets are far
faster. Add `-jobs=N -workers=N` to use more cores, `-max_total_time=<seconds>` to stop on a budget.

**When a run stops finding anything, change what it is given rather than running it longer.** Merge what earlier
runs found into a fresh, minimal corpus — libFuzzer keeps only the inputs that still add coverage — and start the
next run from that:

```powershell
mkdir corpus\merged
libfuzzer-dotnet-windows.exe --target_path=dotnet --target_arg=fuzz-bin\LibRed.Core.Fuzz.dll `
    -merge=1 -timeout=10 -rss_limit_mb=4096 corpus\merged corpus\writes corpus\database
```

## When it finds something

libFuzzer stops and writes the input as `crash-<sha1>` (or `timeout-…`, `oom-…`) in the current directory.
Replay it uninstrumented for the full stack:

```powershell
dotnet fuzz-bin\LibRed.Core.Fuzz.dll replay database crash-<sha1>
```

`replay` also takes a directory, running every input in it and exiting non-zero if any leaks — useful to check a
corpus against a fix. A leak is fixed in the reader, where the bad value is first read, as the throw site of an
`InvalidDataException` that says what was wrong; never by widening the contract.
