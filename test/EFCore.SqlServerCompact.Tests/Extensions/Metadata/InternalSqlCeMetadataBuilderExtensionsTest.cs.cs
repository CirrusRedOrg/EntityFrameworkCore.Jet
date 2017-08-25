using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Tests.Extensions.Metadata
{
    public class InternalSqlCeMetadataBuilderExtensionsTest
    {
        private InternalModelBuilder CreateBuilder()
            => new InternalModelBuilder(new Model());

        [Fact]
        public void Can_access_entity_type()
        {
            var typeBuilder = CreateBuilder().Entity(typeof(Splot), ConfigurationSource.Convention);

            Assert.True(typeBuilder.SqlCe(ConfigurationSource.Convention).ToTable("Splew"));
            Assert.Equal("Splew", typeBuilder.Metadata.SqlCe().TableName);

            Assert.True(typeBuilder.SqlCe(ConfigurationSource.DataAnnotation).ToTable("Splow"));
            Assert.Equal("Splow", typeBuilder.Metadata.SqlCe().TableName);

            Assert.False(typeBuilder.SqlCe(ConfigurationSource.Convention).ToTable("Splod"));
            Assert.Equal("Splow", typeBuilder.Metadata.SqlCe().TableName);
        }

        [Fact]
        public void Can_access_key()
        {
            var modelBuilder = CreateBuilder();
            var entityTypeBuilder = modelBuilder.Entity(typeof(Splot), ConfigurationSource.Convention);
            var idProperty = entityTypeBuilder.Property("Id", typeof(int), ConfigurationSource.Convention).Metadata;
            var keyBuilder = entityTypeBuilder.HasKey(new[] { idProperty.Name }, ConfigurationSource.Convention);

            Assert.True(keyBuilder.SqlCe(ConfigurationSource.Convention).HasName("Splew"));
            Assert.Equal("Splew", keyBuilder.Metadata.SqlCe().Name);

            Assert.True(keyBuilder.SqlCe(ConfigurationSource.DataAnnotation).HasName("Splow"));
            Assert.Equal("Splow", keyBuilder.Metadata.SqlCe().Name);

            Assert.False(keyBuilder.SqlCe(ConfigurationSource.Convention).HasName("Splod"));
            Assert.Equal("Splow", keyBuilder.Metadata.SqlCe().Name);
        }

        [Fact]
        public void Can_access_index()
        {
            var modelBuilder = CreateBuilder();
            var entityTypeBuilder = modelBuilder.Entity(typeof(Splot), ConfigurationSource.Convention);
            entityTypeBuilder.Property("Id", typeof(int), ConfigurationSource.Convention);
            var indexBuilder = entityTypeBuilder.HasIndex(new[] { "Id" }, ConfigurationSource.Convention);

            indexBuilder.SqlCe(ConfigurationSource.Convention).HasName("Splew");
            Assert.Equal("Splew", indexBuilder.Metadata.SqlCe().Name);

            indexBuilder.SqlCe(ConfigurationSource.DataAnnotation).HasName("Splow");
            Assert.Equal("Splow", indexBuilder.Metadata.SqlCe().Name);

            indexBuilder.SqlCe(ConfigurationSource.Convention).HasName("Splod");
            Assert.Equal("Splow", indexBuilder.Metadata.SqlCe().Name);
        }

        [Fact]
        public void Can_access_relationship()
        {
            var modelBuilder = CreateBuilder();
            var entityTypeBuilder = modelBuilder.Entity(typeof(Splot), ConfigurationSource.Convention);
            entityTypeBuilder.Property("Id", typeof(int), ConfigurationSource.Convention);
            var relationshipBuilder = entityTypeBuilder.HasForeignKey("Splot", new[] { "Id" }, ConfigurationSource.Convention);

            Assert.True(relationshipBuilder.SqlCe(ConfigurationSource.Convention).HasConstraintName("Splew"));
            Assert.Equal("Splew", relationshipBuilder.Metadata.SqlCe().Name);

            Assert.True(relationshipBuilder.SqlCe(ConfigurationSource.DataAnnotation).HasConstraintName("Splow"));
            Assert.Equal("Splow", relationshipBuilder.Metadata.SqlCe().Name);

            Assert.False(relationshipBuilder.SqlCe(ConfigurationSource.Convention).HasConstraintName("Splod"));
            Assert.Equal("Splow", relationshipBuilder.Metadata.SqlCe().Name);
        }

        private class Splot
        {
        }
    }
}
