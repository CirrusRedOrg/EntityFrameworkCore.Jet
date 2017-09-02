using System;
using EntityFrameworkCore.Jet.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests.Extensions.Metadata
{
    public class InternalJetMetadataBuilderExtensionsTest
    {
        private InternalModelBuilder CreateBuilder()
            => new InternalModelBuilder(new Model());

        [Fact]
        public void Can_access_entity_type()
        {
            var typeBuilder = CreateBuilder().Entity(typeof(Splot), ConfigurationSource.Convention);

            Assert.True(typeBuilder.Jet(ConfigurationSource.Convention).ToTable("Splew"));
            Assert.Equal("Splew", typeBuilder.Metadata.Jet().TableName);

            Assert.True(typeBuilder.Jet(ConfigurationSource.DataAnnotation).ToTable("Splow"));
            Assert.Equal("Splow", typeBuilder.Metadata.Jet().TableName);

            Assert.False(typeBuilder.Jet(ConfigurationSource.Convention).ToTable("Splod"));
            Assert.Equal("Splow", typeBuilder.Metadata.Jet().TableName);
        }

        [Fact]
        public void Can_access_key()
        {
            var modelBuilder = CreateBuilder();
            var entityTypeBuilder = modelBuilder.Entity(typeof(Splot), ConfigurationSource.Convention);
            var idProperty = entityTypeBuilder.Property("Id", typeof(int), ConfigurationSource.Convention).Metadata;
            var keyBuilder = entityTypeBuilder.HasKey(new[] { idProperty.Name }, ConfigurationSource.Convention);

            Assert.True(keyBuilder.Jet(ConfigurationSource.Convention).Name("Splew"));
            Assert.Equal("Splew", keyBuilder.Metadata.Jet().Name);

            Assert.True(keyBuilder.Jet(ConfigurationSource.DataAnnotation).Name("Splow"));
            Assert.Equal("Splow", keyBuilder.Metadata.Jet().Name);

            Assert.False(keyBuilder.Jet(ConfigurationSource.Convention).Name("Splod"));
            Assert.Equal("Splow", keyBuilder.Metadata.Jet().Name);
        }

        [Fact]
        public void Can_access_index()
        {
            var modelBuilder = CreateBuilder();
            var entityTypeBuilder = modelBuilder.Entity(typeof(Splot), ConfigurationSource.Convention);
            entityTypeBuilder.Property("Id", typeof(int), ConfigurationSource.Convention);
            var indexBuilder = entityTypeBuilder.HasIndex(new[] { "Id" }, ConfigurationSource.Convention);

            indexBuilder.Jet(ConfigurationSource.Convention).Name("Splew");
            Assert.Equal("Splew", indexBuilder.Metadata.Jet().Name);

            indexBuilder.Jet(ConfigurationSource.DataAnnotation).Name("Splow");
            Assert.Equal("Splow", indexBuilder.Metadata.Jet().Name);

            indexBuilder.Jet(ConfigurationSource.Convention).Name("Splod");
            Assert.Equal("Splow", indexBuilder.Metadata.Jet().Name);
        }

        [Fact(Skip = "Unsupported by JET: Actually Jet does not have interesting annotations")]
        public void Can_access_relationship()
        {
            var modelBuilder = CreateBuilder();
            var entityTypeBuilder = modelBuilder.Entity(typeof(Splot), ConfigurationSource.Convention);
            entityTypeBuilder.Property("Id", typeof(int), ConfigurationSource.Convention);
            var relationshipBuilder = entityTypeBuilder.HasForeignKey("Splot", new[] { "Id" }, ConfigurationSource.Convention);
            /*
            Assert.True(relationshipBuilder.Jet(ConfigurationSource.Convention).HasConstraintName("Splew"));
            Assert.Equal("Splew", relationshipBuilder.Metadata.Jet().Name);

            Assert.True(relationshipBuilder.Jet(ConfigurationSource.DataAnnotation).HasConstraintName("Splow"));
            Assert.Equal("Splow", relationshipBuilder.Metadata.Jet().Name);

            Assert.False(relationshipBuilder.Jet(ConfigurationSource.Convention).HasConstraintName("Splod"));
            Assert.Equal("Splow", relationshipBuilder.Metadata.Jet().Name);
            */
        }

        private class Splot
        {
        }
    }
}
