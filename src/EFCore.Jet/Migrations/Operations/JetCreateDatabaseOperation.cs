// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace EntityFrameworkCore.Jet.Migrations.Operations
{
    /// <summary>
    ///     A Jet-specific <see cref="MigrationOperation" /> to create a database.
    /// </summary>
    public class JetCreateDatabaseOperation : MigrationOperation
    {
        /// <summary>
        ///     The name of the database.
        /// </summary>
        public virtual string Name { get; set; } = null!;
        public virtual string? Password { get; set; }
    }
}
