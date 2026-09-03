// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using LibRed.Data;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities
{
    /// <summary>
    /// Builds the LibRed-native <see cref="LibRedException"/> the tests use to simulate a transient
    /// failure. LibRed doesn't raise <c>OleDbException</c>/<c>OdbcException</c>, so — unlike EFCore.Jet's
    /// test factory — this stays entirely on LibRed's own exception type.
    /// </summary>
    public static class LibRedExceptionFactory
    {
        public static DbException CreateException(int number, Guid? connectionId = null)
            => new LibRedException("Bang!", number);
    }
}
