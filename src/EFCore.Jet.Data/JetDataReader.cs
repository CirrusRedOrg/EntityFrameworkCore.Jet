using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;

namespace EntityFrameworkCore.Jet.Data
{
    internal class JetDataReader : DbDataReader
    {
#if DEBUG
        private static int _activeObjectsCount;
#endif

        public JetDataReader(DbDataReader dataReader)
        {
#if DEBUG
            Interlocked.Increment(ref _activeObjectsCount);
#endif
            _wrappedDataReader = dataReader;
        }
        
        public JetDataReader(DbDataReader dataReader, int skipCount)
            : this(dataReader)
        {
            var i = 0;
            while (i < skipCount && _wrappedDataReader.Read())
            {
                i++;
            }
        }

        private readonly DbDataReader _wrappedDataReader;

        public override void Close()
        {
            _wrappedDataReader.Close();
#if DEBUG
            Interlocked.Decrement(ref _activeObjectsCount);
#endif
        }

        public override int Depth
            => _wrappedDataReader.Depth;

        public override int FieldCount
            => _wrappedDataReader.FieldCount;

        public override bool GetBoolean(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && 
                Convert.IsDBNull(value))
            {
                return default;
            }

            if (value is bool boolValue)
                return boolValue;
            if (value is sbyte sbyteValue)
                return sbyteValue != 0;
            if (value is byte byteValue)
                return byteValue != 0;
            if (value is short shortValue)
                return shortValue != 0;
            if (value is ushort ushortValue)
                return ushortValue != 0;
            if (value is int intValue)
                return intValue != 0;
            if (value is uint uintValue)
                return uintValue != 0;
            if (value is long longValue)
                return longValue != 0;
            if (value is ulong ulongValue)
                return ulongValue != 0;
            if (value is decimal decimalValue)
                return decimalValue != 0;
            
            return (bool) value;
        }

        public override byte GetByte(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && 
                Convert.IsDBNull(value))
            {
                return default;
            }
            
            if (value is byte byteValue)
                return byteValue;
            if (value is sbyte sbyteValue)
                return checked((byte) sbyteValue);
            if (value is short shortValue)
                return checked((byte) shortValue);
            if (value is ushort ushortValue)
                return checked((byte) ushortValue);
            if (value is int intValue)
                return checked((byte) intValue);
            if (value is uint uintValue)
                return checked((byte) uintValue);
            if (value is long longValue)
                return checked((byte) longValue);
            if (value is ulong ulongValue)
                return checked((byte) ulongValue);
            if (value is decimal decimalValue)
                return (byte) decimalValue;
            
            return (byte) value;
        }

        public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length)
            => JetConfiguration.UseDefaultValueOnDBNullConversionError &&
               _wrappedDataReader.IsDBNull(ordinal)
                ? 0
                : _wrappedDataReader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

        public override char GetChar(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? default
                : (char) value;
        }

        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
        {
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                _wrappedDataReader.IsDBNull(ordinal))
            {
                return 0;
            }
            
            return _wrappedDataReader.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        }

        public override string GetDataTypeName(int ordinal)
            => _wrappedDataReader.GetDataTypeName(ordinal);

        public override DateTime GetDateTime(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? default
                : (DateTime) value;
        }

        public virtual TimeSpan GetTimeSpan(int ordinal)
            => GetDateTime(ordinal) - JetConfiguration.TimeSpanOffset;

        public virtual DateTimeOffset GetDateTimeOffset(int ordinal)
            => GetDateTime(ordinal);

        public override decimal GetDecimal(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? default
                : Convert.ToDecimal(value);
        }

        public override double GetDouble(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? default
                : Convert.ToDouble(value);
        }

        public override System.Collections.IEnumerator GetEnumerator()
            => _wrappedDataReader.GetEnumerator();

        public override Type GetFieldType(int ordinal)
            => _wrappedDataReader.GetFieldType(ordinal);

        public override float GetFloat(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? default
                : Convert.ToSingle(value);
        }

        public override Guid GetGuid(int ordinal)
        {
            // Fix for discussion https://jetentityframeworkprovider.codeplex.com/discussions/647028
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? Guid.Empty
                : value is byte[] bytes
                    ? new Guid(bytes)
                    : (Guid)value;
        }

        public override short GetInt16(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? default
                : Convert.ToInt16(value);
        }

        public override int GetInt32(int ordinal)
        {
            // Fix for discussion https://jetentityframeworkprovider.codeplex.com/discussions/647028
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? default
                : value is string stringValue
                    ? BitConverter.ToInt32(Encoding.Unicode.GetBytes(stringValue), 0)
                    : Convert.ToInt32(value);
        }

        public override long GetInt64(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? default
                : Convert.ToInt64(value);
        }

        public override string GetName(int ordinal)
            => _wrappedDataReader.GetName(ordinal);

        public override int GetOrdinal(string name)
            => _wrappedDataReader.GetOrdinal(name);
        
        public override DataTable GetSchemaTable()
            => _wrappedDataReader.GetSchemaTable();

        public override string GetString(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            return JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                   Convert.IsDBNull(value)
                ? string.Empty
                : (string) value;
        }

        public override object GetValue(int ordinal)
        {
            var fieldType = GetFieldType(ordinal);
            
            if (fieldType == typeof(bool))
                return GetBoolean(ordinal);
            if (fieldType == typeof(byte))
                return GetByte(ordinal);
            // if (fieldType == typeof(sbyte))
            //     return GetSByte(ordinal);
            if (fieldType == typeof(short))
                return GetInt16(ordinal);
            // if (fieldType == typeof(ushort))
            //     return GetUInt16(ordinal);
            if (fieldType == typeof(int))
                return GetInt32(ordinal);
            // if (fieldType == typeof(uint))
            //     return GetUInt32(ordinal);
            if (fieldType == typeof(long))
                return GetInt64(ordinal);
            // if (fieldType == typeof(ulong))
            //     return GetUInt64(ordinal);
            if (fieldType == typeof(char))
                return GetChar(ordinal);
            if (fieldType == typeof(decimal))
                return GetDecimal(ordinal);
            if (fieldType == typeof(double))
                return GetDouble(ordinal);
            if (fieldType == typeof(float))
                return GetFloat(ordinal);
            if (fieldType == typeof(string))
                return GetString(ordinal);
            if (fieldType == typeof(DateTime))
                return GetDateTime(ordinal);
            if (fieldType == typeof(DateTimeOffset))
                return GetDateTimeOffset(ordinal);
            if (fieldType == typeof(Guid))
                return GetGuid(ordinal);
            if (fieldType == typeof(Stream))
                return GetStream(ordinal);
            if (fieldType == typeof(TextReader) || fieldType == typeof(StringReader))
                return GetTextReader(ordinal);
            if (fieldType == typeof(TimeSpan))
                return GetTimeSpan(ordinal);

            return _wrappedDataReader.GetValue(ordinal);
        }

        public override int GetValues(object[] values)
        {
            var count = Math.Min((values ?? throw new ArgumentNullException(nameof(values))).Length, FieldCount);
            
            for (var i = 0; i < count; i++)
                values[i] = GetValue(i);
            
            return count;
        }

        public override bool HasRows
            => _wrappedDataReader.HasRows;

        public override bool IsClosed
            => _wrappedDataReader.IsClosed;

        public override bool IsDBNull(int ordinal)
            => _wrappedDataReader.IsDBNull(ordinal) ||
               JetConfiguration.IntegerNullValue != null && ((int) JetConfiguration.IntegerNullValue).Equals(GetValue(ordinal));

        public override bool NextResult()
            => _wrappedDataReader.NextResult();

        public override bool Read()
            => _wrappedDataReader.Read();

        public override int RecordsAffected
            => _wrappedDataReader.RecordsAffected;

        public override object this[string name]
            => _wrappedDataReader[name];

        public override object this[int ordinal]
            => _wrappedDataReader[ordinal];
    }
}