using System.Data.Common;
using System.Text;
using System.Threading;

namespace System.Data.Jet
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
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError)
            {
                if (value is DBNull) return false;
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
            var value = GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError)
            {
                if (value is DBNull) return 0;
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
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return 0;
            }
            return _wrappedDataReader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        }

        public override char GetChar(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return (char)0;
            }
            return (char) value;
        }

        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
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
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified);
            }
            return _wrappedDataReader.GetDateTime(ordinal);
        }

        public virtual TimeSpan GetTimeSpan(int ordinal)
            => GetDateTime(ordinal) - JetConfiguration.TimeSpanOffset;

        public virtual DateTimeOffset GetDateTimeOffset(int ordinal)
            => GetDateTime(ordinal);

        public override decimal GetDecimal(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return 0;
            }
            return Convert.ToDecimal(value);
        }

        public override double GetDouble(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return 0;
            }
            return Convert.ToDouble(value);
        }

        public override System.Collections.IEnumerator GetEnumerator()
            => _wrappedDataReader.GetEnumerator();

        public override Type GetFieldType(int ordinal)
            => _wrappedDataReader.GetFieldType(ordinal);

        public override float GetFloat(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return 0;
            }
            return Convert.ToSingle(value);
        }

        public override Guid GetGuid(int ordinal)
        {
            // Fix for discussion https://jetentityframeworkprovider.codeplex.com/discussions/647028
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return Guid.Empty;
            }
            if (value is byte[])
                return new Guid((byte[]) value);
            else
                return _wrappedDataReader.GetGuid(ordinal);
        }

        public override short GetInt16(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return 0;
            }
            return Convert.ToInt16(value);
        }

        public override int GetInt32(int ordinal)
        {
            // Fix for discussion https://jetentityframeworkprovider.codeplex.com/discussions/647028
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return 0;
            }
            else if (value is string)
            {
                var buffer = Encoding.Unicode.GetBytes((string)value);
                var intValue = BitConverter.ToInt32(buffer, 0);
                return intValue;
            }
            else
                return Convert.ToInt32(value);
        }

        public override long GetInt64(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return 0;
            }
            return Convert.ToInt64(value);
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
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError && value is DBNull)
            {
                return "";
            }
            return _wrappedDataReader.GetString(ordinal);
        }

        public override object GetValue(int ordinal)
            => _wrappedDataReader.GetValue(ordinal);

        public override T GetFieldValue<T>(int ordinal)
        {
            if (typeof(T) == typeof(TimeSpan))
                return (T) (object) GetTimeSpan(ordinal);
            else if (typeof(T) == typeof(DateTimeOffset))
                return (T) (object) GetDateTimeOffset(ordinal);
            else
                return base.GetFieldValue<T>(ordinal);
        }

        public override int GetValues(object[] values)
            => _wrappedDataReader.GetValues(values);

        public override bool HasRows
            => _wrappedDataReader.HasRows;

        public override bool IsClosed
            => _wrappedDataReader.IsClosed;

        public override bool IsDBNull(int ordinal)
        {
            if (_wrappedDataReader.IsDBNull(ordinal))
                return true;
            if (JetConfiguration.IntegerNullValue != null && ((int) JetConfiguration.IntegerNullValue).Equals(_wrappedDataReader.GetValue(ordinal)))
                return true;
            return false;
        }

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