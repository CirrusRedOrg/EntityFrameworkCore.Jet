// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace EntityFrameworkCore.LibRed.Infrastructure
{
    /// <summary>
    ///     The SQL dialect the LibRed provider generates.
    /// </summary>
    public enum LibRedSqlMode
    {
        /// <summary>
        ///     LibRed uses its own SQL generator, which is free of the Jet dialect's limitations. This is the default.
        /// </summary>
        Extended = 0,

        /// <summary>
        ///     LibRed generates SQL that the Jet/ACE engine also accepts, using the same SQL generator as the
        ///     EntityFrameworkCore.Jet provider.
        /// </summary>
        Compatible = 1,
    }
}
