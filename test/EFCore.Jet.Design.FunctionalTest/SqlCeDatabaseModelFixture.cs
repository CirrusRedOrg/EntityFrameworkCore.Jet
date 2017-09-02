using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Specification.Tests;

namespace Microsoft.EntityFrameworkCore
{
    public class SqlCeDatabaseModelFixture : IDisposable
    {
        public SqlCeDatabaseModelFixture()
        {
            TestStore = SqlCeTestStore.CreateScratch(true);
        }

        public TestDesignLoggerFactory TestDesignLoggerFactory { get; } = new TestDesignLoggerFactory();

        public DatabaseModel CreateModel(List<string> createSql, IEnumerable<string> tables = null, ILogger logger = null)
        {
            foreach (var sql in createSql)
            {
                TestStore.ExecuteNonQuery(sql);
            }

            return new SqlCeDatabaseModelFactory(
                new DiagnosticsLogger<DbLoggerCategory.Scaffolding>(
                TestDesignLoggerFactory,
                new LoggingOptions(),
                new DiagnosticListener("Fake")))
            .Create(TestStore.ConnectionString, tables ?? Enumerable.Empty<string>(), Enumerable.Empty<string>());
        }

        public IEnumerable<T> Query<T>(string sql, params object[] parameters) => TestStore.Query<T>(sql, parameters);

        public SqlCeTestStore TestStore { get; }

        public void ExecuteNonQuery(string sql) => TestStore.ExecuteNonQuery(sql);

        public void Dispose() => TestStore.Dispose();
    }
}