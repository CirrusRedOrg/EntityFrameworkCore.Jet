using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
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
            if (value is string stringValue)
                return stringValue != "0";

            return (bool)value;
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
            if (value is byte[] byteArrayValue)
                return byteArrayValue[0];
            if (value is sbyte sbyteValue)
                return checked((byte)sbyteValue);
            if (value is short shortValue)
                return checked((byte)shortValue);
            if (value is ushort ushortValue)
                return checked((byte)ushortValue);
            if (value is int intValue)
                return checked((byte)intValue);
            if (value is uint uintValue)
                return checked((byte)uintValue);
            if (value is long longValue)
                return checked((byte)longValue);
            if (value is ulong ulongValue)
                return checked((byte)ulongValue);
            if (value is decimal decimalValue)
                return (byte)decimalValue;
            if (value is string stringvalue)
            {
                if (byte.TryParse(stringvalue, out var result))
                {
                    return result;
                }
                var bt = Encoding.Unicode.GetBytes(stringvalue);
                if (bt.Length > 0) return bt[0];
            }

            return (byte)value;
        }

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
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
                : (char)value;
        }

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
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
            // Some/all ODBC/OLE DB methods don't support returning fractions of seconds.
            // Since DATETIME values are really just DOUBLE values internally in Jet, we explicitly convert those vales
            // to DOUBLE in the most outer SELECT projections as a workaround.
            var value = _wrappedDataReader.GetValue(ordinal);

            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
                return default;

            if (value is double doubleValue)
            {
                // Round to milliseconds.
                return new DateTime(
                    JetConfiguration.TimeSpanOffset.Ticks +
                    (long)(Math.Round(
                                (decimal)(long)((decimal)doubleValue * TimeSpan.TicksPerDay) /
                                TimeSpan.TicksPerMillisecond,
                                0,
                                MidpointRounding.AwayFromZero) *
                            TimeSpan.TicksPerMillisecond));
            }
            //The 0/default value for a DateTime is 30/12/1899 00:00:00
            //We normally translate 1/01/0001 00:00:00 to 30/12/1899 00:00:00 when saving to the database so this is just the reverse
            if (value is DateTime dateTimeValue && dateTimeValue == JetConfiguration.TimeSpanOffset)
            {
                return default;
            }
            return (DateTime)value;
        }

        public DateOnly GetDateOnly(int ordinal)
        {
            var value = GetDateTime(ordinal);
            return DateOnly.FromDateTime(value);
        }

        public TimeOnly GetTimeOnly(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);

            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
                return default;

            if (value is DateTime dateTime)
            {
                return TimeOnly.FromDateTime(dateTime);
            }
            return (TimeOnly)value;
        }

        public virtual TimeSpan GetTimeSpan(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);

            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
                return default;

            if (value is DateTime dateTime)
            {
                return TimeSpan.FromTicks(dateTime.Ticks - JetConfiguration.TimeSpanOffset.Ticks);
            }
            return (TimeSpan)value;
        }

        public virtual DateTimeOffset GetDateTimeOffset(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (value is String stringValue)
            {
                return DateTimeOffset.Parse(stringValue, null, DateTimeStyles.RoundtripKind);
            }
            else if (value is DateTime dateTimeValue && dateTimeValue == JetConfiguration.TimeSpanOffset)
            {
                return default;
            }
            else if (value is DateTime dateTime)
            {
                return new DateTimeOffset(dateTime, TimeSpan.Zero);
            }

            return (DateTimeOffset)value;
        }

        public override decimal GetDecimal(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
            {
                return default;
            }

            try
            {
                return Convert.ToDecimal(value);
            }
            catch
            {
                return (decimal)value;
            }
        }

        public override double GetDouble(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
            {
                return default;
            }

            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return (double)value;
            }
        }

        public override System.Collections.IEnumerator GetEnumerator()
            => _wrappedDataReader.GetEnumerator();

        public override Type GetFieldType(int ordinal)
            => _wrappedDataReader.GetFieldType(ordinal);

        public override float GetFloat(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
            {
                return default;
            }

            try
            {
                return Convert.ToSingle(value);
            }
            catch
            {
                return (float)value;
            }
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
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
            {
                return default;
            }

            try
            {
                return Convert.ToInt16(value);
            }
            catch
            {
                if (value is string stringvalue)
                {
                    var bt = Encoding.Unicode.GetBytes(stringvalue);
                    switch (bt.Length)
                    {
                        case 2: return BitConverter.ToInt16(bt, 0);
                    }
                }
            }
            return (short)value;
        }

        public override int GetInt32(int ordinal)
        {

            // Fix for discussion https://jetentityframeworkprovider.codeplex.com/discussions/647028
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
            {
                return default;
            }
            //We sometimes need to do some conversions due to how Jet handles some types
            //Some functions seem to return a double when an int was passed in (e.g. SUM)
            //If we get a numeric type returned it is easy to convert that to the required int
            //However there are some bugs/weird behaviour when you get to UNION queries
            //especially when one of the columns is NULL being combined with another query
            //where the data type is an int or possibly string
            //Someetimes we get back the number in a string e.g. "1"
            //or we can get back what is claimed as a string but is better being treated as a byte array e.g "\u0003\0"
            //In the first instance a standard conversion is desirable and works
            //In the second instance we need to get the bytes and convert them to an int
            //Note also that there are some tests (bad_data_error_handling) that expect an exception to be thrown
            //These purposely create a query that selects a text column and names it into a name that is meant to produce an int
            //The text e.g. "Chai" should not be attempted to convert to bytes and then to an int but should throw an exception
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                if (value is string stringvalue)
                {
                    var bt = Encoding.Unicode.GetBytes(stringvalue);
                    switch (bt.Length)
                    {
                        case 2: return BitConverter.ToInt16(bt, 0);
                        case 4: return BitConverter.ToInt32(bt, 0);
                    }
                }
            }
            return (int)value;
        }

        public override long GetInt64(int ordinal)
        {
            var value = _wrappedDataReader.GetValue(ordinal);
            if (JetConfiguration.UseDefaultValueOnDBNullConversionError &&
                Convert.IsDBNull(value))
            {
                return default;
            }

            try
            {
                return Convert.ToInt64(value);
            }
            catch
            {
                if (value is string stringvalue)
                {
                    var bt = Encoding.Unicode.GetBytes(stringvalue);
                    switch (bt.Length)
                    {
                        case 2: return BitConverter.ToInt16(bt, 0);
                        case 4: return BitConverter.ToInt32(bt, 0);
                        case 8: return BitConverter.ToInt64(bt, 0);
                    }
                }
            }

            return (long)value;
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
                : (string)value;
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
               JetConfiguration.IntegerNullValue != null && ((int)JetConfiguration.IntegerNullValue).Equals(GetValue(ordinal));

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

        public override T GetFieldValue<T>(int ordinal)
        {
            if (typeof(T) == typeof(DateTime))
            {
                return (T)(object)GetDateTime(ordinal);
            }
            if (typeof(T) == typeof(DateOnly))
            {
                return (T)(object)GetDateOnly(ordinal);
            }
            if (typeof(T) == typeof(TimeOnly))
            {
                return (T)(object)GetTimeOnly(ordinal);
            }
            if (typeof(T) == typeof(TimeSpan))
            {
                return (T)(object)GetTimeSpan(ordinal);
            }
            if (typeof(T) == typeof(DateTimeOffset))
            {
                return (T)(object)GetDateTimeOffset(ordinal);
            }

            return (T)GetValue(ordinal);
        }
    }
}