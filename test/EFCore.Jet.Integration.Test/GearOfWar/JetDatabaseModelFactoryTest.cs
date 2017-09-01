using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Jet;
using System.Linq;
using EntityFrameworkCore.Jet.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.GearOfWar
{
    [TestClass]
    public class JetDatabaseModelFactoryTest : TestBase<GearsOfWarContext>
    {

        [TestMethod]
        [ExpectedException(typeof(System.Data.OleDb.OleDbException), AllowDerivedTypes = true)]
        public void CreateAllSystemTablesAndThrowException()
        {

            // This method is used to create all system table of this model
            string sql = @" SELECT 
  (show tables)
, (show tablecolumns)
, (show indexes)
, (show indexcolumns)
, (show views)
, (show viewcolumns)
, (show constraints)
, (show checkconstraints)
, (show constraintcolumns)
, (show foreignKeyconstraints)
, (show foreignKeys)
, (show viewconstraints)
, (show viewconstraintcolumns)
, (show viewforeignkeys)

WHERE 1 = 2;
";
            Context.Cities.FromSql(sql).ToList();

        }


        [TestMethod]
        public void JetDatabaseModelFactoryTestRun()
        {

            List<string> tableNames = new List<string>();

            using (var connection = GetConnection())
            {

                var command = connection.CreateCommand();
                command.CommandText = "SHOW TABLES";
                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        tableNames.Add(reader.GetString(0));
                }
            }

            var logger = Context.GetService<IDiagnosticsLogger<DbLoggerCategory.Scaffolding>>();
            JetDatabaseModelFactory modelFactory = new JetDatabaseModelFactory(logger);
            using (var connection = GetConnection())
            {
                var model = modelFactory.Create(connection, tableNames, new[] { "Jet" });
                ShowModel(model);
            }

        }

        private void ShowModel(DatabaseModel model)
        {
            foreach (DatabaseTable table in model.Tables)
            {
                ShowIndent(0, table.Name);
                foreach (DatabaseColumn column in table.Columns)
                {
                    ShowIndent(1, column.Name);
                }
                foreach (DatabaseIndex index in table.Indexes)
                {
                    ShowIndent(1,"{0} (Index)", index.Name);
                    foreach (DatabaseColumn indexColumn in index.Columns)
                    {
                        ShowIndent(2, "{0}", indexColumn.Name);
                    }
                }
                foreach (DatabaseForeignKey foreignKey in table.ForeignKeys)
                {
                    ShowIndent(1, "{0} (ForeignKey) {1} => {2}", foreignKey.Name, foreignKey.Table.Name, foreignKey.PrincipalTable.Name);
                    Assert.AreNotEqual(0, foreignKey.Columns.Count);
                    Assert.AreNotEqual(0, foreignKey.PrincipalColumns.Count);

                    for (int index = 0; index < foreignKey.Columns.Count; index++)
                    {
                        DatabaseColumn column = foreignKey.Columns[index];
                        DatabaseColumn principalColumn = foreignKey.PrincipalColumns[index];
                        ShowIndent(2, "{0} => {1}", column.Name, principalColumn.Name);
                    }
                }

            }
        }

        private void ShowIndent(int indent, string text)
        {
            Console.WriteLine(new string(' ', indent) + text);
        }

        private void ShowIndent(int indent, string format, params object[] args)
        {
            Console.WriteLine(new string(' ', indent) + format, args);
        }


        protected override DbConnection GetConnection()
        {
            return new JetConnection(JetConnection.GetConnectionString("SystemTables.accdb"));
        }
    }
}
