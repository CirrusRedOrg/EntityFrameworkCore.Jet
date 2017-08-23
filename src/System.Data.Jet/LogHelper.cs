using System;
using System.Data;
using System.Data.Common;
using System.Data.Jet;

static internal class LogHelper
{
    internal static void ShowCommandHeader(string caller)
    {
        if (!JetConfiguration.ShowSqlStatements)
            return;

        Console.WriteLine("{0}==========", caller);
    }

    internal static void ShowCommandText(string caller, DbCommand command)
    {
        if (!JetConfiguration.ShowSqlStatements)
            return;

        LogHelper.ShowCommandHeader(caller);

        Console.WriteLine("{0}", command.CommandText);
        foreach (DbParameter parameter in command.Parameters)
            Console.WriteLine("{0} = {1}", parameter.ParameterName, GetParameterValue(parameter));
    }

    private static string GetParameterValue(DbParameter parameter)
    {
        if (parameter.Value == DBNull.Value || parameter.Value == null)
            return "null";
        else if (IsString(parameter))
            return String.Format("'{0}'", parameter.Value);
        else if (IsDateTime(parameter))
            return String.Format("#{0:yyyy-MM-ddTHH:mm:ssZ}#", parameter.Value);
        else if (IsTimeSpan(parameter))
            return String.Format("#{0:c}#", parameter.Value);
        else
            return String.Format("{0}", parameter.Value);
    }

    private static bool IsTimeSpan(DbParameter parameter)
    {
        switch (parameter.DbType)
        {
            case DbType.DateTimeOffset:
                return true;
            default:
                return false;
        }
    }

    private static bool IsDateTime(DbParameter parameter)
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

    private static bool IsString(DbParameter parameter)
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