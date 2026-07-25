using System.Globalization;

namespace JetLockTrace;

/// <summary>One row of a Process Monitor CSV export, reduced to the fields that matter here.</summary>
/// <param name="Operation">e.g. <c>LockFile</c>, <c>ReadFile</c>.</param>
/// <param name="Path">The file the operation was against.</param>
/// <param name="Offset">Byte offset from the <c>Detail</c> column, if it had one.</param>
/// <param name="Length">Byte length from the <c>Detail</c> column, if it had one.</param>
/// <param name="Exclusive">For a lock, whether <c>Detail</c> said <c>Exclusive: True</c>.</param>
public sealed record TraceEvent(string Operation, string Path, long? Offset, long? Length, bool? Exclusive);

/// <summary>Reads a Process Monitor CSV export.</summary>
/// <remarks>
///     ProcMon quotes every field, and the <c>Detail</c> column contains commas inside those quotes
///     (<c>"Offset: 536,905,217, Length: 257"</c>), so the fields have to be split with a real CSV reader rather
///     than by splitting on commas. Numbers carry thousands separators for the same reason.
/// </remarks>
public static class ProcMonCsv
{
    /// <summary>The operations worth decoding; everything else in a trace is noise for this purpose.</summary>
    private static readonly HashSet<string> InterestingOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "LockFile", "UnlockFile", "UnlockFileSingle", "ReadFile", "WriteFile",
    };

    /// <summary>Parses <paramref name="path" />, keeping only lock/unlock/read/write rows.</summary>
    public static IEnumerable<TraceEvent> Read(string path)
    {
        using var reader = new StreamReader(path);

        string[]? header = ReadRecord(reader);
        if (header is null)
        {
            yield break;
        }

        int operationColumn = IndexOf(header, "Operation");
        int pathColumn = IndexOf(header, "Path");
        int detailColumn = IndexOf(header, "Detail");

        if (operationColumn < 0 || pathColumn < 0 || detailColumn < 0)
        {
            throw new InvalidDataException(
                "The CSV has no Operation/Path/Detail columns. Export from Process Monitor with "
                + "File > Save > Comma-Separated Values, leaving the default columns in place.");
        }

        while (ReadRecord(reader) is { } fields)
        {
            if (fields.Length <= detailColumn)
            {
                continue;
            }

            string operation = fields[operationColumn];
            if (!InterestingOperations.Contains(operation))
            {
                continue;
            }

            string detail = fields[detailColumn];

            yield return new TraceEvent(
                operation,
                fields[pathColumn],
                FindNumber(detail, "Offset:"),
                FindNumber(detail, "Length:"),
                detail.Contains("Exclusive: True", StringComparison.OrdinalIgnoreCase)
                    ? true
                    : detail.Contains("Exclusive: False", StringComparison.OrdinalIgnoreCase)
                        ? false
                        : null);
        }
    }

    private static int IndexOf(string[] header, string name)
        => Array.FindIndex(header, h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pulls the number following <paramref name="label" />, e.g. <c>Offset: 536,905,217</c> → 536905217.</summary>
    private static long? FindNumber(string detail, string label)
    {
        int start = detail.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += label.Length;

        // Skip to the first digit, then take digits and thousands separators.
        while (start < detail.Length && !char.IsDigit(detail[start]))
        {
            // A non-digit, non-space before the number means we are looking at a different field.
            if (detail[start] is not (' ' or '\t'))
            {
                return null;
            }

            start++;
        }

        int end = start;
        while (end < detail.Length && (char.IsDigit(detail[end]) || detail[end] == ','))
        {
            end++;
        }

        return end > start
               && long.TryParse(
                   detail.AsSpan(start, end - start),
                   NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture,
                   out long value)
            ? value
            : null;
    }

    /// <summary>Reads one CSV record, honouring quotes and embedded newlines.</summary>
    private static string[]? ReadRecord(StreamReader reader)
    {
        if (reader.EndOfStream)
        {
            return null;
        }

        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;

        while (true)
        {
            int next = reader.Read();

            if (next < 0)
            {
                fields.Add(field.ToString());
                return fields.Count == 1 && fields[0].Length == 0 ? null : fields.ToArray();
            }

            var c = (char)next;

            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (reader.Peek() == '"')
                {
                    reader.Read();
                    field.Append('"');
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    return fields.ToArray();
                default:
                    field.Append(c);
                    break;
            }
        }
    }
}
