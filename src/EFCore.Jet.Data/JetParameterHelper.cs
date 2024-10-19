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
            return parameter.DbType switch
            {
                DbType.Guid => true,
                _ => false
            };
        }

        private static bool IsNumeric(DbParameter parameter)
        {
            return parameter.DbType switch
            {
                DbType.Byte or DbType.Int16 or DbType.Int32 or DbType.Int64 or DbType.Object or DbType.SByte
                    or DbType.UInt16 or DbType.UInt32 or DbType.UInt64 => true,
                _ => false
            };
        }


        internal static bool IsTimeSpan(DbParameter parameter)
        {
            if (!(parameter is DbParameter))
                return false;

            return (parameter as DbParameter).DbType == DbType.Time;
        }

        internal static bool IsDateTime(DbParameter parameter)
        {
            return parameter.DbType switch
            {
                DbType.DateTimeOffset or DbType.Date or DbType.DateTime or DbType.Time or DbType.DateTime2 => true,
                _ => false
            };
        }

        internal static bool IsString(DbParameter parameter)
        {
            return parameter.DbType switch
            {
                DbType.AnsiString or DbType.String or DbType.AnsiStringFixedLength or DbType.StringFixedLength => true,
                _ => false
            };
        }
    }
}