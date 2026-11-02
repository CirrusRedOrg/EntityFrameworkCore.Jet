using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Tests.Shared;
using SharpFuzz;

namespace LibRed.Core.Fuzz;

// Coverage-guided fuzzing of LibRed.Core with SharpFuzz + libFuzzer. See README.md for how to run it.
//
// Every target holds LibRed to the contract MalformedFile states: a damaged input may throw
// InvalidDataException, NotSupportedException or UnauthorizedAccessException and nothing else. Anything else
// escaping a target is a crash to libFuzzer. Hangs and runaway allocations are libFuzzer's -timeout and
// -rss_limit_mb, which is the half of the problem the fixed-seed sweep in LibRed.Core.Tests cannot see.
//
// Two ways in:
//   LIBRED_FUZZ_TARGET=<target>, run under libfuzzer-dotnet   — fuzz (libfuzzer-dotnet passes one argument,
//                                                               the dll, so the target travels in the env)
//   dotnet LibRed.Core.Fuzz.dll replay <target> <file|dir>    — run saved inputs (a crash, a corpus) once
//                                                               each, uninstrumented, and report any leak
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["replay", string replayTarget, string input])
            return Replay(Target(replayTarget), input);

        string? name = Environment.GetEnvironmentVariable("LIBRED_FUZZ_TARGET");
        if (name is null || args is ["replay", ..])
        {
            Console.Error.WriteLine("Fuzz:   set LIBRED_FUZZ_TARGET and run under libfuzzer-dotnet (see README.md)");
            Console.Error.WriteLine("Replay: LibRed.Core.Fuzz replay <target> <file|directory>");
            Console.Error.WriteLine($"Targets: {string.Join(", ", Targets)}");
            return 2;
        }

        Fuzzer.LibFuzzer.Run(Target(name));
        return 0;
    }

    private static readonly string[] Targets = ["database", "writes", "properties", "namemap"];

    private static ReadOnlySpanAction Target(string name) => name switch
    {
        "database" => new DatabaseTarget(write: false).Run,
        "writes" => new DatabaseTarget(write: true).Run,
        "properties" => Properties,
        "namemap" => NameMapBlob,
        _ => throw new ArgumentException($"Unknown target '{name}'. Targets: {string.Join(", ", Targets)}.", nameof(name)),
    };

    /// <summary>An <c>LvProp</c> property blob, as the catalog reads one for every table.</summary>
    private static void Properties(ReadOnlySpan<byte> data)
    {
        try { PropertyBlob.Read(data); }
        catch (Exception e) when (MalformedFile.IsExpected(e)) { }
    }

    /// <summary>A name map, in both of its stored layouts: an <c>MSysNameMap</c> row and an <c>LvProp</c>
    /// property.</summary>
    private static void NameMapBlob(ReadOnlySpan<byte> data)
    {
        try { NameMap.ReadRow(data); }
        catch (Exception e) when (MalformedFile.IsExpected(e)) { }
        try { NameMap.ReadProperty(data); }
        catch (Exception e) when (MalformedFile.IsExpected(e)) { }
    }

    /// <summary>Runs each saved input through <paramref name="target"/> once and reports what leaks.</summary>
    private static int Replay(ReadOnlySpanAction target, string input)
    {
        string[] files = Directory.Exists(input) ? Directory.GetFiles(input) : [input];
        int leaks = 0;
        foreach (string file in files)
        {
            try { target(File.ReadAllBytes(file)); }
            catch (Exception e)
            {
                leaks++;
                Console.WriteLine($"LEAK {Path.GetFileName(file)}: {e}");
            }
        }
        Console.WriteLine($"{files.Length} inputs, {leaks} leaked");
        return leaks == 0 ? 0 : 1;
    }
}

/// <summary>
/// A whole database, damaged by the fuzz input and then walked as a caller would (MalformedFile.Exercise) —
/// read only, or with a row of each user table rewritten, deleted and inserted again on the way.
/// </summary>
/// <remarks>
/// The input is not a file. A database is hundreds of pages and libfuzzer-dotnet carries at most 1 MiB, and
/// mutating a whole file spends nearly every mutation on row data. So the input is a patch list against one of
/// a fixed set of good seed databases: <c>[seed][page u16][offset u16][byte]…</c>, each field reduced modulo
/// what it indexes. Inputs stay a few bytes long, every one decodes to a database, and libFuzzer still learns
/// which patches reach new code. Unlike the sweep, page 0 and every offset are in range.
/// </remarks>
internal sealed class DatabaseTarget
{
    private const int PatchSize = 5;

    private readonly bool _write;
    private readonly string _directory;
    private readonly string _path;
    private (string Shape, byte[] Bytes, int PageSize, string? Password)[]? _seeds;

    public DatabaseTarget(bool write)
    {
        _write = write;
        _directory = Path.Combine(Path.GetTempPath(), $"libred-fuzz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        // libFuzzer ends the process on a crash, which skips this; the folder is then left in %TEMP%.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { /* best effort */ }
        };

        // One path, rewritten every run. The page cache and lock registries are per path and released on the
        // last close, so a run that leaves a handle open fails the next write — a leak worth seeing.
        _path = Path.Combine(_directory, "input.accdb");
    }

    public void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        // Built on the first input, not in the constructor: authoring the seeds and detecting their page size
        // run instrumented LibRed.Core code, and SharpFuzz's coverage map only exists once Fuzzer.LibFuzzer.Run
        // has started — instrumented code called before that dereferences a null map.
        _seeds ??= LoadSeeds();
        (_, byte[] clean, int pageSize, string? password) = _seeds[data[0] % _seeds.Length];

        byte[] bytes = (byte[])clean.Clone();
        int pages = bytes.Length / pageSize;
        for (int i = 1; i + PatchSize <= data.Length; i += PatchSize)
        {
            int page = BinaryPrimitives.ReadUInt16LittleEndian(data[i..]) % pages;
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(data[(i + 2)..]) % pageSize;
            bytes[page * pageSize + offset] = data[i + 4];
        }
        File.WriteAllBytes(_path, bytes);

        foreach ((_, Exception? leak) in MalformedFile.Exercise(_path, password, _write))
            if (leak is not null)
                ExceptionDispatchInfo.Throw(leak);
    }

    private (string Shape, byte[] Bytes, int PageSize, string? Password)[] LoadSeeds()
    {
        string data = Path.Combine(AppContext.BaseDirectory, "Data");
        var sources = MalformedFile.Sources(_directory, Path.Combine(data, "Northwind.accdb"))
            .Select(s => (s.Shape, s.Path, Password: (string?)null))
            .Append(("encrypted", Path.Combine(data, "EncryptedTest.accdb"), "Test"));

        return [.. sources.Select(s =>
        {
            byte[] bytes = File.ReadAllBytes(s.Item2);
            return (s.Item1, bytes, JetFormatBase.Detect(new MemoryStream(bytes)).PageSize, s.Item3);
        })];
    }
}
