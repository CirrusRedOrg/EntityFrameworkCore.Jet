using System;
using System.Data.Common;
using System.Text;

namespace System.Data.Jet
{
    class JetDataReader : DbDataReader
    {
        public JetDataReader(DbDataReader dataReader)
        {
            _wrappedDataReader = dataReader;
        }

        public JetDataReader(DbDataReader dataReader, int topCount, int skipCount)
        {
            _wrappedDataReader = dataReader;
            _topCount = topCount;
            for (int i = 0; i < skipCount; i++)
            {
                _wrappedDataReader.Read();
            }
        }


        private DbDataReader _wrappedDataReader;
        private readonly int _topCount = 0;
        private int _readCount = 0;

        public override void Close()
        {
            _wrappedDataReader.Close();
        }

        public override int Depth
        {
            get { return _wrappedDataReader.Depth; }
        }

        public override int FieldCount
        {
            get { return _wrappedDataReader.FieldCount; }
        }

        public override bool GetBoolean(int ordinal)
        {
            object booleanObject = _wrappedDataReader.GetValue(ordinal);
            if (booleanObject == null)
                throw new InvalidOperationException("Cannot cast null to boolean");
            if (booleanObject is bool)
                return _wrappedDataReader.GetBoolean(ordinal);
            else if (booleanObject is short)
                return ((short)booleanObject) != 0;
            else
                throw new InvalidOperationException(string.Format("Cannot convert {0} to boolean", booleanObject.GetType()));
        }

        public override byte GetByte(int ordinal)
        {
            return Convert.ToByte(_wrappedDataReader.GetValue(ordinal));
        }

        public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length)
        {
            return _wrappedDataReader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        }

        public override char GetChar(int ordinal)
        {
            return _wrappedDataReader.GetChar(ordinal);
        }

        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
        {
            return _wrappedDataReader.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        }

        public override string GetDataTypeName(int ordinal)
        {
            return _wrappedDataReader.GetDataTypeName(ordinal);
        }

        public override DateTime GetDateTime(int ordinal)
        {
            return _wrappedDataReader.GetDateTime(ordinal);
        }

        public virtual TimeSpan GetTimeSpan(int ordinal)
        {
            TimeSpan timeSpan = GetDateTime(ordinal) - JetConfiguration.TimeSpanOffset;

            return timeSpan;
        }

        public virtual DateTimeOffset GetDateTimeOffset(int ordinal)
        {
            return GetDateTime(ordinal);
        }


        public override decimal GetDecimal(int ordinal)
        {
            return Convert.ToDecimal(_wrappedDataReader.GetValue(ordinal));
        }

        public override double GetDouble(int ordinal)
        {
            object value = _wrappedDataReader.GetValue(ordinal);
            if (value is string)
            {
                byte[] buffer = Encoding.Unicode.GetBytes((string)value);
                double doubleValue = BitConverter.ToDouble(buffer, 0);
                return doubleValue;
            }
            else
                return Convert.ToDouble(_wrappedDataReader.GetValue(ordinal));
        }

        public override System.Collections.IEnumerator GetEnumerator()
        {
            return _wrappedDataReader.GetEnumerator();
        }

        public override Type GetFieldType(int ordinal)
        {
            return _wrappedDataReader.GetFieldType(ordinal);
        }

        public override float GetFloat(int ordinal)
        {
            object value = _wrappedDataReader.GetValue(ordinal);
            if (value is string)
            {
                byte[] buffer = Encoding.Unicode.GetBytes((string)value);
                float singleValue = BitConverter.ToSingle(buffer, 0);
                return singleValue;
            }
            else
                return Convert.ToSingle(_wrappedDataReader.GetValue(ordinal));
        }

        public override Guid GetGuid(int ordinal)
        {
            object value = _wrappedDataReader.GetValue(ordinal);
            if (value is byte[])
                return new Guid((byte[])value);
            else
                return _wrappedDataReader.GetGuid(ordinal);
        }

        public override short GetInt16(int ordinal)
        {
            return Convert.ToInt16(_wrappedDataReader.GetValue(ordinal));
        }

        public override int GetInt32(int ordinal)
        {
            object value = _wrappedDataReader.GetValue(ordinal);
            if (value is string)
            {
                byte[] buffer = Encoding.Unicode.GetBytes((string)value);
                int intValue = BitConverter.ToInt32(buffer, 0);
                return intValue;
            }
            else
                return Convert.ToInt32(value);
        }

        public override long GetInt64(int ordinal)
        {
            object value = _wrappedDataReader.GetValue(ordinal);
            if (value is string)
            {
                byte[] buffer = Encoding.Unicode.GetBytes((string)value);
                long longValue = BitConverter.ToInt64(buffer, 0);
                return longValue;
            }
            else
                return Convert.ToInt64(value);
        }

        public override string GetName(int ordinal)
        {
            return _wrappedDataReader.GetName(ordinal);
        }

        public override int GetOrdinal(string name)
        {
            return _wrappedDataReader.GetOrdinal(name);
        }

        public override System.Data.DataTable GetSchemaTable()
        {
            return _wrappedDataReader.GetSchemaTable();
        }

        public override string GetString(int ordinal)
        {
            return _wrappedDataReader.GetString(ordinal);
        }

        public override object GetValue(int ordinal)
        {
            return _wrappedDataReader.GetValue(ordinal);
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            if (typeof(T) == typeof(TimeSpan))
                return (T)(object)GetTimeSpan(ordinal);
            else if (typeof(T) == typeof(DateTimeOffset))
                return (T)(object)GetDateTimeOffset(ordinal);
            else
                return base.GetFieldValue<T>(ordinal);
        }

        public override int GetValues(object[] values)
        {
            return _wrappedDataReader.GetValues(values);
        }

        public override bool HasRows
        {
            get { return _wrappedDataReader.HasRows; }
        }

        public override bool IsClosed
        {
            get { return _wrappedDataReader.IsClosed; }
        }

        public override bool IsDBNull(int ordinal)
        {
            if (_wrappedDataReader.IsDBNull(ordinal))
                return true;
            if (JetConfiguration.IntegerNullValue != null && ((int)JetConfiguration.IntegerNullValue).Equals(_wrappedDataReader.GetValue(ordinal)))
                return true;
            return false;
        }

        public override bool NextResult()
        {
            return _wrappedDataReader.NextResult();
        }

        public override bool Read()
        {
            _readCount++;
            if (_topCount != 0 && _readCount > _topCount)
                return false;

            return _wrappedDataReader.Read();
        }

        public override int RecordsAffected
        {
            get { return _wrappedDataReader.RecordsAffected; }
        }

        public override object this[string name]
        {
            get { return _wrappedDataReader[name]; }
        }

        public override object this[int ordinal]
        {
            get { return _wrappedDataReader[ordinal]; }
        }
    }
}
