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
    private bool _closed;

    /// <param name="recordsAffected">Rows affected for a DML command; -1 for a query (ADO convention).</param>
    internal LibRedDataReader(ResultSet result, int recordsAffected = -1)
    {
        _result = result;
        _rows = result.Rows.GetEnumerator();
        _recordsAffected = recordsAffected;
    }

    public override int FieldCount => _result.ColumnNames.Count;
    public override int Depth => 0;
    public override bool HasRows => _result.Rows.Any();
    public override bool IsClosed => _closed;
    public override int RecordsAffected => _recordsAffected;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
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

    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public override bool IsDBNull(int ordinal) => _current[ordinal] is null;

    public override Type GetFieldType(int ordinal) => _current[ordinal]?.GetType() ?? typeof(object);

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var source = (byte[])GetValue(ordinal);
        if (buffer is null) return source.Length;
        long copy = Math.Min(length, source.Length - dataOffset);
        Array.Copy(source, dataOffset, buffer, bufferOffset, copy);
        return copy;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var source = GetString(ordinal).ToCharArray();
        if (buffer is null) return source.Length;
        long copy = Math.Min(length, source.Length - dataOffset);
        Array.Copy(source, dataOffset, buffer, bufferOffset, copy);
        return copy;
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
