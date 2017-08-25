using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using System;
using System.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Tests.Migrations
{
    public class SqlCeModelDifferTest : MigrationsModelDifferTestBase
    {
        [Fact]
        public void Create_table_overridden()
        {
            Execute(
                _ => { },
                modelBuilder => modelBuilder.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id").HasName("PK_People");
                        x.ToTable("People");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var addTableOperation = Assert.IsType<CreateTableOperation>(operations[0]);
                    Assert.Equal(null, addTableOperation.Schema);
                    Assert.Equal("People", addTableOperation.Name);

                    Assert.Equal("PK_People", addTableOperation.PrimaryKey.Name);
                });
        }

        [Fact]
        public void Rename_table_overridden()
        {
            Execute(
                source => source.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                    }),
                target => target.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id").HasName("PK_Person");
                        x.ToTable("People");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var addTableOperation = Assert.IsType<RenameTableOperation>(operations[0]);
                    Assert.Equal("Person", addTableOperation.Name);
                    Assert.Equal(null, addTableOperation.NewSchema);
                    Assert.Equal("People", addTableOperation.NewName);
                });
        }

        [Fact]
        public void Drop_table_overridden()
        {
            Execute(
                modelBuilder => modelBuilder.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.ToTable("People");
                    }),
                _ => { },
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var addTableOperation = Assert.IsType<DropTableOperation>(operations[0]);
                    Assert.Equal(null, addTableOperation.Schema);
                    Assert.Equal("People", addTableOperation.Name);
                });
        }

        [Fact]
        public void Add_column_with_dependencies()
        {
            Execute(
                source => source.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id").HasName("PK_People");
                        x.ToTable("People");
                    }),
                modelBuilder => modelBuilder.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id").HasName("PK_People");
                        x.ToTable("People");
                        x.Property<string>("FirstName");
                        x.Property<string>("FullName").HasComputedColumnSql("[FirstName] + [LastName]");
                        x.Property<string>("LastName");
                    }),
                operations =>
                {
                    Assert.Equal(3, operations.Count);

                    var columnOperation = Assert.IsType<AddColumnOperation>(operations[2]);
                    Assert.Equal("[FirstName] + [LastName]", columnOperation.ComputedColumnSql);
                });
        }


        [Fact]
        public void Rename_column_overridden()
        {
            Execute(
                source => source.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value");
                    }),
                target => target.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value").HasColumnName("PersonValue");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var addTableOperation = Assert.IsType<RenameColumnOperation>(operations[0]);
                    Assert.Equal("Person", addTableOperation.Table);
                    Assert.Equal("Value", addTableOperation.Name);
                    Assert.Equal("PersonValue", addTableOperation.NewName);
                });
        }

        [Fact]
        public void Alter_column_overridden()
        {
            Execute(
                source => source.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value");
                    }),
                target => target.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value")
                            .HasColumnType("varchar(8000)")
                            .HasDefaultValueSql("1 + 1");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var addTableOperation = Assert.IsType<AlterColumnOperation>(operations[0]);
                    Assert.Equal("Person", addTableOperation.Table);
                    Assert.Equal("Value", addTableOperation.Name);
                    Assert.Equal("varchar(8000)", addTableOperation.ColumnType);
                    Assert.Equal("1 + 1", addTableOperation.DefaultValueSql);
                });
        }

        [Fact]
        public void Alter_column_identity()
        {
            Execute(
                source => source.Entity(
                    "Lamb",
                    x =>
                    {
                        x.ToTable("Lamb", "bah");
                        x.Property<int>("Id").ValueGeneratedNever();
                        x.HasKey("Id");
                    }),
                target => target.Entity(
                    "Lamb",
                    x =>
                    {
                        x.ToTable("Lamb", "bah");
                        x.Property<int>("Id").ValueGeneratedOnAdd();
                        x.HasKey("Id");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var operation = Assert.IsType<AlterColumnOperation>(operations[0]);
                    Assert.Equal("bah", operation.Schema);
                    Assert.Equal("Lamb", operation.Table);
                    Assert.Equal("Id", operation.Name);
                    Assert.Equal(SqlCeAnnotationNames.Identity, operation["SqlCe:ValueGeneration"]);
                });
        }

        [Fact]
        public void Alter_column_non_key_identity()
        {
            Execute(
                source => source.Entity(
                    "Lamb",
                    x =>
                    {
                        x.ToTable("Lamb", "bah");
                        x.Property<int>("Num").ValueGeneratedNever();
                        x.Property<int>("Id");
                        x.HasKey("Id");
                    }),
                target => target.Entity(
                    "Lamb",
                    x =>
                    {
                        x.ToTable("Lamb", "bah");
                        x.Property<int>("Num").ValueGeneratedOnAdd();
                        x.Property<int>("Id");
                        x.HasKey("Id");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var operation = Assert.IsType<AlterColumnOperation>(operations[0]);
                    Assert.Equal("bah", operation.Schema);
                    Assert.Equal("Lamb", operation.Table);
                    Assert.Equal("Num", operation.Name);
                    Assert.Equal(SqlCeAnnotationNames.Identity, operation["SqlCe:ValueGeneration"]);
                });
        }

        [Fact]
        public void Add_column_overridden()
        {
            Execute(
                source => source.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                    }),
                target => target.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value").HasColumnName("PersonValue");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var addTableOperation = Assert.IsType<AddColumnOperation>(operations[0]);
                    Assert.Equal("Person", addTableOperation.Table);
                    Assert.Equal("PersonValue", addTableOperation.Name);
                });
        }

        [Fact]
        public void Drop_column_overridden()
        {
            Execute(
                source => source.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value").HasColumnName("PersonValue");
                    }),
                target => target.Entity(
                    "Person",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.HasKey("Id");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var addTableOperation = Assert.IsType<DropColumnOperation>(operations[0]);
                    Assert.Equal("Person", addTableOperation.Table);
                    Assert.Equal("PersonValue", addTableOperation.Name);
                });
        }

        [Fact]
        public void Add_unique_constraint_overridden()
        {
            Execute(
                source => source.Entity(
                    "Ewe",
                    x =>
                    {
                        x.ToTable("Ewe", "bah");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("AlternateId");
                    }),
                target => target.Entity(
                    "Ewe",
                    x =>
                    {
                        x.ToTable("Ewe", "bah");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("AlternateId");
                        x.HasAlternateKey("AlternateId").HasName("AK_Ewe");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var operation = Assert.IsType<AddUniqueConstraintOperation>(operations[0]);
                    Assert.Equal("bah", operation.Schema);
                    Assert.Equal("Ewe", operation.Table);
                    Assert.Equal("AK_Ewe", operation.Name);
                });
        }

        [Fact]
        public void Drop_unique_constraint_overridden()
        {
            Execute(
                source => source.Entity(
                    "Ewe",
                    x =>
                    {
                        x.ToTable("Ewe", "bah");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("AlternateId");
                        x.HasAlternateKey("AlternateId").HasName("AK_Ewe");
                    }),
                target => target.Entity(
                    "Ewe",
                    x =>
                    {
                        x.ToTable("Ewe", "bah");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("AlternateId");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var operation = Assert.IsType<DropUniqueConstraintOperation>(operations[0]);
                    Assert.Equal("bah", operation.Schema);
                    Assert.Equal("Ewe", operation.Table);
                    Assert.Equal("AK_Ewe", operation.Name);
                });
        }

        [Fact]
        public void Add_foreign_key_overridden()
        {
            Execute(
                source => source.Entity(
                    "Amoeba",
                    x =>
                    {
                        x.ToTable("Amoeba", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("ParentId");
                    }),
                target => target.Entity(
                    "Amoeba",
                    x =>
                    {
                        x.ToTable("Amoeba", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("ParentId");
                        x.HasOne("Amoeba").WithMany().HasForeignKey("ParentId").HasConstraintName("FK_Amoeba_Parent");
                    }),
                operations =>
                {
                    Assert.Equal(2, operations.Count);

                    var createIndexOperation = Assert.IsType<CreateIndexOperation>(operations[0]);
                    Assert.Equal("dbo", createIndexOperation.Schema);
                    Assert.Equal("Amoeba", createIndexOperation.Table);
                    Assert.Equal("IX_Amoeba_ParentId", createIndexOperation.Name);

                    var addFkOperation = Assert.IsType<AddForeignKeyOperation>(operations[1]);
                    Assert.Equal("dbo", addFkOperation.Schema);
                    Assert.Equal("Amoeba", addFkOperation.Table);
                    Assert.Equal("FK_Amoeba_Parent", addFkOperation.Name);
                });
        }

        [Fact]
        public void Drop_foreign_key_overridden()
        {
            Execute(
                source => source.Entity(
                    "Anemone",
                    x =>
                    {
                        x.ToTable("Anemone", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("ParentId");
                        x.HasOne("Anemone").WithMany().HasForeignKey("ParentId").HasConstraintName("FK_Anemone_Parent");
                    }),
                target => target.Entity(
                    "Anemone",
                    x =>
                    {
                        x.ToTable("Anemone", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("ParentId");
                    }),
                operations =>
                {
                    Assert.Equal(2, operations.Count);

                    var dropFkOperation = Assert.IsType<DropForeignKeyOperation>(operations[0]);
                    Assert.Equal("dbo", dropFkOperation.Schema);
                    Assert.Equal("Anemone", dropFkOperation.Table);
                    Assert.Equal("FK_Anemone_Parent", dropFkOperation.Name);

                    var dropIndexOperation = Assert.IsType<DropIndexOperation>(operations[1]);
                    Assert.Equal("dbo", dropIndexOperation.Schema);
                    Assert.Equal("Anemone", dropIndexOperation.Table);
                    Assert.Equal("IX_Anemone_ParentId", dropIndexOperation.Name);
                });
        }

        [Fact]
        public void Rename_index_overridden()
        {
            Execute(
                source => source.Entity(
                    "Donkey",
                    x =>
                    {
                        x.ToTable("Donkey", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value");
                        x.HasIndex("Value");
                    }),
                target => target.Entity(
                    "Donkey",
                    x =>
                    {
                        x.ToTable("Donkey", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value");
                        x.HasIndex("Value").HasName("IX_dbo.Donkey_Value");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var operation = Assert.IsType<RenameIndexOperation>(operations[0]);
                    Assert.Equal("dbo", operation.Schema);
                    Assert.Equal("Donkey", operation.Table);
                    Assert.Equal("IX_Donkey_Value", operation.Name);
                    Assert.Equal("IX_dbo.Donkey_Value", operation.NewName);
                });
        }

        [Fact]
        public void Create_index_overridden()
        {
            Execute(
                source => source.Entity(
                    "Hippo",
                    x =>
                    {
                        x.ToTable("Hippo", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value");
                    }),
                target => target.Entity(
                    "Hippo",
                    x =>
                    {
                        x.ToTable("Hippo", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value");
                        x.HasIndex("Value").HasName("IX_HipVal");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var operation = Assert.IsType<CreateIndexOperation>(operations[0]);
                    Assert.Equal("dbo", operation.Schema);
                    Assert.Equal("Hippo", operation.Table);
                    Assert.Equal("IX_HipVal", operation.Name);
                });
        }

        [Fact]
        public void Drop_index_overridden()
        {
            Execute(
                source => source.Entity(
                    "Horse",
                    x =>
                    {
                        x.ToTable("Horse", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value");
                        x.HasIndex("Value").HasName("IX_HorseVal");
                    }),
                target => target.Entity(
                    "Horse",
                    x =>
                    {
                        x.ToTable("Horse", "dbo");
                        x.Property<int>("Id");
                        x.HasKey("Id");
                        x.Property<int>("Value");
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var operation = Assert.IsType<DropIndexOperation>(operations[0]);
                    Assert.Equal("dbo", operation.Schema);
                    Assert.Equal("Horse", operation.Table);
                    Assert.Equal("IX_HorseVal", operation.Name);
                });
        }

        [Fact]
        public void Alter_column_rowversion()
        {
            Execute(
                source => source.Entity(
                    "Toad",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.Property<byte[]>("Version");
                    }),
                target => target.Entity(
                    "Toad",
                    x =>
                    {
                        x.Property<int>("Id");
                        x.Property<byte[]>("Version")
                            .ValueGeneratedOnAddOrUpdate()
                            .IsConcurrencyToken();
                    }),
                operations =>
                {
                    Assert.Equal(1, operations.Count);

                    var operation = Assert.IsType<AlterColumnOperation>(operations[0]);
                    Assert.Equal("Toad", operation.Table);
                    Assert.Equal("Version", operation.Name);
                    Assert.True(operation.IsRowVersion);
                    Assert.True(operation.IsDestructiveChange);
                });
        }

        protected override ModelBuilder CreateModelBuilder() => SqlCeTestHelpers.Instance.CreateConventionBuilder();

        protected override MigrationsModelDiffer CreateModelDiffer()
            => new MigrationsModelDiffer(
        new SqlCeTypeMapper(new RelationalTypeMapperDependencies()),
        new SqlCeMigrationsAnnotationProvider(new MigrationsAnnotationProviderDependencies()));
    }
}