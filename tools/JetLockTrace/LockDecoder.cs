namespace JetLockTrace;

/// <summary>
///     Decodes the extended byte-range lock offsets Jet/ACE places on the lock file, and the page/field targets of
///     reads and writes against the database file.
/// </summary>
/// <remarks>
///     <para>
///         Lock offsets pack three things:
///     </para>
///     <code>
///         offset = (region &lt;&lt; 28) | (page &lt;&lt; 9) | userNumber
///     </code>
///     <para>
///         The <c>&lt;&lt; 9</c> spacing gives every page a 512-byte window in lock space, because an exclusive lock
///         spans 256-512 bytes and must not reach into the next page's window. It is <b>not</b> the page size — Jet
///         3.5 used 2 KB pages and the same shift, and ACE uses 4 KB.
///     </para>
///     <para>
///         The locks are placed beyond end-of-file, so nothing in the database is ever actually locked; the ranges are
///         pure semaphores. Region names come from the Microsoft Jet locking white paper
///         (<c>docs/JetWhitePapers_UPDATE1/Jetlock.docx</c>) and are the Jet development team's own.
///     </para>
/// </remarks>
public static class LockDecoder
{
    /// <summary>Bytes of lock space reserved per page. See the remarks on <see cref="LockDecoder" />.</summary>
    private const int LockBytesPerPage = 512;

    /// <summary>Offset of the user commit-byte table within page 0.</summary>
    private const int CommitByteTableOffset = 0xE00;

    /// <summary>Bytes per user in the commit-byte table (Jet 3.0 and later; Jet 2.x used one).</summary>
    private const int CommitByteWidth = 2;

    /// <summary>Bytes per entry in the lock file: 32 for the computer name, 32 for the security name.</summary>
    private const int UserRecordWidth = 64;

    private static string RegionName(long region)
        => region switch
        {
            0x1 => "user-lock",
            // Jet 3.0 moved read locks into the write-lock range; they differ only by being shared and 1 byte wide.
            0x2 => "write/read",
            0x3 => "read/commit",
            0x4 => "table-read",
            0x5 => "table-write",
            0x6 => "deny-write",
            _ => $"region-{region:X}",
        };

    /// <summary>Describes a lock or unlock at <paramref name="offset" /> spanning <paramref name="length" /> bytes.</summary>
    public static string DescribeLock(long offset, long? length)
    {
        long region = (offset >> 28) & 0xF;
        string kind = RegionName(region);
        string width = DescribeWidth(length);

        long withinRegion = offset & 0x0FFFFFFF;

        // Region 1 is not page-based, and its stride is 256 rather than the 512 of regions 2-6: it holds groups of
        // one shared byte per user, so 256 bytes is enough and an exclusive lock over a whole group is exactly 256
        // wide. Group 0 is the user-slot array the white paper documents (0x10000001..0x100000FF); observed traces
        // also use groups 1 and 5, which the paper does not mention.
        if (region == 0x1)
        {
            long group = withinRegion >> 8;
            long slot = withinRegion & 0xFF;
            string note = group == 0 ? "" : "  <-- undocumented group";

            return length is > 1
                ? $"{kind,-11} group {group,-2} users {slot}..{slot + length.Value - 1,-4} {width}  [0x{withinRegion:X}]{note}"
                : $"{kind,-11} group {group,-2} user {slot,-9} {width}  [0x{withinRegion:X}]{note}";
        }

        long page = withinRegion / LockBytesPerPage;
        long user = withinRegion % LockBytesPerPage;

        // The raw region-relative offset goes in hex too: the page/user split is an inference, and printing what it
        // was derived from is what makes an anomalous value obvious instead of plausible-looking.
        return $"{kind,-11} page {page,-6} user {user,-3} {width}  [0x{withinRegion:X}]";
    }

    private static string DescribeWidth(long? length)
        => length switch
        {
            null => "?",
            1 => "SHARED(1)",
            // The white paper: an exclusive lock takes the first 256 bytes to block and detect shared locks, plus
            // enough beyond that to identify which user holds it.
            >= 256 and <= 512 => $"EXCL({length})",
            _ => $"RANGE({length})",
        };

    /// <summary>Describes a read or write against the database file.</summary>
    public static string DescribeDatabaseIo(long offset, long? length, int pageSize)
    {
        long page = offset / pageSize;
        long inPage = offset % pageSize;

        if (page == 0 && inPage >= CommitByteTableOffset)
        {
            long slot = (inPage - CommitByteTableOffset) / CommitByteWidth;
            return $"commit-byte user {slot}   (page 0 +0x{inPage:X3}, {length} bytes)";
        }

        if (inPage == 0 && length == pageSize)
        {
            return $"page {page,-6} (full)";
        }

        // A single read can span many pages — Access pulls 64 KB at once when opening a database.
        if (inPage == 0 && length > pageSize)
        {
            long lastPage = page + ((length.Value + pageSize - 1) / pageSize) - 1;
            return $"pages {page}..{lastPage}   ({length} bytes)";
        }

        return $"page {page,-6} +{inPage,-5} ({length} bytes)";
    }

    /// <summary>Describes a read or write against the lock file.</summary>
    /// <remarks>
    ///     Reports the record <i>index</i> rather than a user slot, because the mapping between the two is not settled:
    ///     the white paper says slot 1 writes "the first 64 bytes" (implying <c>(slot - 1) * 64</c>) but also that a
    ///     lock at <c>0x10000040</c> writes "starting at 4096 bytes" (implying <c>slot * 64</c>), and those disagree.
    ///     An observed trace has slot 1 writing at offset 0. Left as an index until an experiment settles it.
    /// </remarks>
    public static string DescribeLockFileIo(long offset, long? length)
        => length == UserRecordWidth && offset % UserRecordWidth == 0
            ? $"user record #{offset / UserRecordWidth}   (computer + security name)"
            : $"offset {offset} ({length} bytes)";

    /// <summary>Whether <paramref name="path" /> names a lock file rather than a database.</summary>
    public static bool IsLockFile(string path)
        => path.EndsWith(".laccdb", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path" /> names a database or its lock file.</summary>
    public static bool IsDatabaseOrLockFile(string path)
        => IsLockFile(path)
            || path.EndsWith(".accdb", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".accde", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mdw", StringComparison.OrdinalIgnoreCase);
}
