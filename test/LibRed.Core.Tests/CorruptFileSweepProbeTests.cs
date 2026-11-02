using Xunit;

namespace LibRed.Core.Tests;

// Structural mutation sweep: take a good database, corrupt one field of one page header, and drive every
// read path over the result. What counts as a leak, the databases corrupted and the walk over them live in
// MalformedFile (test/LibRed.Shared), shared with the coverage-guided harness in LibRed.Core.Fuzz.
public class CorruptFileSweepProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void No_structural_mutation_escapes_an_unexpected_exception_type()
    {
        // Deliberately small and fixed-seed: this is a REGRESSION guard, not a search. It runs the same 600
        // mutations every time, so once green it stays green and finds nothing new. Discovery means raising
        // this and varying the seed in an offline run — done at 2500 (12,500 mutations, 208k table reads,
        // 236k index walks, ~2 min) with no leaks, which is what the committed size is guarding. Open-ended
        // search is LibRed.Core.Fuzz's job.
        const int perSource = 120;
        var random = new Random(20260907);

        string directory = Path.Combine(Path.GetTempPath(), $"libred-fuzz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var leaks = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int mutations = 0, reachedCatalog = 0, readTables = 0, walkedIndexes = 0;
        try
        {
            foreach ((string shape, string source) in MalformedFile.Sources(directory, TestDatabases.NorthwindAccdb))
            {
                byte[] clean = File.ReadAllBytes(source);
                int pageSize = TestDatabases.FormatOf(source).PageSize;
                int pages = clean.Length / pageSize;
                output.WriteLine($"{shape}: {pages} pages");

                for (int i = 0; i < perSource; i++)
                {
                    // Page 0 is the database header and has its own tests; offsets below 64 are the page
                    // header, where the pointers, counts and offsets live. A byte flipped anywhere else
                    // lands in string or numeric DATA, which changes a value and proves nothing.
                    int page = random.Next(1, pages);
                    int offset = random.Next(0, 64);
                    byte value = (byte)random.Next(256);

                    byte[] mutated = (byte[])clean.Clone();
                    mutated[page * pageSize + offset] = value;

                    string path = Path.Combine(directory, $"{shape}-{i:000}.accdb");
                    File.WriteAllBytes(path, mutated);
                    mutations++;

                    string where = $"{shape} page {page} offset 0x{offset:X2} = 0x{value:X2}";
                    foreach ((string stage, Exception? leak) in MalformedFile.Exercise(path))
                    {
                        if (stage.StartsWith("catalog: ok", StringComparison.Ordinal)) reachedCatalog++;
                        if (stage.StartsWith("table '", StringComparison.Ordinal)
                            && stage.Contains("': ok", StringComparison.Ordinal)) readTables++;
                        if (stage.StartsWith("index '", StringComparison.Ordinal)
                            && stage.Contains("': ok", StringComparison.Ordinal)) walkedIndexes++;
                        if (leak is null) continue;

                        // One line per distinct (exception type, throwing frame): a hundred mutations of one
                        // field are one bug, and the first one's coordinates are enough to reproduce it.
                        string frame = leak.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "(no frame)";
                        if (seen.Add($"{leak.GetType().Name}|{frame}"))
                        {
                            leaks.Add($"{leak.GetType().Name} at {frame}  [{where}, {stage}]");
                            output.WriteLine("  " + leaks[^1]);
                        }
                    }
                    File.Delete(path);
                }
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best effort */ }
        }

        // A run where every mutation was rejected at `open` would pass while proving nothing, so the depth
        // the mutations actually reached is asserted, not just reported. An earlier version of this had a
        // broken counter reading "read a table 0 times" and still passed.
        output.WriteLine($"{mutations} mutations: reached the catalog {reachedCatalog} times, " +
                         $"read a table {readTables} times, walked an index {walkedIndexes} times, " +
                         $"{leaks.Count} distinct leaks");
        Assert.True(reachedCatalog > 0, "no mutation got past open — the sweep proved nothing");
        Assert.True(readTables > 0, "no mutation reached a single row — the sweep proved nothing");
        Assert.True(walkedIndexes > 0, "no mutation reached an index page — the sweep proved nothing");
        Assert.Empty(leaks);
    }
}
