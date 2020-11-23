// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
using System.Data.Jet;
using System.Data.OleDb;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public static class OleDbExceptionFactory
    {
        public static OleDbException CreateOleDbException(int number, Guid? connectionId = null)
        {
            var errorCtors = typeof(OleDbError)
                .GetTypeInfo()
                .DeclaredConstructors;

            var error = (OleDbError)errorCtors.First(c => c.GetParameters().Length == 8)
                .Invoke(new object[] { number, (byte)0, (byte)0, "Server", "ErrorMessage", "Procedure", 0, null });
            var errors = (OleDbErrorCollection)typeof(OleDbErrorCollection)
                .GetTypeInfo()
                .DeclaredConstructors
                .Single()
                .Invoke(null);

            typeof(OleDbErrorCollection).GetRuntimeMethods().Single(m => m.Name == "Add").Invoke(errors, new object[] { error });

            var exceptionCtors = typeof(OleDbException)
                .GetTypeInfo()
                .DeclaredConstructors;

            return (OleDbException)exceptionCtors.First(c => c.GetParameters().Length == 4)
                .Invoke(new object[] { "Bang!", errors, null, connectionId ?? Guid.NewGuid() });
        }
    }
}
