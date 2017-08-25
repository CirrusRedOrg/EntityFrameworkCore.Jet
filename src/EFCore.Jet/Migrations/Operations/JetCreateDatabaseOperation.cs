// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Jet;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.Jet.Migrations.Operations
{
    public class JetCreateDatabaseOperation : MigrationOperation
    {
        /// <summary>
        /// Gets or sets the full file name with extension.
        /// It supports standard .Net expansion
        /// </summary>
        /// <value>
        /// The file name.
        /// </value>
        public virtual string Name { get; [param: NotNull] set; }
    }
}
