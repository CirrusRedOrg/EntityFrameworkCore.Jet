using JetLockTrace;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(
        """
        jetlocktrace — decode Jet/ACE byte-range locks in a Process Monitor trace.

          jetlocktrace <trace.csv> [options]

        Options
          --page-size <n>   Database page size. Default 4096 (Jet 4 / ACE); use 2048 for Jet 3.
          --canonical       Emit only the decoded operations, with no file column, so two traces of
                            different scenarios can be diffed directly.
          --file <substr>   Only rows whose path contains <substr>, for tracing one database at a time.

        Capture a trace with Process Monitor, filtered to Process Name is MSACCESS.EXE and Path contains
        your database name, then File > Save > Comma-Separated Values with the default columns.
        """);
    return 0;
}

string? csvPath = null;
var pageSize = 4096;
var canonical = false;
string? fileFilter = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--page-size" when i + 1 < args.Length:
            if (!int.TryParse(args[++i], out pageSize) || pageSize <= 0)
            {
                Console.Error.WriteLine("--page-size needs a positive number.");
                return 1;
            }

            break;
        case "--canonical":
            canonical = true;
            break;
        case "--file" when i + 1 < args.Length:
            fileFilter = args[++i];
            break;
        default:
            if (args[i].StartsWith('-'))
            {
                Console.Error.WriteLine($"Unknown option '{args[i]}'. Try --help.");
                return 1;
            }

            csvPath = args[i];
            break;
    }
}

if (csvPath is null)
{
    Console.Error.WriteLine("No CSV given. Try --help.");
    return 1;
}

if (!File.Exists(csvPath))
{
    Console.Error.WriteLine($"'{csvPath}' does not exist.");
    return 1;
}

try
{
    foreach (TraceEvent e in ProcMonCsv.Read(csvPath))
    {
        if (!LockDecoder.IsDatabaseOrLockFile(e.Path))
        {
            continue;
        }

        if (fileFilter is not null && !e.Path.Contains(fileFilter, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (e.Offset is not { } offset)
        {
            continue;
        }

        bool isLockFile = LockDecoder.IsLockFile(e.Path);

        string decoded = e.Operation switch
        {
            "LockFile" or "UnlockFile" or "UnlockFileSingle" => LockDecoder.DescribeLock(offset, e.Length),
            _ when isLockFile => LockDecoder.DescribeLockFileIo(offset, e.Length),
            _ => LockDecoder.DescribeDatabaseIo(offset, e.Length, pageSize),
        };

        // The lock/unlock pairing is the interesting rhythm, so keep the verb but drop ProcMon's noise.
        string verb = e.Operation switch
        {
            "UnlockFileSingle" or "UnlockFile" => "unlock",
            "LockFile" => "lock",
            "ReadFile" => "read",
            "WriteFile" => "write",
            _ => e.Operation.ToLowerInvariant(),
        };

        Console.WriteLine(
            canonical
                ? $"{verb,-6} {decoded}"
                : $"{verb,-6} {(isLockFile ? "lck" : "db "),-3} {decoded}");
    }
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

return 0;
