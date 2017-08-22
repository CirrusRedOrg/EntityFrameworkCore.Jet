// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.Jet.Migrations.Operations
{
    public class JetDropDatabaseOperation : MigrationOperation
    {
        public virtual string Name { get; [param: NotNull] set; }
    }
}
