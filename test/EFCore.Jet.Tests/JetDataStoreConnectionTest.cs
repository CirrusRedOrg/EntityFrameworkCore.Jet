using System.Diagnostics;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests
{
    public class JetDataStoreConnectionTest
    {
        [Fact]
        public void Creates_Jet_connection_string()
        {
            using (var connection = new JetConnection(CreateDependencies()))
            {
                Assert.IsType<JetConnection>(connection.DbConnection);
            }
        }

        public static RelationalConnectionDependencies CreateDependencies(DbContextOptions options = null)
        {
            options = options
                      ?? new DbContextOptionsBuilder()
                          .UseJet(System.Data.Jet.JetConnection.GetConnectionString(@"C:\data\EF7Jet.accdb;"))
                          .Options;

            return new RelationalConnectionDependencies(
                options,
                new DiagnosticsLogger<DbLoggerCategory.Database.Transaction>(
                    new LoggerFactory(),
                    new LoggingOptions(),
                    new DiagnosticListener("FakeDiagnosticListener")),
                new DiagnosticsLogger<DbLoggerCategory.Database.Connection>(
                    new LoggerFactory(),
                    new LoggingOptions(),
                    new DiagnosticListener("FakeDiagnosticListener")),
                new NamedConnectionStringResolver(options));
        }
    }
}
