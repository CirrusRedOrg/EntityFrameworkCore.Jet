// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

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
        public virtual string Name { get; [param: NotNull] set; }
        public virtual string Password { get; [param: CanBeNull] set; }
    }
}
