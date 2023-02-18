// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Migrations.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xunit;
using Assert = Xunit.Assert;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    //Idempotent is ignored
    public class JetMigrationsSqlGeneratorTest : MigrationsSqlGeneratorTestBase
    {
        [ConditionalFact]
        public virtual void AddColumnOperation_identity_legacy()
        {
            Generate(
                new AddColumnOperation
                {
                    Table = "People",
                    Name = "Id",
                    ClrType = typeof(int),
                    ColumnType = "int",
                    DefaultValue = 0,
                    IsNullable = false,
                    [JetAnnotationNames.ValueGenerationStrategy] =
                        JetValueGenerationStrategy.IdentityColumn
                });

            AssertSql(
                @"ALTER TABLE `People` ADD `Id` counter NOT NULL;
");
        }

        public override void AddColumnOperation_without_column_type()
        {
            base.AddColumnOperation_without_column_type();

            AssertSql(
                @"ALTER TABLE `People` ADD `Alias` longchar NOT NULL;
");
        }

        public override void AddColumnOperation_with_unicode_no_model()
        {
            base.AddColumnOperation_with_unicode_no_model();

            AssertSql(
                @"ALTER TABLE `Person` ADD `Name` longchar NULL;
");
        }

        public override void AddColumnOperation_with_fixed_length_no_model()
        {
            base.AddColumnOperation_with_fixed_length_no_model();

            AssertSql(
                @"ALTER TABLE `Person` ADD `Name` char(100) NULL;
");
        }

        public override void AddColumnOperation_with_maxLength_no_model()
        {
            base.AddColumnOperation_with_maxLength_no_model();

            AssertSql(
                @"ALTER TABLE `Person` ADD `Name` varchar(30) NULL;
");
        }

        public override void AddColumnOperation_with_maxLength_overridden()
        {
            base.AddColumnOperation_with_maxLength_overridden();

            AssertSql(
                @"ALTER TABLE `Person` ADD `Name` varchar(32) NULL;
");
        }

        public override void AddColumnOperation_with_precision_and_scale_overridden()
        {
            base.AddColumnOperation_with_precision_and_scale_overridden();

            AssertSql(
                @"ALTER TABLE `Person` ADD `Pi` decimal(15,10) NOT NULL;
");
        }

        public override void AddColumnOperation_with_precision_and_scale_no_model()
        {
            base.AddColumnOperation_with_precision_and_scale_no_model();

            AssertSql(
                @"ALTER TABLE `Person` ADD `Pi` decimal(20,7) NOT NULL;
");
        }

        public override void AddColumnOperation_with_unicode_overridden()
        {
            base.AddColumnOperation_with_unicode_overridden();

            AssertSql(
                @"ALTER TABLE `Person` ADD `Name` longchar NULL;
");
        }

        [ConditionalFact]
        public virtual void AddColumnOperation_with_rowversion_overridden()
        {
            Generate(
                modelBuilder => modelBuilder.Entity<Person>().Property<byte[]>("RowVersion"),
                new AddColumnOperation
                {
                    Table = "Person",
                    Name = "RowVersion",
                    ClrType = typeof(byte[]),
                    IsRowVersion = true,
                    IsNullable = true
                });

            AssertSql(
                @"ALTER TABLE `Person` ADD `RowVersion` varbinary(8) NULL;
");
        }

        [ConditionalFact]
        public virtual void AddColumnOperation_with_rowversion_no_model()
        {
            Generate(
                new AddColumnOperation
                {
                    Table = "Person",
                    Name = "RowVersion",
                    ClrType = typeof(byte[]),
                    IsRowVersion = true,
                    IsNullable = true
                });

            AssertSql(
                @"ALTER TABLE `Person` ADD `RowVersion` varbinary(8) NULL;
");
        }

        public override void AlterColumnOperation_without_column_type()
        {
            base.AlterColumnOperation_without_column_type();

            AssertSql(
                @"ALTER TABLE `People` ALTER COLUMN `LuckyNumber` DROP DEFAULT;
ALTER TABLE `People` ALTER COLUMN `LuckyNumber` integer NOT NULL;
");
        }

        public override void AddForeignKeyOperation_without_principal_columns()
        {
            base.AddForeignKeyOperation_without_principal_columns();

            AssertSql(
                @"ALTER TABLE `People` ADD FOREIGN KEY (`SpouseId`) REFERENCES `People`;
");
        }

        [ConditionalFact]
        public virtual void AlterColumnOperation_with_identity_legacy()
        {
            Generate(
                new AlterColumnOperation
                {
                    Table = "People",
                    Name = "Id",
                    ClrType = typeof(int),
                    [JetAnnotationNames.ValueGenerationStrategy] =
                        JetValueGenerationStrategy.IdentityColumn
                });

            AssertSql(
                @"ALTER TABLE `People` ALTER COLUMN `Id` DROP DEFAULT;
ALTER TABLE `People` ALTER COLUMN `Id` integer NOT NULL;
");
        }

        [ConditionalFact]
        public virtual void AlterColumnOperation_with_index_no_oldColumn()
        {
            Generate(
                modelBuilder => modelBuilder
                    .HasAnnotation(CoreAnnotationNames.ProductVersion, "1.0.0-rtm")
                    .Entity<Person>(
                        x =>
                        {
                            x.Property<string>("Name").HasMaxLength(30);
                            x.HasIndex("Name");
                        }),
                new AlterColumnOperation
                {
                    Table = "Person",
                    Name = "Name",
                    ClrType = typeof(string),
                    MaxLength = 30,
                    IsNullable = true,
                    OldColumn = new AddColumnOperation()
                });

            AssertSql(
                @"ALTER TABLE `Person` ALTER COLUMN `Name` DROP DEFAULT;
ALTER TABLE `Person` ALTER COLUMN `Name` varchar(30) NULL;
");
        }

        [ConditionalFact]
        public virtual void AlterColumnOperation_with_added_index()
        {
            Generate(
                modelBuilder => modelBuilder
                    .HasAnnotation(CoreAnnotationNames.ProductVersion, "1.1.0")
                    .Entity<Person>(
                        x =>
                        {
                            x.Property<string>("Name").HasMaxLength(30);
                            x.HasIndex("Name");
                        }),
                new AlterColumnOperation
                {
                    Table = "Person",
                    Name = "Name",
                    ClrType = typeof(string),
                    MaxLength = 30,
                    IsNullable = true,
                    OldColumn = new AddColumnOperation { ClrType = typeof(string), IsNullable = true }
                },
                new CreateIndexOperation
                {
                    Name = "IX_Person_Name",
                    Table = "Person",
                    Columns = new[] { "Name" }
                });

            AssertSql(
                @"ALTER TABLE `Person` ALTER COLUMN `Name` DROP DEFAULT;
ALTER TABLE `Person` ALTER COLUMN `Name` varchar(30) NULL;
GO

CREATE INDEX `IX_Person_Name` ON `Person` (`Name`);
");
        }

        [ConditionalFact]
        public virtual void AlterColumnOperation_with_added_index_no_oldType()
        {
            Generate(
                modelBuilder => modelBuilder
                    .HasAnnotation(CoreAnnotationNames.ProductVersion, "2.1.0")
                    .Entity<Person>(
                        x =>
                        {
                            x.Property<string>("Name");
                            x.HasIndex("Name");
                        }),
                new AlterColumnOperation
                {
                    Table = "Person",
                    Name = "Name",
                    ClrType = typeof(string),
                    IsNullable = true,
                    OldColumn = new AddColumnOperation { ClrType = typeof(string), IsNullable = true }
                },
                new CreateIndexOperation
                {
                    Name = "IX_Person_Name",
                    Table = "Person",
                    Columns = new[] { "Name" }
                });

            AssertSql(
                @"ALTER TABLE `Person` ALTER COLUMN `Name` DROP DEFAULT;
ALTER TABLE `Person` ALTER COLUMN `Name` varchar(255) NULL;
GO

CREATE INDEX `IX_Person_Name` ON `Person` (`Name`);
");
        }

        [ConditionalFact]
        public virtual void AlterColumnOperation_identity_legacy()
        {
            Generate(
                modelBuilder => modelBuilder.HasAnnotation(CoreAnnotationNames.ProductVersion, "1.1.0"),
                new AlterColumnOperation
                {
                    Table = "Person",
                    Name = "Id",
                    ClrType = typeof(long),
                    [JetAnnotationNames.ValueGenerationStrategy] = JetValueGenerationStrategy.IdentityColumn,
                    OldColumn = new AddColumnOperation
                    {
                        ClrType = typeof(int),
                        [JetAnnotationNames.ValueGenerationStrategy] = JetValueGenerationStrategy.IdentityColumn
                    }
                });

            AssertSql(
                @"ALTER TABLE `Person` ALTER COLUMN `Id` DROP DEFAULT;
ALTER TABLE `Person` ALTER COLUMN `Id` integer NOT NULL;
");
        }

        [ConditionalFact]
        public virtual void AlterColumnOperation_add_identity_legacy()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => Generate(
                    modelBuilder => modelBuilder.HasAnnotation(CoreAnnotationNames.ProductVersion, "1.1.0"),
                    new AlterColumnOperation
                    {
                        Table = "Person",
                        Name = "Id",
                        ClrType = typeof(int),
                        [JetAnnotationNames.ValueGenerationStrategy] = JetValueGenerationStrategy.IdentityColumn,
                        OldColumn = new AddColumnOperation { ClrType = typeof(int) }
                    }));

            Assert.Equal(JetStrings.AlterIdentityColumn, ex.Message);
        }

        [ConditionalFact]
        public virtual void AlterColumnOperation_remove_identity_legacy()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => Generate(
                    modelBuilder => modelBuilder.HasAnnotation(CoreAnnotationNames.ProductVersion, "1.1.0"),
                    new AlterColumnOperation
                    {
                        Table = "Person",
                        Name = "Id",
                        ClrType = typeof(int),
                        OldColumn = new AddColumnOperation
                        {
                            ClrType = typeof(int),
                            [JetAnnotationNames.ValueGenerationStrategy] = JetValueGenerationStrategy.IdentityColumn
                        }
                    }));

            Assert.Equal(JetStrings.AlterIdentityColumn, ex.Message);
        }

        [ConditionalFact]
        public virtual void CreateDatabaseOperation()
        {
            Generate(
                new JetCreateDatabaseOperation { Name = "Northwind" });

            AssertSql(
                @"CREATE DATABASE 'Northwind';
");
        }

//         [ConditionalFact]
//         public virtual void CreateDatabaseOperation_with_filename()
//         {
//             Generate(
//                 new JetCreateDatabaseOperation { Name = "Northwind", FileName = "Narf.mdf" });
//
//             var expectedFile = Path.GetFullPath("Narf.mdf");
//             var expectedLog = Path.GetFullPath("Narf_log.ldf");
//
//             AssertSql(
//                 $@"CREATE DATABASE [Northwind]
// ON (NAME = 'Narf', FILENAME = '{expectedFile}')
// LOG ON (NAME = 'Narf_log', FILENAME = '{expectedLog}');
// GO
//
// IF SERVERPROPERTY('EngineEdition') <> 5
// BEGIN
//     ALTER DATABASE [Northwind] SET READ_COMMITTED_SNAPSHOT ON;
// END;
// ");
//         }

//         [ConditionalFact]
//         public virtual void CreateDatabaseOperation_with_filename_and_datadirectory()
//         {
//             var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
//
//             Generate(
//                 new JetCreateDatabaseOperation { Name = "Northwind", FileName = "|DataDirectory|Narf.mdf" });
//
//             var expectedFile = Path.Combine(baseDirectory, "Narf.mdf");
//             var expectedLog = Path.Combine(baseDirectory, "Narf_log.ldf");
//
//             AssertSql(
//                 $@"CREATE DATABASE [Northwind]
// ON (NAME = 'Narf', FILENAME = '{expectedFile}')
// LOG ON (NAME = 'Narf_log', FILENAME = '{expectedLog}');
// GO
//
// IF SERVERPROPERTY('EngineEdition') <> 5
// BEGIN
//     ALTER DATABASE [Northwind] SET READ_COMMITTED_SNAPSHOT ON;
// END;
// ");
//         }

//         [ConditionalFact]
//         public virtual void CreateDatabaseOperation_with_filename_and_custom_datadirectory()
//         {
//             var dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
//
//             AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory);
//
//             Generate(
//                 new JetCreateDatabaseOperation { Name = "Northwind", FileName = "|DataDirectory|Narf.mdf" });
//
//             AppDomain.CurrentDomain.SetData("DataDirectory", null);
//
//             var expectedFile = Path.Combine(dataDirectory, "Narf.mdf");
//             var expectedLog = Path.Combine(dataDirectory, "Narf_log.ldf");
//
//             AssertSql(
//                 $@"CREATE DATABASE [Northwind]
// ON (NAME = 'Narf', FILENAME = '{expectedFile}')
// LOG ON (NAME = 'Narf_log', FILENAME = '{expectedLog}');
// GO
//
// IF SERVERPROPERTY('EngineEdition') <> 5
// BEGIN
//     ALTER DATABASE [Northwind] SET READ_COMMITTED_SNAPSHOT ON;
// END;
// ");
//         }

//         [ConditionalFact]
//         public virtual void CreateDatabaseOperation_with_collation()
//         {
//             Generate(
//                 new JetCreateDatabaseOperation { Name = "Northwind", Collation = "German_PhoneBook_CI_AS" });
//
//             AssertSql(
//                 @"CREATE DATABASE [Northwind]
// COLLATE German_PhoneBook_CI_AS;
// GO
//
// IF SERVERPROPERTY('EngineEdition') <> 5
// BEGIN
//     ALTER DATABASE [Northwind] SET READ_COMMITTED_SNAPSHOT ON;
// END;
// ");
//         }

        [ConditionalFact]
        public virtual void AlterDatabaseOperation_collation()
        {
            Generate(
                new AlterDatabaseOperation { Collation = "German_PhoneBook_CI_AS" });

            Assert.Contains(
                "COLLATE German_PhoneBook_CI_AS",
                Sql);
        }

        // [ConditionalFact]
        // public virtual void AlterDatabaseOperation_memory_optimized()
        // {
        //     Generate(
        //         new AlterDatabaseOperation { [JetAnnotationNames.MemoryOptimized] = true });
        //
        //     Assert.Contains(
        //         "CONTAINS MEMORY_OPTIMIZED_DATA;",
        //         Sql);
        // }

        [ConditionalFact]
        public virtual void DropDatabaseOperation()
        {
            Generate(
                new JetDropDatabaseOperation { Name = "Northwind" });

            AssertSql(
                @"DROP DATABASE 'Northwind';
");
        }

        [ConditionalFact(Skip = "Jet does not support sequences")]
        public virtual void MoveSequenceOperation_legacy()
        {
            Generate(
                new RenameSequenceOperation
                {
                    Name = "EntityFrameworkHiLoSequence",
                    Schema = "dbo",
                    NewSchema = "my"
                });

            AssertSql(
                @"ALTER SCHEMA [my] TRANSFER [dbo].[EntityFrameworkHiLoSequence];
");
        }

        [ConditionalFact]
        public virtual void MoveTableOperation_legacy()
        {
            Generate(
                new RenameTableOperation
                {
                    Name = "People",
                    Schema = "dbo",
                    NewSchema = "hr"
                });

            AssertSql(
                @"ALTER SCHEMA [hr] TRANSFER [dbo].`People`;
");
        }

        [ConditionalFact]
        public virtual void RenameIndexOperations_throws_when_no_table()
        {
            var migrationBuilder = new MigrationBuilder("Jet");

            migrationBuilder.RenameIndex(
                name: "IX_OldIndex",
                newName: "IX_NewIndex");

            var ex = Assert.Throws<InvalidOperationException>(
                () => Generate(migrationBuilder.Operations.ToArray()));

            Assert.Equal(JetStrings.IndexTableRequired, ex.Message);
        }

        [ConditionalFact(Skip = "Jet does not support sequences")]
        public virtual void RenameSequenceOperation_legacy()
        {
            Generate(
                new RenameSequenceOperation
                {
                    Name = "EntityFrameworkHiLoSequence",
                    Schema = "dbo",
                    NewName = "MySequence"
                });

            AssertSql(
                @"EXEC sp_rename N'[dbo].[EntityFrameworkHiLoSequence]', N'MySequence';
");
        }

        [ConditionalFact]
        public override void RenameTableOperation_legacy()
        {
            base.RenameTableOperation_legacy();

            AssertSql(
                @"ALTER TABLE `People` RENAME TO `Person`;
");
        }

        public override void RenameTableOperation()
        {
            base.RenameTableOperation();

            AssertSql(
                @"ALTER TABLE `People` RENAME TO `Person`;
");
        }

        [ConditionalFact]
        public virtual void SqlOperation_handles_backslash()
        {
            Generate(
                new SqlOperation { Sql = @"-- Multiline \" + EOL + "comment" });

            AssertSql(
                @"-- Multiline comment
");
        }

        [ConditionalFact]
        public virtual void SqlOperation_ignores_sequential_gos()
        {
            Generate(
                new SqlOperation { Sql = "-- Ready set" + EOL + "GO" + EOL + "GO" });

            AssertSql(
                @"-- Ready set
");
        }

        [ConditionalFact]
        public virtual void SqlOperation_handles_go()
        {
            Generate(
                new SqlOperation { Sql = "-- I" + EOL + "go" + EOL + "-- Too" });

            AssertSql(
                @"-- I
GO

-- Too
");
        }

        [ConditionalFact]
        public virtual void SqlOperation_handles_go_with_count()
        {
            Generate(
                new SqlOperation { Sql = "-- I" + EOL + "GO 2" });

            AssertSql(
                @"-- I
GO

-- I
");
        }

        [ConditionalFact]
        public virtual void SqlOperation_ignores_non_go()
        {
            Generate(
                new SqlOperation { Sql = "-- I GO 2" });

            AssertSql(
                @"-- I GO 2
");
        }

        public override void SqlOperation()
        {
            base.SqlOperation();

            AssertSql(
                @"-- I <3 DDL
");
        }

        public override void InsertDataOperation_all_args_spatial()
        {
            base.InsertDataOperation_all_args_spatial();

            AssertSql(
                @"IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE `Name` IN (N'Id', N'Full Name', N'Geometry') AND [object_id] = OBJECT_ID(N'[dbo].`People`'))
    SET IDENTITY_INSERT [dbo].`People` ON;
INSERT INTO [dbo].`People` ([Id], [Full Name], [Geometry])
VALUES (0, NULL, NULL),
(1, 'Daenerys Targaryen', NULL),
(2, 'John Snow', NULL),
(3, 'Arya Stark', NULL),
(4, 'Harry Strickland', NULL),
(5, 'The Imp', NULL),
(6, 'The Kingslayer', NULL),
(7, 'Aemon Targaryen', geography::Parse('GEOMETRYCOLLECTION (LINESTRING (1.1 2.2 NULL, 2.2 2.2 NULL, 2.2 1.1 NULL, 7.1 7.2 NULL), LINESTRING (7.1 7.2 NULL, 20.2 20.2 NULL, 20.2 1.1 NULL, 70.1 70.2 NULL), MULTIPOINT ((1.1 2.2 NULL), (2.2 2.2 NULL), (2.2 1.1 NULL)), POLYGON ((1.1 2.2 NULL, 2.2 2.2 NULL, 2.2 1.1 NULL, 1.1 2.2 NULL)), POLYGON ((10.1 20.2 NULL, 20.2 20.2 NULL, 20.2 10.1 NULL, 10.1 20.2 NULL)), POINT (1.1 2.2 3.3), MULTILINESTRING ((1.1 2.2 NULL, 2.2 2.2 NULL, 2.2 1.1 NULL, 7.1 7.2 NULL), (7.1 7.2 NULL, 20.2 20.2 NULL, 20.2 1.1 NULL, 70.1 70.2 NULL)), MULTIPOLYGON (((10.1 20.2 NULL, 20.2 20.2 NULL, 20.2 10.1 NULL, 10.1 20.2 NULL)), ((1.1 2.2 NULL, 2.2 2.2 NULL, 2.2 1.1 NULL, 1.1 2.2 NULL))))'));
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE `Name` IN (N'Id', N'Full Name', N'Geometry') AND [object_id] = OBJECT_ID(N'[dbo].`People`'))
    SET IDENTITY_INSERT [dbo].`People` OFF;
");
        }

        // The test data we're using is geographic but is represented in NTS as a GeometryCollection
        protected override string GetGeometryCollectionStoreType()
            => "geography";

        public override void InsertDataOperation_required_args()
        {
            base.InsertDataOperation_required_args();

            AssertSql(
                @"INSERT INTO `People` (`First Name`)
VALUES ('John');
");
        }

        public override void InsertDataOperation_required_args_composite()
        {
            base.InsertDataOperation_required_args_composite();

            AssertSql(
                @"INSERT INTO `People` (`First Name`, `Last Name`)
VALUES ('John', 'Snow');
");
        }

        public override void InsertDataOperation_required_args_multiple_rows()
        {
            base.InsertDataOperation_required_args_multiple_rows();

            AssertSql(
                @"INSERT INTO `People` (`First Name`)
VALUES ('John'),
('Daenerys');
");
        }

        public override void InsertDataOperation_throws_for_unsupported_column_types()
        {
            base.InsertDataOperation_throws_for_unsupported_column_types();
        }

        public override void DeleteDataOperation_all_args()
        {
            base.DeleteDataOperation_all_args();

            AssertSql(
                @"DELETE FROM `People`
WHERE `First Name` = 'Hodor';
SELECT @@ROWCOUNT;

DELETE FROM `People`
WHERE `First Name` = 'Daenerys';
SELECT @@ROWCOUNT;

DELETE FROM `People`
WHERE `First Name` = 'John';
SELECT @@ROWCOUNT;

DELETE FROM `People`
WHERE `First Name` = 'Arya';
SELECT @@ROWCOUNT;

DELETE FROM `People`
WHERE `First Name` = 'Harry';
SELECT @@ROWCOUNT;

");
        }

        public override void DeleteDataOperation_all_args_composite()
        {
            base.DeleteDataOperation_all_args_composite();

            AssertSql(
                @"DELETE FROM `People`
WHERE `First Name` = 'Hodor' AND `Last Name` IS NULL;
SELECT @@ROWCOUNT;

DELETE FROM `People`
WHERE `First Name` = 'Daenerys' AND `Last Name` = 'Targaryen';
SELECT @@ROWCOUNT;

DELETE FROM `People`
WHERE `First Name` = 'John' AND `Last Name` = 'Snow';
SELECT @@ROWCOUNT;

DELETE FROM `People`
WHERE `First Name` = 'Arya' AND `Last Name` = 'Stark';
SELECT @@ROWCOUNT;

DELETE FROM `People`
WHERE `First Name` = 'Harry' AND `Last Name` = 'Strickland';
SELECT @@ROWCOUNT;

");
        }

        public override void DeleteDataOperation_required_args()
        {
            base.DeleteDataOperation_required_args();

            AssertSql(
                @"DELETE FROM `People`
WHERE `Last Name` = 'Snow';
SELECT @@ROWCOUNT;

");
        }

        public override void DeleteDataOperation_required_args_composite()
        {
            base.DeleteDataOperation_required_args_composite();

            AssertSql(
                @"DELETE FROM `People`
WHERE `First Name` = 'John' AND `Last Name` = 'Snow';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_all_args()
        {
            base.UpdateDataOperation_all_args();

            AssertSql(
                @"UPDATE `People` SET `Birthplace` = 'Winterfell', `House Allegiance` = 'Stark', `Culture` = 'Northmen'
WHERE `First Name` = 'Hodor';
SELECT @@ROWCOUNT;

UPDATE `People` SET `Birthplace` = 'Dragonstone', `House Allegiance` = 'Targaryen', `Culture` = 'Valyrian'
WHERE `First Name` = 'Daenerys';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_all_args_composite()
        {
            base.UpdateDataOperation_all_args_composite();

            AssertSql(
                @"UPDATE `People` SET `House Allegiance` = 'Stark'
WHERE `First Name` = 'Hodor' AND `Last Name` IS NULL;
SELECT @@ROWCOUNT;

UPDATE `People` SET `House Allegiance` = 'Targaryen'
WHERE `First Name` = 'Daenerys' AND `Last Name` = 'Targaryen';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_all_args_composite_multi()
        {
            base.UpdateDataOperation_all_args_composite_multi();

            AssertSql(
                @"UPDATE `People` SET `Birthplace` = 'Winterfell', `House Allegiance` = 'Stark', `Culture` = 'Northmen'
WHERE `First Name` = 'Hodor' AND `Last Name` IS NULL;
SELECT @@ROWCOUNT;

UPDATE `People` SET `Birthplace` = 'Dragonstone', `House Allegiance` = 'Targaryen', `Culture` = 'Valyrian'
WHERE `First Name` = 'Daenerys' AND `Last Name` = 'Targaryen';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_all_args_multi()
        {
            base.UpdateDataOperation_all_args_multi();

            AssertSql(
                @"UPDATE `People` SET `Birthplace` = 'Dragonstone', `House Allegiance` = 'Targaryen', `Culture` = 'Valyrian'
WHERE `First Name` = 'Daenerys';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_required_args()
        {
            base.UpdateDataOperation_required_args();

            AssertSql(
                @"UPDATE `People` SET `House Allegiance` = 'Targaryen'
WHERE `First Name` = 'Daenerys';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_required_args_composite()
        {
            base.UpdateDataOperation_required_args_composite();

            AssertSql(
                @"UPDATE `People` SET `House Allegiance` = 'Targaryen'
WHERE `First Name` = 'Daenerys' AND `Last Name` = 'Targaryen';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_required_args_composite_multi()
        {
            base.UpdateDataOperation_required_args_composite_multi();

            AssertSql(
                @"UPDATE `People` SET `Birthplace` = 'Dragonstone', `House Allegiance` = 'Targaryen', `Culture` = 'Valyrian'
WHERE `First Name` = 'Daenerys' AND `Last Name` = 'Targaryen';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_required_args_multi()
        {
            base.UpdateDataOperation_required_args_multi();

            AssertSql(
                @"UPDATE `People` SET `Birthplace` = 'Dragonstone', `House Allegiance` = 'Targaryen', `Culture` = 'Valyrian'
WHERE `First Name` = 'Daenerys';
SELECT @@ROWCOUNT;

");
        }

        public override void UpdateDataOperation_required_args_multiple_rows()
        {
            base.UpdateDataOperation_required_args_multiple_rows();

            AssertSql(
                @"UPDATE `People` SET `House Allegiance` = 'Stark'
WHERE `First Name` = 'Hodor';
SELECT @@ROWCOUNT;

UPDATE `People` SET `House Allegiance` = 'Targaryen'
WHERE `First Name` = 'Daenerys';
SELECT @@ROWCOUNT;

");
        }

        public override void DefaultValue_with_line_breaks(bool isUnicode)
        {
            base.DefaultValue_with_line_breaks(isUnicode);

            var expectedSql = @$"CREATE TABLE `TestLineBreaks` (
    `TestDefaultValue` longchar NOT NULL DEFAULT '' & CHR(13) & '' & CHR(10) & 'Various Line' & CHR(13) & 'Breaks' & CHR(10) & ''
);
";
            AssertSql(expectedSql);
        }

        public override void DefaultValue_with_line_breaks_2(bool isUnicode)
        {
            base.DefaultValue_with_line_breaks_2(isUnicode);

            var unicodePrefixForType = string.Empty;
            var expectedSql = @$"CREATE TABLE `TestLineBreaks` (
    `TestDefaultValue` longchar NOT NULL DEFAULT '0' & CHR(13) & '' & CHR(10) & '1' & CHR(13) & '' & CHR(10) & '2' & CHR(13) & '' & CHR(10) & '3' & CHR(13) & '' & CHR(10) & '4' & CHR(13) & '' & CHR(10) & '5' & CHR(13) & '' & CHR(10) & '6' & CHR(13) & '' & CHR(10) & '7' & CHR(13) & '' & CHR(10) & '8' & CHR(13) & '' & CHR(10) & '9' & CHR(13) & '' & CHR(10) & '10' & CHR(13) & '' & CHR(10) & '11' & CHR(13) & '' & CHR(10) & '12' & CHR(13) & '' & CHR(10) & '13' & CHR(13) & '' & CHR(10) & '14' & CHR(13) & '' & CHR(10) & '15' & CHR(13) & '' & CHR(10) & '16' & CHR(13) & '' & CHR(10) & '17' & CHR(13) & '' & CHR(10) & '18' & CHR(13) & '' & CHR(10) & '19' & CHR(13) & '' & CHR(10) & '20' & CHR(13) & '' & CHR(10) & '21' & CHR(13) & '' & CHR(10) & '22' & CHR(13) & '' & CHR(10) & '23' & CHR(13) & '' & CHR(10) & '24' & CHR(13) & '' & CHR(10) & '25' & CHR(13) & '' & CHR(10) & '26' & CHR(13) & '' & CHR(10) & '27' & CHR(13) & '' & CHR(10) & '28' & CHR(13) & '' & CHR(10) & '29' & CHR(13) & '' & CHR(10) & '30' & CHR(13) & '' & CHR(10) & '31' & CHR(13) & '' & CHR(10) & '32' & CHR(13) & '' & CHR(10) & '33' & CHR(13) & '' & CHR(10) & '34' & CHR(13) & '' & CHR(10) & '35' & CHR(13) & '' & CHR(10) & '36' & CHR(13) & '' & CHR(10) & '37' & CHR(13) & '' & CHR(10) & '38' & CHR(13) & '' & CHR(10) & '39' & CHR(13) & '' & CHR(10) & '40' & CHR(13) & '' & CHR(10) & '41' & CHR(13) & '' & CHR(10) & '42' & CHR(13) & '' & CHR(10) & '43' & CHR(13) & '' & CHR(10) & '44' & CHR(13) & '' & CHR(10) & '45' & CHR(13) & '' & CHR(10) & '46' & CHR(13) & '' & CHR(10) & '47' & CHR(13) & '' & CHR(10) & '48' & CHR(13) & '' & CHR(10) & '49' & CHR(13) & '' & CHR(10) & '50' & CHR(13) & '' & CHR(10) & '51' & CHR(13) & '' & CHR(10) & '52' & CHR(13) & '' & CHR(10) & '53' & CHR(13) & '' & CHR(10) & '54' & CHR(13) & '' & CHR(10) & '55' & CHR(13) & '' & CHR(10) & '56' & CHR(13) & '' & CHR(10) & '57' & CHR(13) & '' & CHR(10) & '58' & CHR(13) & '' & CHR(10) & '59' & CHR(13) & '' & CHR(10) & '60' & CHR(13) & '' & CHR(10) & '61' & CHR(13) & '' & CHR(10) & '62' & CHR(13) & '' & CHR(10) & '63' & CHR(13) & '' & CHR(10) & '64' & CHR(13) & '' & CHR(10) & '65' & CHR(13) & '' & CHR(10) & '66' & CHR(13) & '' & CHR(10) & '67' & CHR(13) & '' & CHR(10) & '68' & CHR(13) & '' & CHR(10) & '69' & CHR(13) & '' & CHR(10) & '70' & CHR(13) & '' & CHR(10) & '71' & CHR(13) & '' & CHR(10) & '72' & CHR(13) & '' & CHR(10) & '73' & CHR(13) & '' & CHR(10) & '74' & CHR(13) & '' & CHR(10) & '75' & CHR(13) & '' & CHR(10) & '76' & CHR(13) & '' & CHR(10) & '77' & CHR(13) & '' & CHR(10) & '78' & CHR(13) & '' & CHR(10) & '79' & CHR(13) & '' & CHR(10) & '80' & CHR(13) & '' & CHR(10) & '81' & CHR(13) & '' & CHR(10) & '82' & CHR(13) & '' & CHR(10) & '83' & CHR(13) & '' & CHR(10) & '84' & CHR(13) & '' & CHR(10) & '85' & CHR(13) & '' & CHR(10) & '86' & CHR(13) & '' & CHR(10) & '87' & CHR(13) & '' & CHR(10) & '88' & CHR(13) & '' & CHR(10) & '89' & CHR(13) & '' & CHR(10) & '90' & CHR(13) & '' & CHR(10) & '91' & CHR(13) & '' & CHR(10) & '92' & CHR(13) & '' & CHR(10) & '93' & CHR(13) & '' & CHR(10) & '94' & CHR(13) & '' & CHR(10) & '95' & CHR(13) & '' & CHR(10) & '96' & CHR(13) & '' & CHR(10) & '97' & CHR(13) & '' & CHR(10) & '98' & CHR(13) & '' & CHR(10) & '99' & CHR(13) & '' & CHR(10) & '100' & CHR(13) & '' & CHR(10) & '101' & CHR(13) & '' & CHR(10) & '102' & CHR(13) & '' & CHR(10) & '103' & CHR(13) & '' & CHR(10) & '104' & CHR(13) & '' & CHR(10) & '105' & CHR(13) & '' & CHR(10) & '106' & CHR(13) & '' & CHR(10) & '107' & CHR(13) & '' & CHR(10) & '108' & CHR(13) & '' & CHR(10) & '109' & CHR(13) & '' & CHR(10) & '110' & CHR(13) & '' & CHR(10) & '111' & CHR(13) & '' & CHR(10) & '112' & CHR(13) & '' & CHR(10) & '113' & CHR(13) & '' & CHR(10) & '114' & CHR(13) & '' & CHR(10) & '115' & CHR(13) & '' & CHR(10) & '116' & CHR(13) & '' & CHR(10) & '117' & CHR(13) & '' & CHR(10) & '118' & CHR(13) & '' & CHR(10) & '119' & CHR(13) & '' & CHR(10) & '120' & CHR(13) & '' & CHR(10) & '121' & CHR(13) & '' & CHR(10) & '122' & CHR(13) & '' & CHR(10) & '123' & CHR(13) & '' & CHR(10) & '124' & CHR(13) & '' & CHR(10) & '125' & CHR(13) & '' & CHR(10) & '126' & CHR(13) & '' & CHR(10) & '127' & CHR(13) & '' & CHR(10) & '128' & CHR(13) & '' & CHR(10) & '129' & CHR(13) & '' & CHR(10) & '130' & CHR(13) & '' & CHR(10) & '131' & CHR(13) & '' & CHR(10) & '132' & CHR(13) & '' & CHR(10) & '133' & CHR(13) & '' & CHR(10) & '134' & CHR(13) & '' & CHR(10) & '135' & CHR(13) & '' & CHR(10) & '136' & CHR(13) & '' & CHR(10) & '137' & CHR(13) & '' & CHR(10) & '138' & CHR(13) & '' & CHR(10) & '139' & CHR(13) & '' & CHR(10) & '140' & CHR(13) & '' & CHR(10) & '141' & CHR(13) & '' & CHR(10) & '142' & CHR(13) & '' & CHR(10) & '143' & CHR(13) & '' & CHR(10) & '144' & CHR(13) & '' & CHR(10) & '145' & CHR(13) & '' & CHR(10) & '146' & CHR(13) & '' & CHR(10) & '147' & CHR(13) & '' & CHR(10) & '148' & CHR(13) & '' & CHR(10) & '149' & CHR(13) & '' & CHR(10) & '150' & CHR(13) & '' & CHR(10) & '151' & CHR(13) & '' & CHR(10) & '152' & CHR(13) & '' & CHR(10) & '153' & CHR(13) & '' & CHR(10) & '154' & CHR(13) & '' & CHR(10) & '155' & CHR(13) & '' & CHR(10) & '156' & CHR(13) & '' & CHR(10) & '157' & CHR(13) & '' & CHR(10) & '158' & CHR(13) & '' & CHR(10) & '159' & CHR(13) & '' & CHR(10) & '160' & CHR(13) & '' & CHR(10) & '161' & CHR(13) & '' & CHR(10) & '162' & CHR(13) & '' & CHR(10) & '163' & CHR(13) & '' & CHR(10) & '164' & CHR(13) & '' & CHR(10) & '165' & CHR(13) & '' & CHR(10) & '166' & CHR(13) & '' & CHR(10) & '167' & CHR(13) & '' & CHR(10) & '168' & CHR(13) & '' & CHR(10) & '169' & CHR(13) & '' & CHR(10) & '170' & CHR(13) & '' & CHR(10) & '171' & CHR(13) & '' & CHR(10) & '172' & CHR(13) & '' & CHR(10) & '173' & CHR(13) & '' & CHR(10) & '174' & CHR(13) & '' & CHR(10) & '175' & CHR(13) & '' & CHR(10) & '176' & CHR(13) & '' & CHR(10) & '177' & CHR(13) & '' & CHR(10) & '178' & CHR(13) & '' & CHR(10) & '179' & CHR(13) & '' & CHR(10) & '180' & CHR(13) & '' & CHR(10) & '181' & CHR(13) & '' & CHR(10) & '182' & CHR(13) & '' & CHR(10) & '183' & CHR(13) & '' & CHR(10) & '184' & CHR(13) & '' & CHR(10) & '185' & CHR(13) & '' & CHR(10) & '186' & CHR(13) & '' & CHR(10) & '187' & CHR(13) & '' & CHR(10) & '188' & CHR(13) & '' & CHR(10) & '189' & CHR(13) & '' & CHR(10) & '190' & CHR(13) & '' & CHR(10) & '191' & CHR(13) & '' & CHR(10) & '192' & CHR(13) & '' & CHR(10) & '193' & CHR(13) & '' & CHR(10) & '194' & CHR(13) & '' & CHR(10) & '195' & CHR(13) & '' & CHR(10) & '196' & CHR(13) & '' & CHR(10) & '197' & CHR(13) & '' & CHR(10) & '198' & CHR(13) & '' & CHR(10) & '199' & CHR(13) & '' & CHR(10) & '200' & CHR(13) & '' & CHR(10) & '201' & CHR(13) & '' & CHR(10) & '202' & CHR(13) & '' & CHR(10) & '203' & CHR(13) & '' & CHR(10) & '204' & CHR(13) & '' & CHR(10) & '205' & CHR(13) & '' & CHR(10) & '206' & CHR(13) & '' & CHR(10) & '207' & CHR(13) & '' & CHR(10) & '208' & CHR(13) & '' & CHR(10) & '209' & CHR(13) & '' & CHR(10) & '210' & CHR(13) & '' & CHR(10) & '211' & CHR(13) & '' & CHR(10) & '212' & CHR(13) & '' & CHR(10) & '213' & CHR(13) & '' & CHR(10) & '214' & CHR(13) & '' & CHR(10) & '215' & CHR(13) & '' & CHR(10) & '216' & CHR(13) & '' & CHR(10) & '217' & CHR(13) & '' & CHR(10) & '218' & CHR(13) & '' & CHR(10) & '219' & CHR(13) & '' & CHR(10) & '220' & CHR(13) & '' & CHR(10) & '221' & CHR(13) & '' & CHR(10) & '222' & CHR(13) & '' & CHR(10) & '223' & CHR(13) & '' & CHR(10) & '224' & CHR(13) & '' & CHR(10) & '225' & CHR(13) & '' & CHR(10) & '226' & CHR(13) & '' & CHR(10) & '227' & CHR(13) & '' & CHR(10) & '228' & CHR(13) & '' & CHR(10) & '229' & CHR(13) & '' & CHR(10) & '230' & CHR(13) & '' & CHR(10) & '231' & CHR(13) & '' & CHR(10) & '232' & CHR(13) & '' & CHR(10) & '233' & CHR(13) & '' & CHR(10) & '234' & CHR(13) & '' & CHR(10) & '235' & CHR(13) & '' & CHR(10) & '236' & CHR(13) & '' & CHR(10) & '237' & CHR(13) & '' & CHR(10) & '238' & CHR(13) & '' & CHR(10) & '239' & CHR(13) & '' & CHR(10) & '240' & CHR(13) & '' & CHR(10) & '241' & CHR(13) & '' & CHR(10) & '242' & CHR(13) & '' & CHR(10) & '243' & CHR(13) & '' & CHR(10) & '244' & CHR(13) & '' & CHR(10) & '245' & CHR(13) & '' & CHR(10) & '246' & CHR(13) & '' & CHR(10) & '247' & CHR(13) & '' & CHR(10) & '248' & CHR(13) & '' & CHR(10) & '249' & CHR(13) & '' & CHR(10) & '250' & CHR(13) & '' & CHR(10) & '251' & CHR(13) & '' & CHR(10) & '252' & CHR(13) & '' & CHR(10) & '253' & CHR(13) & '' & CHR(10) & '254' & CHR(13) & '' & CHR(10) & '255' & CHR(13) & '' & CHR(10) & '256' & CHR(13) & '' & CHR(10) & '257' & CHR(13) & '' & CHR(10) & '258' & CHR(13) & '' & CHR(10) & '259' & CHR(13) & '' & CHR(10) & '260' & CHR(13) & '' & CHR(10) & '261' & CHR(13) & '' & CHR(10) & '262' & CHR(13) & '' & CHR(10) & '263' & CHR(13) & '' & CHR(10) & '264' & CHR(13) & '' & CHR(10) & '265' & CHR(13) & '' & CHR(10) & '266' & CHR(13) & '' & CHR(10) & '267' & CHR(13) & '' & CHR(10) & '268' & CHR(13) & '' & CHR(10) & '269' & CHR(13) & '' & CHR(10) & '270' & CHR(13) & '' & CHR(10) & '271' & CHR(13) & '' & CHR(10) & '272' & CHR(13) & '' & CHR(10) & '273' & CHR(13) & '' & CHR(10) & '274' & CHR(13) & '' & CHR(10) & '275' & CHR(13) & '' & CHR(10) & '276' & CHR(13) & '' & CHR(10) & '277' & CHR(13) & '' & CHR(10) & '278' & CHR(13) & '' & CHR(10) & '279' & CHR(13) & '' & CHR(10) & '280' & CHR(13) & '' & CHR(10) & '281' & CHR(13) & '' & CHR(10) & '282' & CHR(13) & '' & CHR(10) & '283' & CHR(13) & '' & CHR(10) & '284' & CHR(13) & '' & CHR(10) & '285' & CHR(13) & '' & CHR(10) & '286' & CHR(13) & '' & CHR(10) & '287' & CHR(13) & '' & CHR(10) & '288' & CHR(13) & '' & CHR(10) & '289' & CHR(13) & '' & CHR(10) & '290' & CHR(13) & '' & CHR(10) & '291' & CHR(13) & '' & CHR(10) & '292' & CHR(13) & '' & CHR(10) & '293' & CHR(13) & '' & CHR(10) & '294' & CHR(13) & '' & CHR(10) & '295' & CHR(13) & '' & CHR(10) & '296' & CHR(13) & '' & CHR(10) & '297' & CHR(13) & '' & CHR(10) & '298' & CHR(13) & '' & CHR(10) & '299' & CHR(13) & '' & CHR(10) & ''
);
";
            AssertSql(expectedSql);
        }

        [ConditionalFact]
        public virtual void AddColumn_generates_exec_when_computed_and_idempotent()
        {
            Generate(
                modelBuilder => { },
                migrationBuilder => migrationBuilder.AddColumn<int>(
                    name: "Column2",
                    table: "Table1",
                    computedColumnSql: "[Column1] + 1"),
                MigrationsSqlGenerationOptions.Idempotent);

            AssertSql(
                @"EXEC(N'ALTER TABLE [Table1] ADD [Column2] AS [Column1] + 1');
");
        }

        [ConditionalFact]
        public virtual void AddCheckConstraint_generates_exec_when_idempotent()
        {
            Generate(
                modelBuilder => { },
                migrationBuilder => migrationBuilder.AddCheckConstraint(
                    name: "CK_Table1",
                    table: "Table1",
                    "[Column1] BETWEEN 0 AND 100"),
                MigrationsSqlGenerationOptions.Idempotent);

            AssertSql(
                @"ALTER TABLE `Table1` ADD CONSTRAINT `CK_Table1` CHECK ([Column1] BETWEEN 0 AND 100);
");
        }

        [ConditionalFact]
        public virtual void CreateIndex_generates_exec_when_filter_and_idempotent()
        {
            Generate(
                modelBuilder => { },
                migrationBuilder => migrationBuilder.CreateIndex(
                    name: "IX_Table1_Column1",
                    table: "Table1",
                    column: "Column1",
                    filter: "[Column1] IS NOT NULL"),
                MigrationsSqlGenerationOptions.Idempotent);

            AssertSql(
                @"CREATE INDEX `IX_Table1_Column1` ON `Table1` (`Column1`) WHERE [Column1] IS NOT NULL;
");
        }

        [ConditionalFact]
        public virtual void CreateIndex_generates_exec_when_legacy_filter_and_idempotent()
        {
            Generate(
                modelBuilder =>
                {
                    modelBuilder
                        .HasAnnotation(CoreAnnotationNames.ProductVersion, "1.1.0")
                        .Entity("Table1").Property<int?>("Column1");
                },
                migrationBuilder => migrationBuilder.CreateIndex(
                    name: "IX_Table1_Column1",
                    table: "Table1",
                    column: "Column1",
                    unique: true),
                MigrationsSqlGenerationOptions.Idempotent);

            AssertSql(
                @"CREATE UNIQUE INDEX `IX_Table1_Column1` ON `Table1` (`Column1`) WHERE `Column1` IS NOT NULL';
");
        }

        [ConditionalFact]
        public virtual void AddColumnOperation_datetime_with_defaultValue_sql()
        {
            Generate(
                new AddColumnOperation
                {
                    Table = "People",
                    Schema = null,
                    Name = "Birthday",
                    ClrType = typeof(DateTime),
                    ColumnType = "datetime",
                    IsNullable = false,
                    DefaultValueSql = "#2019-01-01#"
                });

            AssertSql(
                $@"ALTER TABLE `People` ADD `Birthday` datetime NOT NULL DEFAULT #2019-01-01#;
");
        }
        
        public JetMigrationsSqlGeneratorTest()
            : base(
                JetTestHelpers.Instance,
                new ServiceCollection()/*.AddEntityFrameworkJetNetTopologySuite()*/,
                JetTestHelpers.Instance.AddProviderOptions(
                    ((IRelationalDbContextOptionsBuilderInfrastructure)
                        new JetDbContextOptionsBuilder(new DbContextOptionsBuilder())/*.UseNetTopologySuite()*/)
                    .OptionsBuilder).Options)
        {
        }
    }
}
