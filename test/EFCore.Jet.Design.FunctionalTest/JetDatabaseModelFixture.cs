using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EntityFramework.Jet.FunctionalTests;
using EntityFrameworkCore.Jet.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Jet.Design.FunctionalTests
{
    public class JetDatabaseModelFixture : IDisposable
    {
        public JetDatabaseModelFixture()
        {
            TestStore = JetTestStore.CreateScratch(true);
        }

        public TestDesignLoggerFactory TestDesignLoggerFactory { get; } = new TestDesignLoggerFactory();

        public DatabaseModel CreateModel(List<string> createSql, IEnumerable<string> tables = null, ILogger logger = null)
        {
            foreach (var sql in createSql)
            {
                TestStore.ExecuteNonQuery(sql);
            }

            return new JetDatabaseModelFactory(
                new DiagnosticsLogger<DbLoggerCategory.Scaffolding>(
                TestDesignLoggerFactory,
                new LoggingOptions(),
                new DiagnosticListener("Fake")))
            .Create(TestStore.ConnectionString, tables ?? Enumerable.Empty<string>(), Enumerable.Empty<string>());
        }

        public IEnumerable<T> Query<T>(string sql, params object[] parameters) => TestStore.Query<T>(sql, parameters);

        public JetTestStore TestStore { get; }

        public void ExecuteNonQuery(string sql) => TestStore.ExecuteNonQuery(sql);

        public void Dispose() => TestStore.Dispose();
    }
}