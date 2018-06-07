using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class CreateNorthwindTest
    {
        private string _scriptPath;

        private const string DatabaseName = "NorthwindEF7.accdb";

        private DbConnection _connection;

        [TestMethod]
        public void CreateNorthwindTestRun()
        {
            JetConfiguration.ShowSqlStatements = false;
            ExecuteScript();
            JetConfiguration.ShowSqlStatements = true;
        }


        [TestInitialize]
        public void Initialize()
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            _scriptPath = Path.Combine(Path.GetDirectoryName(this.GetType().Assembly.Location), "Northwind.sql");

            JetConnection.DropDatabase(JetConnection.GetConnectionString(DatabaseName), false);
            AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString(DatabaseName));

            _connection = new JetConnection(JetConnection.GetConnectionString(DatabaseName));
            _connection.Open();
        }

        public void ExecuteScript()
        {
            var script = File.ReadAllText(_scriptPath);
            foreach (var batch in
                new Regex("^GO", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(1000.0))
                    .Split(script).Where(b => !string.IsNullOrEmpty(b)).ToList())
            {
                DbCommand command = _connection.CreateCommand();
                command.CommandText = batch;
                try
                {
                    command.ExecuteNonQuery();

                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine(batch);
                    throw;
                }
            }
        }


        [TestCleanup]
        public void CleanUp()
        {
            _connection.Dispose();
        }

    }
}
