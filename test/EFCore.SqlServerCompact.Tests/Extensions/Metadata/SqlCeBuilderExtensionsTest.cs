using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Tests.Extensions.Metadata
{
    public class SqlCeBuilderExtensionsTest
    {
        [Fact]
        public void Can_set_column_name()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .HasColumnName("Eman");

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .HasColumnName("MyNameIs");

            var property = modelBuilder.Model.FindEntityType(typeof(Customer)).FindProperty("Name");

            Assert.Equal("Name", property.Name);
            Assert.Equal("MyNameIs", property.Relational().ColumnName);
            Assert.Equal("MyNameIs", property.SqlCe().ColumnName);
        }

        [Fact]
        public void Can_set_column_type()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .HasColumnType("nvarchar(42)");

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .HasColumnType("nvarchar(DA)");

            var property = modelBuilder.Model.FindEntityType(typeof(Customer)).FindProperty("Name");

            Assert.Equal("nvarchar(DA)", property.Relational().ColumnType);
            Assert.Equal("nvarchar(DA)", property.SqlCe().ColumnType);
        }

        [Fact]
        public void Can_set_column_default_expression()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .HasDefaultValueSql("VanillaCoke");

            var property = modelBuilder.Model.FindEntityType(typeof(Customer)).FindProperty("Name");

            Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .HasDefaultValueSql("CherryCoke");

            Assert.Equal("CherryCoke", property.Relational().DefaultValueSql);
            Assert.Equal("CherryCoke", property.SqlCe().DefaultValueSql);
            Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
        }

        [Fact]
        public void Setting_column_default_expression_does_not_modify_explicitly_set_value_generated()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .ValueGeneratedNever()
                .HasDefaultValueSql("VanillaCoke");

            var property = modelBuilder.Model.FindEntityType(typeof(Customer)).FindProperty("Name");

            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .HasDefaultValueSql("CherryCoke");

            Assert.Equal("CherryCoke", property.Relational().DefaultValueSql);
            Assert.Equal("CherryCoke", property.SqlCe().DefaultValueSql);
            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        }

        [Fact]
        public void Can_set_column_default_value()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Offset)
                .HasDefaultValue(new DateTime(1973, 9, 3, 0, 10, 0));

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Offset)
                .HasDefaultValue(new DateTime(2006, 9, 19, 19, 0, 0));

            var property = modelBuilder.Model.FindEntityType(typeof(Customer)).FindProperty("Offset");

            Assert.Equal(new DateTime(2006, 9, 19, 19, 0, 0), property.Relational().DefaultValue);
            Assert.Equal(new DateTime(2006, 9, 19, 19, 0, 0), property.SqlCe().DefaultValue);
        }

        [Fact]
        public void Setting_column_default_value_does_not_modify_explicitly_set_value_generated()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .ValueGeneratedNever()
                .HasDefaultValueSql("VanillaCoke");

            var property = modelBuilder.Model.FindEntityType(typeof(Customer)).FindProperty("Name");

            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);

            modelBuilder
                .Entity<Customer>()
                .Property(e => e.Name)
                .HasDefaultValueSql("CherryCoke");

            Assert.Equal("CherryCoke", property.Relational().DefaultValueSql);
            Assert.Equal("CherryCoke", property.SqlCe().DefaultValueSql);
            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        }

        [Fact]
        public void Can_set_key_name()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .HasKey(e => e.Id)
                .HasName("KeyLimePie")
                .HasName("LemonSupreme");

            var key = modelBuilder.Model.FindEntityType(typeof(Customer)).FindPrimaryKey();

            Assert.Equal("LemonSupreme", key.Relational().Name);
            Assert.Equal("LemonSupreme", key.SqlCe().Name);
        }

        [Fact]
        public void Can_set_foreign_key_name_for_one_to_many()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>().HasMany(e => e.Orders).WithOne(e => e.Customer)
                .HasConstraintName("LemonSupreme")
                .HasConstraintName("ChocolateLimes");

            var foreignKey = modelBuilder.Model.FindEntityType(typeof(Order)).GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Customer));

            Assert.Equal("ChocolateLimes", foreignKey.Relational().Name);

            modelBuilder
                .Entity<Customer>().HasMany(e => e.Orders).WithOne(e => e.Customer)
                .HasConstraintName(null);

            Assert.Equal("FK_Order_Customer_CustomerId", foreignKey.Relational().Name);
        }

        [Fact]
        public void Can_set_foreign_key_name_for_one_to_many_with_FK_specified()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>().HasMany(e => e.Orders).WithOne(e => e.Customer)
                .HasForeignKey(e => e.CustomerId)
                .HasConstraintName("LemonSupreme")
                .HasConstraintName("ChocolateLimes");

            var foreignKey = modelBuilder.Model.FindEntityType(typeof(Order)).GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Customer));

            Assert.Equal("ChocolateLimes", foreignKey.Relational().Name);
            Assert.Equal("ChocolateLimes", foreignKey.SqlCe().Name);
        }

        [Fact]
        public void Can_set_foreign_key_name_for_many_to_one()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Order>().HasOne(e => e.Customer).WithMany(e => e.Orders)
                .HasConstraintName("LemonSupreme")
                .HasConstraintName("ChocolateLimes");

            var foreignKey = modelBuilder.Model.FindEntityType(typeof(Order)).GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Customer));

            Assert.Equal("ChocolateLimes", foreignKey.Relational().Name);

            modelBuilder
                .Entity<Order>().HasOne(e => e.Customer).WithMany(e => e.Orders)
                .HasConstraintName(null);

            Assert.Equal("FK_Order_Customer_CustomerId", foreignKey.Relational().Name);
        }

        [Fact]
        public void Can_set_foreign_key_name_for_many_to_one_with_FK_specified()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Order>().HasOne(e => e.Customer).WithMany(e => e.Orders)
                .HasForeignKey(e => e.CustomerId)
                .HasConstraintName("LemonSupreme")
                .HasConstraintName("ChocolateLimes");

            var foreignKey = modelBuilder.Model.FindEntityType(typeof(Order)).GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Customer));

            Assert.Equal("ChocolateLimes", foreignKey.Relational().Name);
            Assert.Equal("ChocolateLimes", foreignKey.SqlCe().Name);
        }

        [Fact]
        public void Can_set_foreign_key_name_for_one_to_one()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Order>().HasOne(e => e.Details).WithOne(e => e.Order)
                .HasPrincipalKey<Order>(e => e.OrderId)
                .HasConstraintName("LemonSupreme")
                .HasConstraintName("ChocolateLimes");

            var foreignKey = modelBuilder.Model.FindEntityType(typeof(OrderDetails)).GetForeignKeys().Single();

            Assert.Equal("ChocolateLimes", foreignKey.Relational().Name);

            modelBuilder
                .Entity<Order>().HasOne(e => e.Details).WithOne(e => e.Order)
                .HasConstraintName(null);

            Assert.Equal("FK_OrderDetails_Order_OrderId", foreignKey.Relational().Name);
        }

        [Fact]
        public void Can_set_foreign_key_name_for_one_to_one_with_FK_specified()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Order>().HasOne(e => e.Details).WithOne(e => e.Order)
                .HasForeignKey<OrderDetails>(e => e.Id)
                .HasConstraintName("LemonSupreme")
                .HasConstraintName("ChocolateLimes");

            var foreignKey = modelBuilder.Model.FindEntityType(typeof(OrderDetails)).GetForeignKeys().Single();

            Assert.Equal("ChocolateLimes", foreignKey.Relational().Name);
            Assert.Equal("ChocolateLimes", foreignKey.SqlCe().Name);
        }

        [Fact]
        public void Can_set_index_name()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .HasIndex(e => e.Id)
                .HasName("Eeeendeeex")
                .HasName("Dexter");

            var index = modelBuilder.Model.FindEntityType(typeof(Customer)).GetIndexes().Single();

            Assert.Equal("Dexter", index.Relational().Name);
            Assert.Equal("Dexter", index.SqlCe().Name);
        }

        [Fact]
        public void Can_set_table_name()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>()
                .ToTable("Customizer")
                .ToTable("Custardizer");

            var entityType = modelBuilder.Model.FindEntityType(typeof(Customer));

            Assert.Equal("Customer", entityType.DisplayName());
            Assert.Equal("Custardizer", entityType.Relational().TableName);
            Assert.Equal("Custardizer", entityType.SqlCe().TableName);
        }

        [Fact]
        public void Can_set_table_name_non_generic()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity(typeof(Customer))
                .ToTable("Customizer")
                .ToTable("Custardizer");

            var entityType = modelBuilder.Model.FindEntityType(typeof(Customer));

            Assert.Equal("Customer", entityType.DisplayName());
            Assert.Equal("Custardizer", entityType.Relational().TableName);
            Assert.Equal("Custardizer", entityType.SqlCe().TableName);
        }

        [Fact]
        public void SqlCe_entity_methods_have_non_generic_overloads()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity(typeof(Customer))
                .ToTable("Will");

            modelBuilder
                .Entity<Customer>()
                .ToTable("Simon");
        }

        [Fact]
        public void SqlCe_property_methods_dont_break_out_of_the_generics2()
        {
            var modelBuilder = CreateConventionModelBuilder();

            AssertIsGeneric(
                modelBuilder
                    .Entity<Customer>()
                    .Property(e => e.Name)
                    .HasColumnName("Will"));

            AssertIsGeneric(
                modelBuilder
                    .Entity<Customer>()
                    .Property(e => e.Name)
                    .HasColumnType("Jay"));

            AssertIsGeneric(
                modelBuilder
                    .Entity<Customer>()
                    .Property(e => e.Name)
                    .HasDefaultValue("Neil"));
        }

        [Fact]
        public void SqlCe_property_methods_dont_break_out_of_the_generics()
        {
            var modelBuilder = CreateConventionModelBuilder();

            AssertIsGeneric(
                modelBuilder
                    .Entity<Customer>()
                    .Property(e => e.Name)
                    .HasColumnName("Will"));

            AssertIsGeneric(
                modelBuilder
                    .Entity<Customer>()
                    .Property(e => e.Name)
                    .HasColumnType("Jay"));

            AssertIsGeneric(
                modelBuilder
                    .Entity<Customer>()
                    .Property(e => e.Name)
                    .HasDefaultValueSql("Simon"));
        }

        [Fact]
        public void SqlCe_property_methods_have_non_generic_overloads()
        {
            var modelBuilder = CreateConventionModelBuilder();
            modelBuilder
                .Entity(typeof(Customer))
                .Property(typeof(string), "Name")
                .HasColumnName("Will");

            modelBuilder
                .Entity<Customer>()
                .Property(typeof(string), "Name")
                .HasColumnName("Jay");

            modelBuilder
                .Entity(typeof(Customer))
                .Property(typeof(string), "Name")
                .HasColumnType("Simon");

            modelBuilder
                .Entity<Customer>()
                .Property(typeof(string), "Name")
                .HasColumnType("Neil");

            modelBuilder
                .Entity(typeof(Customer))
                .Property(typeof(string), "Name")
                .HasDefaultValue("Simon");

            modelBuilder
                .Entity<Customer>()
                .Property(typeof(string), "Name")
                .HasDefaultValue("Neil");
        }

        [Fact]
        public void SqlCe_property_methods_have_non_generic_overloads2()
        {
            var modelBuilder = CreateConventionModelBuilder();
            modelBuilder
                .Entity(typeof(Customer))
                .Property(typeof(string), "Name")
                .HasColumnName("Will");

            modelBuilder
                .Entity<Customer>()
                .Property(typeof(string), "Name")
                .HasColumnName("Jay");

            modelBuilder
                .Entity(typeof(Customer))
                .Property(typeof(string), "Name")
                .HasColumnType("Simon");

            modelBuilder
                .Entity<Customer>()
                .Property(typeof(string), "Name")
                .HasColumnType("Neil");

            modelBuilder
                .Entity(typeof(Customer))
                .Property(typeof(string), "Name")
                .HasDefaultValueSql("Simon");

            modelBuilder
                .Entity<Customer>()
                .Property(typeof(string), "Name")
                .HasDefaultValueSql("Neil");
        }

        [Fact]
        public void SqlCe_relationship_methods_dont_break_out_of_the_generics()
        {
            var modelBuilder = CreateConventionModelBuilder();

            AssertIsGeneric(
                modelBuilder
                    .Entity<Customer>().HasMany(e => e.Orders)
                    .WithOne(e => e.Customer)
                    .HasConstraintName("Will"));

            AssertIsGeneric(
                modelBuilder
                    .Entity<Order>()
                    .HasOne(e => e.Customer)
                    .WithMany(e => e.Orders)
                    .HasConstraintName("Jay"));

            AssertIsGeneric(
                modelBuilder
                    .Entity<Order>()
                    .HasOne(e => e.Details)
                    .WithOne(e => e.Order)
                    .HasConstraintName("Simon"));
        }

        [Fact]
        public void SqlCe_relationship_methods_have_non_generic_overloads()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder
                .Entity<Customer>().HasMany(typeof(Order), "Orders")
                .WithOne("Customer")
                .HasConstraintName("Will");

            modelBuilder
                .Entity<Order>()
                .HasOne(e => e.Customer)
                .WithMany(e => e.Orders)
                .HasConstraintName("Jay");

            modelBuilder
                .Entity<Order>()
                .HasOne(e => e.Details)
                .WithOne(e => e.Order)
                .HasConstraintName("Simon");
        }

        private void AssertIsGeneric(EntityTypeBuilder<Customer> _)
        {
        }

        private void AssertIsGeneric(PropertyBuilder<string> _)
        {
        }

        private void AssertIsGeneric(PropertyBuilder<int> _)
        {
        }

        private void AssertIsGeneric(ReferenceCollectionBuilder<Customer, Order> _)
        {
        }

        private void AssertIsGeneric(ReferenceReferenceBuilder<Order, OrderDetails> _)
        {
        }

        protected virtual ModelBuilder CreateConventionModelBuilder()
        {
            return SqlCeTestHelpers.Instance.CreateConventionBuilder();
        }

        private class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime Offset { get; set; }

            public IEnumerable<Order> Orders { get; set; }
        }

        private class Order
        {
            public int OrderId { get; set; }

            public int CustomerId { get; set; }
            public Customer Customer { get; set; }

            public OrderDetails Details { get; set; }
        }

        private class OrderDetails
        {
            public int Id { get; set; }

            public int OrderId { get; set; }
            public Order Order { get; set; }
        }
    }
}
