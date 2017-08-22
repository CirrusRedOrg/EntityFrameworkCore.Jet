// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.Jet.Migrations.Operations
{
    public class JetCreateDatabaseOperation : MigrationOperation
    {
        public virtual string FileName { get; [param: CanBeNull] set; }
    }
}
