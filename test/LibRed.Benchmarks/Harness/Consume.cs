using System.Data.Common;
using LibRed.Engine.Execution;

namespace LibRed.Benchmarks.Harness;

/// <summary>
/// Drains a result and folds it into a checksum.
/// </summary>
/// <remarks>
/// This is not a nicety. <see cref="ResultSet.Rows"/> is lazily evaluated — enumerating it is what drives the
/// cursors — so a benchmark that returns the <see cref="ResultSet"/> measures plan construction and nothing
/// else, and reports a query over a million rows as a few hundred nanoseconds. Every read benchmark must
/// return the checksum, which also stops the JIT eliminating the work as dead.
/// </remarks>
public static class Consume
{
    /// <summary>Enumerates every row and column, returning a value derived from the data so nothing can be
    /// optimised away.</summary>
    public static long Drain(ResultSet result)
    {
        long checksum = 0;
        foreach (object?[] row in result.Rows)
        {
            checksum += row.Length;
            for (int i = 0; i < row.Length; i++)
                if (row[i] is { } value)
                    checksum ^= value.GetHashCode();
        }

        return checksum;
    }

    /// <summary>The same drain over the storage layer's raw row sequence — a cursor or an index seek, with no
    /// SQL above it.</summary>
    public static long Drain(IEnumerable<object?[]> rows)
    {
        long checksum = 0;
        foreach (object?[] row in rows)
        {
            checksum += row.Length;
            for (int i = 0; i < row.Length; i++)
                if (row[i] is { } value)
                    checksum ^= value.GetHashCode();
        }

        return checksum;
    }

    /// <summary>The same drain over an ADO reader, so the ACE side of the head-to-head does the same amount
    /// of work as the LibRed side — reading every column, not just counting rows.</summary>
    public static long Drain(DbDataReader reader)
    {
        long checksum = 0;
        int fields = reader.FieldCount;
        var buffer = new object[fields];
        while (reader.Read())
        {
            reader.GetValues(buffer);
            checksum += fields;
            for (int i = 0; i < fields; i++)
                if (buffer[i] is { } value && value is not DBNull)
                    checksum ^= value.GetHashCode();
        }

        return checksum;
    }
}
