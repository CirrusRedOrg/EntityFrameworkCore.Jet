// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.Jet.Migrations.Operations
{
    /// <summary>
    ///     A Jet-specific <see cref="MigrationOperation" /> to drop a database.
    /// </summary>
    public class JetDropDatabaseOperation : MigrationOperation
    {
        /// <summary>
        ///     The name of the database.
        /// </summary>
        public virtual string Name { get; set; } = null!;
    }
}
