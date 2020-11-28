using System;
using System.Data;
using System.Data.Common;
using System.Data.Jet;

static internal class LogHelper
{

    internal static void ShowInfo(string info)
    {
        if (!JetConfiguration.ShowSqlStatements)
            return;

        Console.WriteLine(info);
    }

    internal static void ShowInfo(string format, params object[] args)
    {
        if (!JetConfiguration.ShowSqlStatements)
            return;

        Console.WriteLine(format, args);
    }


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
            Console.WriteLine("{0}({1}) = {2}", parameter.ParameterName, parameter.DbType, JetParameterHelper.GetParameterValueToDisplay(parameter));
    }
}