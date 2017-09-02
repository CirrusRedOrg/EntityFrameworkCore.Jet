using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests.Extensions.Metadata
{
    public class JetMetadataExtensionsTest
    {
        [Fact]
        public void Can_get_and_set_column_name()
        {
            var modelBuilder = GetModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .Metadata;

            Assert.Equal("Name", property.Jet().ColumnName);
            Assert.Equal("Name", ((IProperty)property).Jet().ColumnName);

            property.Relational().ColumnName = "Eman";

            Assert.Equal("Name", property.Name);
            Assert.Equal("Eman", property.Relational().ColumnName);
            Assert.Equal("Eman", property.Jet().ColumnName);
            Assert.Equal("Eman", ((IProperty)property).Jet().ColumnName);

            property.Jet().ColumnName = "MyNameIs";

            Assert.Equal("Name", property.Name);
            Assert.Equal("MyNameIs", property.Relational().ColumnName);
            Assert.Equal("MyNameIs", property.Jet().ColumnName);
            Assert.Equal("MyNameIs", ((IProperty)property).Jet().ColumnName);

            property.Jet().ColumnName = null;

            Assert.Equal("Name", property.Name);
            Assert.Equal("Name", property.Relational().ColumnName);
            Assert.Equal("Name", property.Jet().ColumnName);
            Assert.Equal("Name", ((IProperty)property).Jet().ColumnName);
        }

        [Fact]
        public void Can_get_and_set_table_name()
        {
            var modelBuilder = GetModelBuilder();

            var entityType = modelBuilder
                .Entity<Customer>()
                .Metadata;

            Assert.Equal("Customer", entityType.Jet().TableName);
            Assert.Equal("Customer", ((IEntityType)entityType).Jet().TableName);

            entityType.Relational().TableName = "Customizer";

            Assert.Equal("Customer", entityType.DisplayName());
            Assert.Equal("Customizer", entityType.Relational().TableName);
            Assert.Equal("Customizer", entityType.Jet().TableName);
            Assert.Equal("Customizer", ((IEntityType)entityType).Jet().TableName);

            entityType.Jet().TableName = "Custardizer";

            Assert.Equal("Customer", entityType.DisplayName());
            Assert.Equal("Custardizer", entityType.Relational().TableName);
            Assert.Equal("Custardizer", entityType.Jet().TableName);
            Assert.Equal("Custardizer", ((IEntityType)entityType).Jet().TableName);

            entityType.Jet().TableName = null;

            Assert.Equal("Customer", entityType.DisplayName());
            Assert.Equal("Customer", entityType.Relational().TableName);
            Assert.Equal("Customer", entityType.Jet().TableName);
            Assert.Equal("Customer", ((IEntityType)entityType).Jet().TableName);
        }

        [Fact]
        public void Can_get_schema_name()
        {
            var modelBuilder = new ModelBuilder(new ConventionSet());

            var entityType = modelBuilder
                .Entity<Customer>()
                .Metadata;

            Assert.Null(entityType.Jet().Schema);
            Assert.Null(((IEntityType)entityType).Jet().Schema);

            entityType.Relational().Schema = "db0";

            Assert.Equal("db0", entityType.Jet().Schema);
            Assert.Equal("db0", ((IEntityType)entityType).Jet().Schema);
        }

        [Fact]
        public void Can_get_and_set_column_type()
        {
            var modelBuilder = GetModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .Metadata;

            Assert.Null(property.Relational().ColumnType);
            Assert.Null(property.Jet().ColumnType);
            Assert.Null(((IProperty)property).Jet().ColumnType);

            property.Relational().ColumnType = "nvarchar(max)";

            Assert.Equal("nvarchar(max)", property.Relational().ColumnType);
            Assert.Equal("nvarchar(max)", property.Jet().ColumnType);
            Assert.Equal("nvarchar(max)", ((IProperty)property).Jet().ColumnType);

            property.Jet().ColumnType = "nvarchar(verstappen)";

            Assert.Equal("nvarchar(verstappen)", property.Relational().ColumnType);
            Assert.Equal("nvarchar(verstappen)", property.Jet().ColumnType);
            Assert.Equal("nvarchar(verstappen)", ((IProperty)property).Jet().ColumnType);

            property.Jet().ColumnType = null;

            Assert.Null(property.Relational().ColumnType);
            Assert.Null(property.Jet().ColumnType);
            Assert.Null(((IProperty)property).Jet().ColumnType);
        }

        [Fact]
        public void Can_get_and_set_column_default_expression()
        {
            var modelBuilder = GetModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .Metadata;

            Assert.Null(property.Relational().DefaultValueSql);
            Assert.Null(property.Jet().DefaultValueSql);
            Assert.Null(((IProperty)property).Jet().DefaultValueSql);

            property.Relational().DefaultValueSql = "newsequentialid()";

            Assert.Equal("newsequentialid()", property.Relational().DefaultValueSql);
            Assert.Equal("newsequentialid()", property.Jet().DefaultValueSql);
            Assert.Equal("newsequentialid()", ((IProperty)property).Jet().DefaultValueSql);

            property.Jet().DefaultValueSql = "expressyourself()";

            Assert.Equal("expressyourself()", property.Relational().DefaultValueSql);
            Assert.Equal("expressyourself()", property.Jet().DefaultValueSql);
            Assert.Equal("expressyourself()", ((IProperty)property).Jet().DefaultValueSql);

            property.Jet().DefaultValueSql = null;

            Assert.Null(property.Relational().DefaultValueSql);
            Assert.Null(property.Jet().DefaultValueSql);
            Assert.Null(((IProperty)property).Jet().DefaultValueSql);
        }

        [Fact]
        public void Can_get_and_set_column_key_name()
        {
            var modelBuilder = GetModelBuilder();

            var key = modelBuilder
                .Entity<Customer>()
                .HasKey(e => e.Id)
                .Metadata;

            Assert.Equal("PK_Customer", key.Relational().Name);
            Assert.Equal("PK_Customer", key.Jet().Name);
            Assert.Equal("PK_Customer", ((IKey)key).Jet().Name);

            key.Relational().Name = "PrimaryKey";

            Assert.Equal("PrimaryKey", key.Relational().Name);
            Assert.Equal("PrimaryKey", key.Jet().Name);
            Assert.Equal("PrimaryKey", ((IKey)key).Jet().Name);

            key.Jet().Name = "PrimarySchool";

            Assert.Equal("PrimarySchool", key.Relational().Name);
            Assert.Equal("PrimarySchool", key.Jet().Name);
            Assert.Equal("PrimarySchool", ((IKey)key).Jet().Name);

            key.Jet().Name = null;

            Assert.Equal("PK_Customer", key.Relational().Name);
            Assert.Equal("PK_Customer", key.Jet().Name);
            Assert.Equal("PK_Customer", ((IKey)key).Jet().Name);
        }

        [Fact]
        public void Can_get_and_set_column_foreign_key_name()
        {
            var modelBuilder = GetModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .HasKey(e => e.Id);

            var foreignKey = modelBuilder
                .Entity<Order>()
                .HasOne<Customer>()
                .WithOne()
                .HasForeignKey<Order>(e => e.CustomerId)
                .Metadata;

            Assert.Equal("FK_Order_Customer_CustomerId", foreignKey.Relational().Name);
            Assert.Equal("FK_Order_Customer_CustomerId", ((IForeignKey)foreignKey).Relational().Name);

            foreignKey.Relational().Name = "FK";

            Assert.Equal("FK", foreignKey.Relational().Name);
            Assert.Equal("FK", ((IForeignKey)foreignKey).Relational().Name);

            foreignKey.Relational().Name = "KFC";

            Assert.Equal("KFC", foreignKey.Relational().Name);
            Assert.Equal("KFC", ((IForeignKey)foreignKey).Relational().Name);

            foreignKey.Relational().Name = null;

            Assert.Equal("FK_Order_Customer_CustomerId", foreignKey.Relational().Name);
            Assert.Equal("FK_Order_Customer_CustomerId", ((IForeignKey)foreignKey).Relational().Name);
        }

        [Fact]
        public void Can_get_and_set_index_name()
        {
            var modelBuilder = GetModelBuilder();

            var index = modelBuilder
                .Entity<Customer>()
                .HasIndex(e => e.Id)
                .Metadata;

            Assert.Equal("IX_Customer_Id", index.Relational().Name);
            Assert.Equal("IX_Customer_Id", ((IIndex)index).Relational().Name);

            index.Relational().Name = "MyIndex";

            Assert.Equal("MyIndex", index.Relational().Name);
            Assert.Equal("MyIndex", ((IIndex)index).Relational().Name);

            index.Jet().Name = "DexKnows";

            Assert.Equal("DexKnows", index.Relational().Name);
            Assert.Equal("DexKnows", ((IIndex)index).Relational().Name);

            index.Jet().Name = null;

            Assert.Equal("IX_Customer_Id", index.Relational().Name);
            Assert.Equal("IX_Customer_Id", ((IIndex)index).Relational().Name);
        }

        private static ModelBuilder GetModelBuilder() => JetTestHelpers.Instance.CreateConventionBuilder();

        private class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private class Order
        {
            public int CustomerId { get; set; }
        }
    }
}
