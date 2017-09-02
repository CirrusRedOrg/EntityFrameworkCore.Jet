using System.Reflection;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests
{
    public class JetDataStoreSourceTest
    {
        [Fact]
        public void Is_configured_when_configuration_contains_associated_extension()
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder.UseJet("Data Source=Crunchie");

            Assert.True(new DatabaseProvider<JetOptionsExtension>(new DatabaseProviderDependencies()).IsConfigured(optionsBuilder.Options));
        }

        [Fact]
        public void Is_not_configured_when_configuration_does_not_contain_associated_extension()
        {
            var optionsBuilder = new DbContextOptionsBuilder();

            Assert.False(new DatabaseProvider<JetOptionsExtension>(new DatabaseProviderDependencies()).IsConfigured(optionsBuilder.Options));
        }

        [Fact]
        public void Returns_appropriate_name()
        {
            Assert.Equal(
                typeof(JetConnection).GetTypeInfo().Assembly.GetName().Name,
                new DatabaseProvider<JetOptionsExtension>(new DatabaseProviderDependencies()).Name);
        }
    }
}
