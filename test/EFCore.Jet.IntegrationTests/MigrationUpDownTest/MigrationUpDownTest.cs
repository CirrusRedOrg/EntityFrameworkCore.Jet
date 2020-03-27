using System;
using EFCore.Jet.Integration.Test.Model02;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.MigrationUpDownTest
{
    [TestClass]
    public class MigrationUpDownTest
    {
        [TestMethod]
        public void SimpleMigration()
        {
            using (var context = new Context(SetUpCodeFirst.Connection))
            {
                var migration = new SimpleMigration();
                migration.Up(); // or migration.Down();                
                context.RunMigration(migration);
            }
        }

        [TestMethod]
        public void MigrationWithDefaultValue()
        {
            using (var context = new Context(SetUpCodeFirst.Connection))
            {
                var migration = new MigrationWithDefaultValue();
                migration.Up(); // or migration.Down();                
                context.RunMigration(migration);
            }
        }
        [TestMethod]
        public void MigrationWithDefaultValueSql()
        {
            using (var context = new Context(SetUpCodeFirst.Connection))
            {
                var migration = new MigrationWithDefaultValueSql();
                migration.Up(); // or migration.Down();                
                context.RunMigration(migration);
            }
        }

        [TestMethod]
        public void MigrationWithNumericDefaultValue()
        {
            using (var context = new Context(SetUpCodeFirst.Connection))
            {
                var migration = new MigrationWithNumericDefaultValue();
                migration.Up(); // or migration.Down();                
                context.RunMigration(migration);
            }
        }
        [TestMethod]
        public void MigrationWithNumericDefaultValueSql()
        {
            using (var context = new Context(SetUpCodeFirst.Connection))
            {
                var migration = new MigrationWithNumericDefaultValueSql();
                migration.Up(); // or migration.Down();                
                context.RunMigration(migration);
            }
        }


    }
}
