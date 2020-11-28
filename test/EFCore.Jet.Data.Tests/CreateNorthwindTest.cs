using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class CreateNorthwindTest
    {
        private const string StoreName = nameof(CreateNorthwindTest) + ".accdb";

        private string _scriptPath;
        private DbConnection _connection;

        [TestInitialize]
        public void Setup()
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            _scriptPath = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "Northwind.sql");
            _connection = Helpers.CreateAndOpenDatabase(StoreName);
        }

        [TestCleanup]
        public void TearDown()
        {
            _connection.Dispose();
        }

        [TestMethod]
        public void CreateE2ETestRun()
        {
            var showSqlStatements = JetConfiguration.ShowSqlStatements;
            JetConfiguration.ShowSqlStatements = false;
            ExecuteScript();
            JetConfiguration.ShowSqlStatements = showSqlStatements;
        }

        private void ExecuteScript()
        {
            using var command = _connection.CreateCommand();

            var script = File.ReadAllText(_scriptPath);
            var batches = new Regex(@"\s*;\s*", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(1000.0))
                .Split(script)
                .Where(b => !string.IsNullOrEmpty(b))
                .ToList();

            var retryWaitTime = TimeSpan.FromMilliseconds(250);
            const int maxRetryCount = 6;
            var retryCount = 0;
            
            foreach (var batch in batches)
            {
                command.CommandText = batch;
                
                try
                {
                    command.ExecuteNonQuery();
                    retryCount = 0;
                }
                catch (Exception e)
                {
                    if (retryCount >= maxRetryCount)
                    {
                        Console.WriteLine(e.Message);
                        Console.WriteLine(batch);
                        throw;
                    }

                    retryCount++;
                    Thread.Sleep(retryWaitTime);
                }
            }
        }
    }
}