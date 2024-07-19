// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Odbc;
using System.Linq;
using System.Reflection;
using System.Data.OleDb;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public static class OleDbExceptionFactory
    {
        public static OleDbException CreateException(int number, Guid? connectionId = null)
        {
            var exceptionCtors = typeof(OleDbException)
                .GetTypeInfo()
                .DeclaredConstructors;

            return (OleDbException)exceptionCtors.First(c => c.GetParameters().Length == 3)
                .Invoke(["Bang!", number, null]);
        }
    }

    public static class OdbcExceptionFactory
    {
        public static OdbcException CreateException(int number, Guid? connectionId = null)
        {
            var exceptionCtors = typeof(OdbcException)
                .GetTypeInfo()
                .DeclaredConstructors;

            return (OdbcException)exceptionCtors.First(c => c.GetParameters().Length == 3)
                .Invoke(["Bang!", number, null]);
        }
    }
}
