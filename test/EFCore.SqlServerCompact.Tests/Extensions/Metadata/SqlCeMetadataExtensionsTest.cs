using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Tests.Extensions.Metadata
{
    public class SqlCeMetadataExtensionsTest
    {
        [Fact]
        public void Can_get_and_set_column_name()
        {
            var modelBuilder = GetModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .Metadata;

            Assert.Equal("Name", property.SqlCe().ColumnName);
            Assert.Equal("Name", ((IProperty)property).SqlCe().ColumnName);

            property.Relational().ColumnName = "Eman";

            Assert.Equal("Name", property.Name);
            Assert.Equal("Eman", property.Relational().ColumnName);
            Assert.Equal("Eman", property.SqlCe().ColumnName);
            Assert.Equal("Eman", ((IProperty)property).SqlCe().ColumnName);

            property.SqlCe().ColumnName = "MyNameIs";

            Assert.Equal("Name", property.Name);
            Assert.Equal("MyNameIs", property.Relational().ColumnName);
            Assert.Equal("MyNameIs", property.SqlCe().ColumnName);
            Assert.Equal("MyNameIs", ((IProperty)property).SqlCe().ColumnName);

            property.SqlCe().ColumnName = null;

            Assert.Equal("Name", property.Name);
            Assert.Equal("Name", property.Relational().ColumnName);
            Assert.Equal("Name", property.SqlCe().ColumnName);
            Assert.Equal("Name", ((IProperty)property).SqlCe().ColumnName);
        }

        [Fact]
        public void Can_get_and_set_table_name()
        {
            var modelBuilder = GetModelBuilder();

            var entityType = modelBuilder
                .Entity<Customer>()
                .Metadata;

            Assert.Equal("Customer", entityType.SqlCe().TableName);
            Assert.Equal("Customer", ((IEntityType)entityType).SqlCe().TableName);

            entityType.Relational().TableName = "Customizer";

            Assert.Equal("Customer", entityType.DisplayName());
            Assert.Equal("Customizer", entityType.Relational().TableName);
            Assert.Equal("Customizer", entityType.SqlCe().TableName);
            Assert.Equal("Customizer", ((IEntityType)entityType).SqlCe().TableName);

            entityType.SqlCe().TableName = "Custardizer";

            Assert.Equal("Customer", entityType.DisplayName());
            Assert.Equal("Custardizer", entityType.Relational().TableName);
            Assert.Equal("Custardizer", entityType.SqlCe().TableName);
            Assert.Equal("Custardizer", ((IEntityType)entityType).SqlCe().TableName);

            entityType.SqlCe().TableName = null;

            Assert.Equal("Customer", entityType.DisplayName());
            Assert.Equal("Customer", entityType.Relational().TableName);
            Assert.Equal("Customer", entityType.SqlCe().TableName);
            Assert.Equal("Customer", ((IEntityType)entityType).SqlCe().TableName);
        }

        [Fact]
        public void Can_get_schema_name()
        {
            var modelBuilder = new ModelBuilder(new ConventionSet());

            var entityType = modelBuilder
                .Entity<Customer>()
                .Metadata;

            Assert.Null(entityType.SqlCe().Schema);
            Assert.Null(((IEntityType)entityType).SqlCe().Schema);

            entityType.Relational().Schema = "db0";

            Assert.Equal("db0", entityType.SqlCe().Schema);
            Assert.Equal("db0", ((IEntityType)entityType).SqlCe().Schema);
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
            Assert.Null(property.SqlCe().ColumnType);
            Assert.Null(((IProperty)property).SqlCe().ColumnType);

            property.Relational().ColumnType = "nvarchar(max)";

            Assert.Equal("nvarchar(max)", property.Relational().ColumnType);
            Assert.Equal("nvarchar(max)", property.SqlCe().ColumnType);
            Assert.Equal("nvarchar(max)", ((IProperty)property).SqlCe().ColumnType);

            property.SqlCe().ColumnType = "nvarchar(verstappen)";

            Assert.Equal("nvarchar(verstappen)", property.Relational().ColumnType);
            Assert.Equal("nvarchar(verstappen)", property.SqlCe().ColumnType);
            Assert.Equal("nvarchar(verstappen)", ((IProperty)property).SqlCe().ColumnType);

            property.SqlCe().ColumnType = null;

            Assert.Null(property.Relational().ColumnType);
            Assert.Null(property.SqlCe().ColumnType);
            Assert.Null(((IProperty)property).SqlCe().ColumnType);
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
            Assert.Null(property.SqlCe().DefaultValueSql);
            Assert.Null(((IProperty)property).SqlCe().DefaultValueSql);

            property.Relational().DefaultValueSql = "newsequentialid()";

            Assert.Equal("newsequentialid()", property.Relational().DefaultValueSql);
            Assert.Equal("newsequentialid()", property.SqlCe().DefaultValueSql);
            Assert.Equal("newsequentialid()", ((IProperty)property).SqlCe().DefaultValueSql);

            property.SqlCe().DefaultValueSql = "expressyourself()";

            Assert.Equal("expressyourself()", property.Relational().DefaultValueSql);
            Assert.Equal("expressyourself()", property.SqlCe().DefaultValueSql);
            Assert.Equal("expressyourself()", ((IProperty)property).SqlCe().DefaultValueSql);

            property.SqlCe().DefaultValueSql = null;

            Assert.Null(property.Relational().DefaultValueSql);
            Assert.Null(property.SqlCe().DefaultValueSql);
            Assert.Null(((IProperty)property).SqlCe().DefaultValueSql);
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
            Assert.Equal("PK_Customer", key.SqlCe().Name);
            Assert.Equal("PK_Customer", ((IKey)key).SqlCe().Name);

            key.Relational().Name = "PrimaryKey";

            Assert.Equal("PrimaryKey", key.Relational().Name);
            Assert.Equal("PrimaryKey", key.SqlCe().Name);
            Assert.Equal("PrimaryKey", ((IKey)key).SqlCe().Name);

            key.SqlCe().Name = "PrimarySchool";

            Assert.Equal("PrimarySchool", key.Relational().Name);
            Assert.Equal("PrimarySchool", key.SqlCe().Name);
            Assert.Equal("PrimarySchool", ((IKey)key).SqlCe().Name);

            key.SqlCe().Name = null;

            Assert.Equal("PK_Customer", key.Relational().Name);
            Assert.Equal("PK_Customer", key.SqlCe().Name);
            Assert.Equal("PK_Customer", ((IKey)key).SqlCe().Name);
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

            index.SqlCe().Name = "DexKnows";

            Assert.Equal("DexKnows", index.Relational().Name);
            Assert.Equal("DexKnows", ((IIndex)index).Relational().Name);

            index.SqlCe().Name = null;

            Assert.Equal("IX_Customer_Id", index.Relational().Name);
            Assert.Equal("IX_Customer_Id", ((IIndex)index).Relational().Name);
        }

        private static ModelBuilder GetModelBuilder() => SqlCeTestHelpers.Instance.CreateConventionBuilder();

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
