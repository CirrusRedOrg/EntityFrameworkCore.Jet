using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Tests
{
    public class SqlCeDataStoreSourceTest
    {
        [Fact]
        public void Is_configured_when_configuration_contains_associated_extension()
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder.UseSqlCe("Data Source=Crunchie");

            Assert.True(new DatabaseProvider<SqlCeOptionsExtension>(new DatabaseProviderDependencies()).IsConfigured(optionsBuilder.Options));
        }

        [Fact]
        public void Is_not_configured_when_configuration_does_not_contain_associated_extension()
        {
            var optionsBuilder = new DbContextOptionsBuilder();

            Assert.False(new DatabaseProvider<SqlCeOptionsExtension>(new DatabaseProviderDependencies()).IsConfigured(optionsBuilder.Options));
        }

        [Fact]
        public void Returns_appropriate_name()
        {
            Assert.Equal(
                typeof(SqlCeDatabaseConnection).GetTypeInfo().Assembly.GetName().Name,
                new DatabaseProvider<SqlCeOptionsExtension>(new DatabaseProviderDependencies()).Name);
        }
    }
}
