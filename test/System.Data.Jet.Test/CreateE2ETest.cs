using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/* 
       Additional operations and considerations:

[PropertyConfiguration].[SumOfAAndB] = [A] + [B]
    JET CREATE TABLE STATEMENT does not support computed colum creation. Also Migration will have this issue but adding this configuration
    using Microsoft Access user interface will create the same column
[Tabs   In  Column]
    JET does not allow tabs in column names. Tabs has been removed
[!Exclamation!Mark!Column]
    JET does not support exclamation marks in column names. Exclamation marks has been replaced with #



*/


namespace System.Data.Jet.Test
{
    [TestClass]
    public class CreateE2ETest
    {
        private string _scriptPath;

        private const string DatabaseName = "E2E.accdb";

        private DbConnection _connection;

        [TestMethod]
        public void CreateE2ETestRun()
        {
            JetConfiguration.ShowSqlStatements = false;
            ExecuteScript();
            JetConfiguration.ShowSqlStatements = true;
        }


        [TestInitialize]
        public void Initialize()
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            _scriptPath = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "E2E.sql");

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
                    .Split(script).Where(b => !string.IsNullOrEmpty(b)))
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
