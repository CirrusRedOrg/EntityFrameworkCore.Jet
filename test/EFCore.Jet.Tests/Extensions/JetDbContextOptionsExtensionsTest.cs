using System.Data.Jet;
using System.Linq;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests.Extensions
{
    public class JetDbContextOptionsBuilderExtensionsTest
    {
        [Fact]
        public void Can_add_extension_with_connection_string()
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder.UseJet(JetConnection.GetConnectionString("C:\\data\\Unicorn.accdb"));

            var extension = optionsBuilder.Options.Extensions.OfType<JetOptionsExtension>().Single();

            Assert.Equal(JetConnection.GetConnectionString("C:\\data\\Unicorn.accdb"), extension.ConnectionString);
            Assert.Null(extension.Connection);
        }

        [Fact]
        public void Can_add_extension_with_connection_string_using_generic_options()
        {
            var optionsBuilder = new DbContextOptionsBuilder<DbContext>();
            optionsBuilder.UseJet(JetConnection.GetConnectionString("C:\\data\\Multicorn.accdb"));

            var extension = optionsBuilder.Options.Extensions.OfType<JetOptionsExtension>().Single();

            Assert.Equal(JetConnection.GetConnectionString("C:\\data\\Multicorn.accdb"), extension.ConnectionString);
            Assert.Null(extension.Connection);
        }

        [Fact]
        public void Can_add_extension_with_connection()
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            var connection = new JetConnection();

            optionsBuilder.UseJet(connection);

            var extension = optionsBuilder.Options.Extensions.OfType<JetOptionsExtension>().Single();

            Assert.Same(connection, extension.Connection);
            Assert.Null(extension.ConnectionString);
        }

        [Fact]
        public void Can_add_extension_with_connection_using_generic_options()
        {
            var optionsBuilder = new DbContextOptionsBuilder<DbContext>();
            var connection = new JetConnection();

            optionsBuilder.UseJet(connection);

            var extension = optionsBuilder.Options.Extensions.OfType<JetOptionsExtension>().Single();

            Assert.Same(connection, extension.Connection);
            Assert.Null(extension.ConnectionString);
        }

        [Fact]
        public void Can_add_extension_with_connectionStringBuilder()
        {
            var optionsBuilder = new DbContextOptionsBuilder();

            optionsBuilder.UseJet(JetConnection.GetConnectionString("C:\\data\\Multicorn.accdb"));

            var extension = optionsBuilder.Options.Extensions.OfType<JetOptionsExtension>().Single();

            Assert.Equal(JetConnection.GetConnectionString("C:\\data\\Multicorn.accdb"), extension.ConnectionString);
            Assert.Null(extension.Connection);
        }

        [Fact]
        public void Can_add_extension_with_connectionStringBuilder_using_generic_options()
        {
            var optionsBuilder = new DbContextOptionsBuilder<DbContext>();

            optionsBuilder.UseJet(JetConnection.GetConnectionString("C:\\data\\Multicorn.accdb"));

            var extension = optionsBuilder.Options.Extensions.OfType<JetOptionsExtension>().Single();

            Assert.Equal(JetConnection.GetConnectionString("C:\\data\\Multicorn.accdb"), extension.ConnectionString);
            Assert.Null(extension.Connection);
        }

        [Fact(Skip = "Unsupported by JET: Actually Jet does not have parameter in ContextOptions")]
        public void Can_add_extension_with_legacy_paging()
        {
            /*var optionsBuilder = new DbContextOptionsBuilder<DbContext>();

            optionsBuilder.UseJet(JetConnection.GetConnectionString("C:\\data\\Multicorn.accdb"), b => b.UseClientEvalForUnsupportedSqlConstructs(clientEvalForUnsupportedSqlConstructs: true));

            var extension = optionsBuilder.Options.Extensions.OfType<JetOptionsExtension>().Single();

            Assert.True(extension.ClientEvalForUnsupportedSqlConstructs.HasValue);
            Assert.True(extension.ClientEvalForUnsupportedSqlConstructs.Value);*/
        }
    }
}
