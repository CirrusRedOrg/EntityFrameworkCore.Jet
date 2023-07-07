// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
using EntityFrameworkCore.Jet.Data;
using System.Data.OleDb;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public static class OleDbExceptionFactory
    {
        public static OleDbException CreateOleDbException(int number, Guid? connectionId = null)
        {
            var exceptionCtors = typeof(OleDbException)
                .GetTypeInfo()
                .DeclaredConstructors;

            return (OleDbException)exceptionCtors.First(c => c.GetParameters().Length == 3)
                .Invoke(new object[] { "Bang!", number, null });
        }
    }
}
