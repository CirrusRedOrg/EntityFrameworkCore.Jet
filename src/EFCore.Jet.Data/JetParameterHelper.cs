using System;
using System.Data;
using System.Data.Common;
namespace EntityFrameworkCore.Jet.Data
{
    public static class JetParameterHelper
    {
        internal static string GetParameterValueToDisplay(DbParameter parameter)
        {
            if (parameter.Value == DBNull.Value || parameter.Value == null)
                return "null";
            else if (IsString(parameter))
                return String.Format("'{0}'", parameter.Value);
            else if (IsDateTime(parameter))
            {
                if (parameter.Value is TimeSpan)
                    return String.Format("#{0:c}#", parameter.Value);
                else
                    return String.Format("#{0:yyyy-MM-ddTHH:mm:ssZ}#", parameter.Value);
                
            }
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


        internal static bool IsTimeSpan(DbParameter parameter)
        {
            if (!(parameter is DbParameter))
                return false;

            return (parameter as DbParameter).DbType == DbType.Time;
        }

        internal static bool IsDateTime(DbParameter parameter)
        {
            switch (parameter.DbType)
            {
                case DbType.DateTimeOffset:
                case DbType.Date:
                case DbType.DateTime:
                case DbType.Time:
                case DbType.DateTime2:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsString(DbParameter parameter)
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