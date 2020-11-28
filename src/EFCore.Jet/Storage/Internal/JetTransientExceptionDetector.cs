// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Data;
using JetBrains.Annotations;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     Detects the exceptions caused by Jet transient failures.
    /// </summary>
    public static class JetTransientExceptionDetector
    {
        public static bool ShouldRetryOn([NotNull] Exception ex)
        {
            DataAccessProviderType dataAccessProviderType;

            var exceptionFullName = ex.GetType().FullName;
            
            if (exceptionFullName == "System.Data.OleDb.OleDbException")
                dataAccessProviderType = DataAccessProviderType.OleDb;
            else if (exceptionFullName == "System.Data.Odbc.OdbcException")
                dataAccessProviderType = DataAccessProviderType.Odbc;
            else
                return false;

            dynamic sqlException = ex;
            foreach (var err in sqlException.Errors)
            {
                // TODO: Check additional ACE/Jet Errors
                switch (err.NativeError)
                {
                    // ODBC Error Code: -1311 [HY001]
                    // [Microsoft][ODBC Microsoft Access Driver] Cannot open any more tables.
                    // If too many commands get executed in short succession, ACE/Jet can run out of table handles.
                    // This can happen despite proper disposal of OdbcCommand and OdbcDataReader objects.
                    // Waiting for a couple of milliseconds will give ACE/Jet enough time to catch up.
                    case -1311 when dataAccessProviderType == DataAccessProviderType.Odbc:
                        return true;
                }

                return false;
            }

            if (ex is TimeoutException)
            {
                return true;
            }

            return false;
        }
    }
}