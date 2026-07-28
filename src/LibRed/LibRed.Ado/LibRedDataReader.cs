using System.Collections;
using System.Data.Common;
using LibRed.Engine.Execution;

namespace LibRed.Data;

/// <summary>Forward-only reader projecting an engine <see cref="ResultSet"/> as ADO.NET rows.</summary>
public sealed class LibRedDataReader : DbDataReader
{
    private readonly ResultSet _result;
    private readonly IEnumerator<object?[]> _rows;
    private readonly int _recordsAffected;
    private object?[] _current = [];
    private bool _pendingFirst;
    private bool _hadRows;
    private bool _closed;

    /// <param name="recordsAffected">Rows affected for a DML command; -1 for a query (ADO convention).</param>
    internal LibRedDataReader(ResultSet result, int recordsAffected = -1)
    {
        _result = result;
        _rows = result.Rows.GetEnumerator();
        _recordsAffected = recordsAffected;

        // Buffer the first row eagerly so column types (GetFieldType/GetDataTypeName) are available
        // before the first Read — EF's BufferedDataReader reads that metadata before reading any rows.
        if (_rows.MoveNext())
        {
            _current = _rows.Current;
            _pendingFirst = true;
            _hadRows = true;
        }
    }

    public override int FieldCount => _result.ColumnNames.Count;
    public override int Depth => 0;
    public override bool HasRows => _hadRows;
    public override bool IsClosed => _closed;
    public override int RecordsAffected => _recordsAffected;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_pendingFirst) { _pendingFirst = false; return true; } // yield the pre-buffered first row
        if (!_rows.MoveNext()) return false;
        _current = _rows.Current;
        return true;
    }

    public override bool NextResult() => false;

    public override string GetName(int ordinal) => _result.ColumnNames[ordinal];

    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < _result.ColumnNames.Count; i++)
            if (string.Equals(_result.ColumnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new IndexOutOfRangeException(name);
    }

    public override object GetValue(int ordinal) => _current[ordinal] ?? DBNull.Value;

    /// <summary>The OLE epoch (1899-12-30) — Jet stores TimeSpan/TimeOnly as an offset from it.</summary>
    private static readonly DateTime OleEpoch = new(1899, 12, 30);

    /// <summary>
    /// Typed accessor EF Core uses. Jet has no TimeSpan/DateOnly/TimeOnly/DateTimeOffset type — they are
    /// all stored in a DateTime column — so convert a stored <see cref="DateTime"/> back when one of those
    /// is requested. For <see cref="DateTimeOffset"/> there is no offset on disk (the mapping strips it and
    /// stores UTC on the way in), so it is read back at offset zero.
    /// <para>default(DateTime) (Ticks 0 / 0001-01-01) is below Jet's OLE date floor, so the write path (the
    /// inherited EFCore.Jet DateTime mapping) collapses it onto the epoch OA 0. This reverses it on the way out
    /// — the epoch reads back as default — matching EFCore.Jet's JetDataReader. Scoped to DateTime/DateTimeOffset:
    /// a TimeSpan/TimeOnly at the epoch is a legitimate midnight, handled above.</para>
    /// </summary>
    public override T GetFieldValue<T>(int ordinal)
    {
        if (_current[ordinal] is DateTime dt)
        {
            if (typeof(T) == typeof(TimeSpan)) return (T)(object)(dt - OleEpoch);
            if (typeof(T) == typeof(DateOnly)) return (T)(object)DateOnly.FromDateTime(dt);
            if (typeof(T) == typeof(TimeOnly)) return (T)(object)TimeOnly.FromDateTime(dt);
            if (typeof(T) == typeof(DateTime)) return (T)(object)(dt == OleEpoch ? default : dt);
            if (typeof(T) == typeof(DateTimeOffset)) return (T)(object)new DateTimeOffset(dt == OleEpoch ? default : dt, TimeSpan.Zero);
        }
        return base.GetFieldValue<T>(ordinal);
    }

    /// <summary>The stored epoch is the on-disk home of default(DateTime) — see <see cref="GetFieldValue{T}"/>.</summary>
    private static DateTime FromStored(DateTime dt) => dt == OleEpoch ? default : dt;

    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public override bool IsDBNull(int ordinal) => _current[ordinal] is null;

    public override Type GetFieldType(int ordinal)
    {
        Type declared = _result.ColumnTypes[ordinal];
        return declared != typeof(object)
            ? declared
            : ordinal < _current.Length && _current[ordinal] is { } value ? value.GetType() : typeof(object);
    }

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override bool GetBoolean(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is short) return Convert.ToBoolean(value);
        return (bool)value;
    }
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => FromStored((DateTime)GetValue(ordinal));
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

    public override long GetInt64(int ordinal)
    {
        var result = GetValue(ordinal);
        if (result is long l)
        {
            return l;
        }

        try
        {
            return Convert.ToInt64(result);
        }
        catch (Exception)
        {
            // ignored
        }

        return (long)result;

    }
    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var source = (byte[])GetValue(ordinal);
        if (buffer is null) return source.Length;

        ValidateCopyArguments(dataOffset, buffer.Length, bufferOffset, length);
        if (dataOffset >= source.Length) return 0;

        int copy = Math.Min(length, source.Length - checked((int)dataOffset));
        source.AsSpan(checked((int)dataOffset), copy).CopyTo(buffer.AsSpan(bufferOffset, copy));
        return copy;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var source = GetString(ordinal);
        if (buffer is null) return source.Length;

        ValidateCopyArguments(dataOffset, buffer.Length, bufferOffset, length);
        if (dataOffset >= source.Length) return 0;

        int copy = Math.Min(length, source.Length - checked((int)dataOffset));
        source.AsSpan(checked((int)dataOffset), copy).CopyTo(buffer.AsSpan(bufferOffset, copy));
        return copy;
    }

    private static void ValidateCopyArguments(long dataOffset, int bufferLength, int bufferOffset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dataOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(bufferOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (bufferOffset > bufferLength || length > bufferLength - bufferOffset)
            throw new ArgumentException("The requested range does not fit in the destination buffer.");
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    public override void Close() => _closed = true;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _rows.Dispose();
        Close();
        base.Dispose(disposing);
    }
}
