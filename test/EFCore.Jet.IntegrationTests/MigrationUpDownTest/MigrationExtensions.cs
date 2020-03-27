using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.MigrationUpDownTest
{
    static class MigrationExtensions
    {
        public static void RunMigration(this DbContext context, DbMigration migration)
        {
            var prop = migration.GetType().GetProperty("Operations", BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                IEnumerable<MigrationOperation> operations = prop.GetValue(migration) as IEnumerable<MigrationOperation>;
                MigrationSqlGenerator generator = (new DbMigrationsConfiguration()).GetSqlGenerator("JetEntityFrameworkProvider");
                var statements = generator.Generate(operations, "Jet");
                foreach (MigrationStatement item in statements)
                    context.Database.ExecuteSqlCommand(item.Sql);
            }
        }
    }
}
