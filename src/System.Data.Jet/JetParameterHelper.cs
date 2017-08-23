using System;
using System.Data.Common;

namespace System.Data.Jet
{
    static internal class JetParameterHelper
    {
        public static string GetParameterValueToDisplay(DbParameter parameter)
        {
            if (parameter.Value == DBNull.Value || parameter.Value == null)
                return "null";
            else if (IsString(parameter))
                return String.Format("'{0}'", parameter.Value);
            else if (IsDateTime(parameter))
                return String.Format("#{0:yyyy-MM-ddTHH:mm:ssZ}#", parameter.Value);
            else if (IsTimeSpan(parameter))
                return String.Format("#{0:c}#", parameter.Value);
            else if (IsGuid(parameter))
                return String.Format("{{{0}}}", parameter.Value);
            else if (parameter.Value is Enum)
                return String.Format("{0}({1})", Convert.ToInt32(parameter.Value), parameter.Value);
            else
                return String.Format("{0}", parameter.Value);

        }

        private static bool IsGuid(DbParameter parameter)
        {
            switch (parameter.DbType)
            {
                case DbType.Guid:
                    return true;
                default:
                    return false;
            }
        }

        public static string GetParameterValue(DbParameter parameter)
        {
            if (parameter.Value == DBNull.Value || parameter.Value == null)
                return "null";
            else if (IsString(parameter))
                return String.Format("'{0}'", ((string)parameter.Value).Replace("'", "''"));
            else if (IsDateTime(parameter))
                return String.Format("#{0:MM/dd/yyyy HH:mm:ss}#", parameter.Value);
            else if (IsTimeSpan(parameter))
                return String.Format("#{0:MM/dd/yyyy HH:mm:ss}#", JetConfiguration.TimeSpanOffset + (TimeSpan)parameter.Value);
            else if (IsGuid(parameter))
                return String.Format("'{0}'", parameter.Value);
            else if (IsNumeric(parameter))
                return String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", Convert.ToInt64(parameter.Value));
            else
                return String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", parameter.Value);
        }

        private static bool IsNumeric(DbParameter parameter)
        {
            switch (parameter.DbType)
            {
                case DbType.Byte:
                case DbType.Int16:
                case DbType.Int32:
                case DbType.Int64:
                case DbType.Object:
                case DbType.SByte:
                case DbType.UInt16:
                case DbType.UInt32:
                case DbType.UInt64:
                    return true;
                default:
                    return false;
            }
        }


        public static bool IsTimeSpan(DbParameter parameter)
        {
            switch (parameter.DbType)
            {
                case DbType.DateTimeOffset:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsDateTime(DbParameter parameter)
        {
            switch (parameter.DbType)
            {
                case DbType.Date:
                case DbType.DateTime:
                case DbType.Time:
                case DbType.DateTime2:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsString(DbParameter parameter)
        {
            switch (parameter.DbType)
            {
                case DbType.AnsiString:
                case DbType.String:
                case DbType.AnsiStringFixedLength:
                case DbType.StringFixedLength:
                    return true;
                default:
                    return false;
            }
        }
    }
}