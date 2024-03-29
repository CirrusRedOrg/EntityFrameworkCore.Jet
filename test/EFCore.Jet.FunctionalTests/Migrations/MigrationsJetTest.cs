// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable StringLiteralTypo
// ReSharper disable UnusedParameter.Local
// ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local

#nullable enable

namespace EntityFrameworkCore.Jet.FunctionalTests.Migrations;

public class MigrationsJetTest : MigrationsTestBase<MigrationsJetTest.MigrationsJetFixture>
{
    protected static string EOL
        => Environment.NewLine;

    public MigrationsJetTest(MigrationsJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Create_table()
    {
        await base.Create_table();

        AssertSql(
"""
CREATE TABLE [People] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NULL,
    CONSTRAINT [PK_People] PRIMARY KEY ([Id])
);
""");
    }

    public override async Task Create_table_all_settings()
    {
        await base.Create_table_all_settings();

        AssertSql(
"""
IF SCHEMA_ID(N'dbo2') IS NULL EXEC(N'CREATE SCHEMA [dbo2];');
""",
//
"""
CREATE TABLE [dbo2].[People] (
    [CustomId] int NOT NULL IDENTITY,
    [EmployerId] int NOT NULL,
    [SSN] nvarchar(11) COLLATE German_PhoneBook_CI_AS NOT NULL,
    CONSTRAINT [PK_People] PRIMARY KEY ([CustomId]),
    CONSTRAINT [AK_People_SSN] UNIQUE ([SSN]),
    CONSTRAINT [CK_People_EmployerId] CHECK ([EmployerId] > 0),
    CONSTRAINT [FK_People_Employers_EmployerId] FOREIGN KEY ([EmployerId]) REFERENCES [Employers] ([Id]) ON DELETE CASCADE
);
DECLARE @description AS sql_variant;
SET @description = N'Table comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'dbo2', 'TABLE', N'People';
SET @description = N'Employer ID comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'dbo2', 'TABLE', N'People', 'COLUMN', N'EmployerId';
""",
//
"""
CREATE INDEX [IX_People_EmployerId] ON [dbo2].[People] ([EmployerId]);
""");
    }

    public override async Task Create_table_no_key()
    {
        await base.Create_table_no_key();

        AssertSql(
"""
CREATE TABLE [Anonymous] (
    [SomeColumn] int NOT NULL
);
""");
    }

    public override async Task Create_table_with_comments()
    {
        await base.Create_table_with_comments();

        AssertSql(
"""
CREATE TABLE [People] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NULL,
    CONSTRAINT [PK_People] PRIMARY KEY ([Id])
);
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'Table comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People';
SET @description = N'Column comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'Name';
""");
    }

    public override async Task Create_table_with_multiline_comments()
    {
        await base.Create_table_with_multiline_comments();

        AssertSql(
"""
CREATE TABLE [People] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NULL,
    CONSTRAINT [PK_People] PRIMARY KEY ([Id])
);
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = CONCAT(N'This is a multi-line', NCHAR(13), NCHAR(10), N'table comment.', NCHAR(13), NCHAR(10), N'More information can', NCHAR(13), NCHAR(10), N'be found in the docs.');
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People';
SET @description = CONCAT(N'This is a multi-line', NCHAR(10), N'column comment.', NCHAR(10), N'More information can', NCHAR(10), N'be found in the docs.');
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'Name';
""");
    }

    public override async Task Create_table_with_computed_column(bool? stored)
    {
        await base.Create_table_with_computed_column(stored);

        var storedSql = stored == true ? " PERSISTED" : "";

        AssertSql(
$"""
CREATE TABLE [People] (
    [Id] int NOT NULL IDENTITY,
    [Sum] AS [X] + [Y]{storedSql},
    [X] int NOT NULL,
    [Y] int NOT NULL,
    CONSTRAINT [PK_People] PRIMARY KEY ([Id])
);
""");
    }

    [ConditionalFact]
    public virtual async Task Create_table_with_identity_column_value_converter()
    {
        await Test(
            _ => { },
            builder => builder.UseJetIdentityColumns()
                .Entity("People").Property<int>("IdentityColumn").HasConversion<short>().ValueGeneratedOnAdd(),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "IdentityColumn");
                Assert.Equal(ValueGenerated.OnAdd, column.ValueGenerated);
            });

        AssertSql(
"""
CREATE TABLE [People] (
    [IdentityColumn] smallint NOT NULL IDENTITY
);
""");
    }

    public override async Task Drop_table()
    {
        await base.Drop_table();

        AssertSql(
"""
DROP TABLE [People];
""");
    }

    public override async Task Alter_table_add_comment()
    {
        await base.Alter_table_add_comment();

        AssertSql(
"""
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'Table comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People';
""");
    }

    public override async Task Alter_table_add_comment_non_default_schema()
    {
        await base.Alter_table_add_comment_non_default_schema();

        AssertSql(
"""
DECLARE @description AS sql_variant;
SET @description = N'Table comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', N'SomeOtherSchema', 'TABLE', N'People';
""");
    }

    public override async Task Alter_table_change_comment()
    {
        await base.Alter_table_change_comment();

        AssertSql(
"""
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
EXEC sp_dropextendedproperty 'MS_Description', 'SCHEMA', @defaultSchema, 'TABLE', N'People';
SET @description = N'Table comment2';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People';
""");
    }

    public override async Task Alter_table_remove_comment()
    {
        await base.Alter_table_remove_comment();

        AssertSql(
"""
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
EXEC sp_dropextendedproperty 'MS_Description', 'SCHEMA', @defaultSchema, 'TABLE', N'People';
""");
    }

    public override async Task Rename_table()
    {
        await base.Rename_table();

        AssertSql(
"""
ALTER TABLE [People] DROP CONSTRAINT [PK_People];
""",
//
"""
EXEC sp_rename N'[People]', N'Persons';
""",
//
"""
ALTER TABLE [Persons] ADD CONSTRAINT [PK_Persons] PRIMARY KEY ([Id]);
""");
    }

    public override async Task Rename_table_with_primary_key()
    {
        await base.Rename_table_with_primary_key();

        AssertSql(
"""
ALTER TABLE [People] DROP CONSTRAINT [PK_People];
""",
//
"""
EXEC sp_rename N'[People]', N'Persons';
""",
//
"""
ALTER TABLE [Persons] ADD CONSTRAINT [PK_Persons] PRIMARY KEY ([Id]);
""");
    }

    public override async Task Move_table()
    {
        await base.Move_table();

        AssertSql(
"""
IF SCHEMA_ID(N'TestTableSchema') IS NULL EXEC(N'CREATE SCHEMA [TestTableSchema];');
""",
//
"""
ALTER SCHEMA [TestTableSchema] TRANSFER [TestTable];
""");
    }

    [ConditionalFact]
    public virtual async Task Move_table_into_default_schema()
    {
        await Test(
            builder => builder.Entity("TestTable")
                .ToTable("TestTable", "TestTableSchema")
                .Property<int>("Id"),
            builder => builder.Entity("TestTable")
                .Property<int>("Id"),
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal("dbo", table.Schema);
                Assert.Equal("TestTable", table.Name);
            });

        AssertSql(
"""
DECLARE @defaultSchema sysname = SCHEMA_NAME();
EXEC(N'ALTER SCHEMA [' + @defaultSchema + N'] TRANSFER [TestTableSchema].[TestTable];');
""");
    }

    public override async Task Create_schema()
    {
        await base.Create_schema();

        AssertSql(
"""
IF SCHEMA_ID(N'SomeOtherSchema') IS NULL EXEC(N'CREATE SCHEMA [SomeOtherSchema];');
""",
//
"""
CREATE TABLE [SomeOtherSchema].[People] (
    [Id] int NOT NULL IDENTITY,
    CONSTRAINT [PK_People] PRIMARY KEY ([Id])
);
""");
    }

    [ConditionalFact]
    public virtual async Task Create_schema_dbo_is_ignored()
    {
        await Test(
            builder => { },
            builder => builder.Entity("People")
                .ToTable("People", "dbo")
                .Property<int>("Id"),
            model => Assert.Equal("dbo", Assert.Single(model.Tables).Schema));

        AssertSql(
"""
CREATE TABLE [dbo].[People] (
    [Id] int NOT NULL IDENTITY,
    CONSTRAINT [PK_People] PRIMARY KEY ([Id])
);
""");
    }

    public override async Task Add_column_with_defaultValue_string()
    {
        await base.Add_column_with_defaultValue_string();

        AssertSql(
"""
ALTER TABLE [People] ADD [Name] nvarchar(max) NOT NULL DEFAULT N'John Doe';
""");
    }

    public override async Task Add_column_with_defaultValue_datetime()
    {
        await base.Add_column_with_defaultValue_datetime();

        AssertSql(
"""
ALTER TABLE [People] ADD [Birthday] datetime2 NOT NULL DEFAULT '2015-04-12T17:05:00.0000000';
""");
    }

    [ConditionalTheory]
    [InlineData(0, "", 1234567)]
    [InlineData(1, ".1", 1234567)]
    [InlineData(2, ".12", 1234567)]
    [InlineData(3, ".123", 1234567)]
    [InlineData(4, ".1234", 1234567)]
    [InlineData(5, ".12345", 1234567)]
    [InlineData(6, ".123456", 1234567)]
    [InlineData(7, ".1234567", 1234567)]
    [InlineData(7, ".1200000", 1200000)] //should this really output trailing zeros?
    public async Task Add_column_with_defaultValue_datetime_with_explicit_precision(int precision, string fractionalSeconds, int ticksToAdd)
    {
        await Test(
            builder => builder.Entity("People").Property<int>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<DateTime>("Birthday").HasPrecision(precision)
                .HasDefaultValue(new DateTime(2015, 4, 12, 17, 5, 0).AddTicks(ticksToAdd)),
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal(2, table.Columns.Count);
                var birthdayColumn = Assert.Single(table.Columns, c => c.Name == "Birthday");
                Assert.False(birthdayColumn.IsNullable);
            });

        AssertSql(
$"""
ALTER TABLE [People] ADD [Birthday] datetime2({precision}) NOT NULL DEFAULT '2015-04-12T17:05:00{fractionalSeconds}';
""");
    }

    [ConditionalTheory]
    [InlineData(0, "", 1234567)]
    [InlineData(1, ".1", 1234567)]
    [InlineData(2, ".12", 1234567)]
    [InlineData(3, ".123", 1234567)]
    [InlineData(4, ".1234", 1234567)]
    [InlineData(5, ".12345", 1234567)]
    [InlineData(6, ".123456", 1234567)]
    [InlineData(7, ".1234567", 1234567)]
    [InlineData(7, ".1200000", 1200000)] //should this really output trailing zeros?
    public async Task Add_column_with_defaultValue_datetimeoffset_with_explicit_precision(
        int precision,
        string fractionalSeconds,
        int ticksToAdd)
    {
        await Test(
            builder => builder.Entity("People").Property<int>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<DateTimeOffset>("Birthday").HasPrecision(precision)
                .HasDefaultValue(new DateTimeOffset(new DateTime(2015, 4, 12, 17, 5, 0).AddTicks(ticksToAdd), TimeSpan.FromHours(10))),
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal(2, table.Columns.Count);
                var birthdayColumn = Assert.Single(table.Columns, c => c.Name == "Birthday");
                Assert.False(birthdayColumn.IsNullable);
            });

        AssertSql(
$"""
ALTER TABLE [People] ADD [Birthday] datetimeoffset({precision}) NOT NULL DEFAULT '2015-04-12T17:05:00{fractionalSeconds}+10:00';
""");
    }

    [ConditionalTheory]
    [InlineData(0, "", 1234567)]
    [InlineData(1, ".1", 1234567)]
    [InlineData(2, ".12", 1234567)]
    [InlineData(3, ".123", 1234567)]
    [InlineData(4, ".1234", 1234567)]
    [InlineData(5, ".12345", 1234567)]
    [InlineData(6, ".123456", 1234567)]
    [InlineData(7, ".1234567", 1234567)]
    [InlineData(7, ".12", 1200000)]
    public async Task Add_column_with_defaultValue_time_with_explicit_precision(int precision, string fractionalSeconds, int ticksToAdd)
    {
        await Test(
            builder => builder.Entity("People").Property<int>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<TimeSpan>("Age").HasPrecision(precision)
                .HasDefaultValue(
                    TimeSpan.Parse("12:34:56", CultureInfo.InvariantCulture).Add(TimeSpan.FromTicks(ticksToAdd))),
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal(2, table.Columns.Count);
                var birthdayColumn = Assert.Single(table.Columns, c => c.Name == "Age");
                Assert.False(birthdayColumn.IsNullable);
            });

        AssertSql(
$"""
ALTER TABLE [People] ADD [Age] time({precision}) NOT NULL DEFAULT '12:34:56{fractionalSeconds}';
""");
    }

    [ConditionalFact]
    public virtual async Task Add_column_with_defaultValue_datetime_store_type()
    {
        await Test(
            builder => builder.Entity("People").Property<string>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<DateTime>("Birthday")
                .HasColumnType("datetime")
                .HasDefaultValue(new DateTime(2019, 1, 1)),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "Birthday");
                Assert.Contains("2019", column.DefaultValueSql);
            });

        AssertSql(
"""
ALTER TABLE [People] ADD [Birthday] datetime NOT NULL DEFAULT '2019-01-01T00:00:00.000';
""");
    }

    [ConditionalFact]
    public virtual async Task Add_column_with_defaultValue_smalldatetime_store_type()
    {
        await Test(
            builder => builder.Entity("People").Property<string>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<DateTime>("Birthday")
                .HasColumnType("smalldatetime")
                .HasDefaultValue(new DateTime(2019, 1, 1)),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "Birthday");
                Assert.Contains("2019", column.DefaultValueSql);
            });

        AssertSql(
"""
ALTER TABLE [People] ADD [Birthday] smalldatetime NOT NULL DEFAULT '2019-01-01T00:00:00';
""");
    }

    /*[ConditionalFact]
    public virtual async Task Add_column_with_rowversion()
    {
        await Test(
            builder => builder.Entity("People").Property<int>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<byte[]>("RowVersion").IsRowVersion(),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "RowVersion");
                Assert.Equal("rowversion", column.StoreType);
                Assert.True(column.IsRowVersion());
            });

        AssertSql(
"""
ALTER TABLE [People] ADD [RowVersion] rowversion NULL;
""");
    }*/

    /*[ConditionalFact]
    public virtual async Task Add_column_with_rowversion_and_value_conversion()
    {
        await Test(
            builder => builder.Entity("People").Property<int>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<ulong>("RowVersion")
                .IsRowVersion()
                .HasConversion<byte[]>(),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "RowVersion");
                Assert.Equal("rowversion", column.StoreType);
                Assert.True(column.IsRowVersion());
            });

        AssertSql(
"""
ALTER TABLE [People] ADD [RowVersion] rowversion NOT NULL;
""");
    }*/

    public override async Task Add_column_with_defaultValueSql()
    {
        await base.Add_column_with_defaultValueSql();

        AssertSql(
"""
ALTER TABLE [People] ADD [Sum] int NOT NULL DEFAULT (1 + 2);
""");
    }

    public override async Task Add_column_with_computedSql(bool? stored)
    {
        await base.Add_column_with_computedSql(stored);

        var computedColumnTypeSql = stored == true ? " PERSISTED" : "";

        AssertSql(
$"""
ALTER TABLE [People] ADD [Sum] AS [X] + [Y]{computedColumnTypeSql};
""");
    }

    [ConditionalFact]
    public virtual async Task Add_column_generates_exec_when_computed_and_idempotent()
    {
        await Test(
            builder => builder.Entity("People").Property<int>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<int>("IdPlusOne").HasComputedColumnSql("[Id] + 1"),
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal(2, table.Columns.Count);
                var column = Assert.Single(table.Columns, c => c.Name == "IdPlusOne");
                Assert.Equal("([Id]+(1))", column.ComputedColumnSql);
            },
            migrationsSqlGenerationOptions: MigrationsSqlGenerationOptions.Idempotent);

        AssertSql(
"""
EXEC(N'ALTER TABLE [People] ADD [IdPlusOne] AS [Id] + 1');
""");
    }

    public override async Task Add_column_with_required()
    {
        await base.Add_column_with_required();

        AssertSql(
"""
ALTER TABLE [People] ADD [Name] nvarchar(max) NOT NULL DEFAULT N'';
""");
    }

    public override async Task Add_column_with_ansi()
    {
        await base.Add_column_with_ansi();

        AssertSql(
"""
ALTER TABLE [People] ADD [Name] varchar(max) NULL;
""");
    }

    public override async Task Add_column_with_max_length()
    {
        await base.Add_column_with_max_length();

        AssertSql(
"""
ALTER TABLE [People] ADD [Name] nvarchar(30) NULL;
""");
    }

    public override async Task Add_column_with_max_length_on_derived()
    {
        await base.Add_column_with_max_length_on_derived();

        Assert.Empty(Fixture.TestSqlLoggerFactory.SqlStatements);
    }

    public override async Task Add_column_with_fixed_length()
    {
        await base.Add_column_with_fixed_length();

        AssertSql(
"""
ALTER TABLE [People] ADD [Name] nchar(100) NULL;
""");
    }

    public override async Task Add_column_with_comment()
    {
        await base.Add_column_with_comment();

        AssertSql(
"""
ALTER TABLE [People] ADD [FullName] nvarchar(max) NULL;
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'My comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'FullName';
""");
    }

    public override async Task Add_column_with_collation()
    {
        await base.Add_column_with_collation();

        AssertSql(
"""
ALTER TABLE [People] ADD [Name] nvarchar(max) COLLATE German_PhoneBook_CI_AS NULL;
""");
    }

    public override async Task Add_column_computed_with_collation(bool stored)
    {
        await base.Add_column_computed_with_collation(stored);

        AssertSql(
            stored
                ? """ALTER TABLE [People] ADD [Name] AS 'hello' COLLATE German_PhoneBook_CI_AS PERSISTED;"""
                : """ALTER TABLE [People] ADD [Name] AS 'hello' COLLATE German_PhoneBook_CI_AS;""");
    }

    public override async Task Add_column_shared()
    {
        await base.Add_column_shared();

        AssertSql();
    }

    public override async Task Add_column_with_check_constraint()
    {
        await base.Add_column_with_check_constraint();

        AssertSql(
"""
ALTER TABLE [People] ADD [DriverLicense] int NOT NULL DEFAULT 0;
""",
//
"""
ALTER TABLE [People] ADD CONSTRAINT [CK_People_Foo] CHECK ([DriverLicense] > 0);
""");
    }

    [ConditionalFact]
    public virtual async Task Add_column_identity()
    {
        await Test(
            builder => builder.Entity("People").Property<string>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<int>("IdentityColumn").UseJetIdentityColumn(),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "IdentityColumn");
                Assert.Equal(ValueGenerated.OnAdd, column.ValueGenerated);
            });

        AssertSql(
"""
ALTER TABLE [People] ADD [IdentityColumn] int NOT NULL IDENTITY;
""");
    }

    [ConditionalFact]
    public virtual async Task Add_column_identity_seed_increment()
    {
        await Test(
            builder => builder.Entity("People").Property<string>("Id"),
            builder => { },
            builder => builder.Entity("People").Property<int>("IdentityColumn").UseJetIdentityColumn(100, 5),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "IdentityColumn");
                Assert.Equal(ValueGenerated.OnAdd, column.ValueGenerated);
                // TODO: Do we not reverse-engineer identity facets?
                // Assert.Equal(100, column[JetAnnotationNames.IdentitySeed]);
                // Assert.Equal(5, column[JetAnnotationNames.IdentityIncrement]);
            });

        AssertSql(
"""
ALTER TABLE [People] ADD [IdentityColumn] int NOT NULL IDENTITY(100, 5);
""");
    }

    [ConditionalFact]
    public virtual async Task Add_column_identity_seed_increment_for_TPC()
    {
        await Test(
            builder =>
            {
                builder.Entity("Animal").UseTpcMappingStrategy().Property<string>("Id");
                builder.Entity("Cat").HasBaseType("Animal").ToTable("Cats");
                builder.Entity("Dog").HasBaseType("Animal").ToTable("Dogs");
            },
            builder => { },
            builder =>
            {
                builder.Entity("Animal")
                    .Property<int>("IdentityColumn");
                builder.Entity("Cat").ToTable("Cats", tb => tb.Property("IdentityColumn").UseJetIdentityColumn(1, 2));
                builder.Entity("Dog").ToTable("Dogs", tb => tb.Property("IdentityColumn").UseJetIdentityColumn(2, 2));
            },
            model =>
            {
                Assert.Collection(
                    model.Tables,
                    t =>
                    {
                        Assert.Equal("Animal", t.Name);
                        var column = Assert.Single(t.Columns, c => c.Name == "IdentityColumn");
                        Assert.Null(column.ValueGenerated);
                    },
                    t =>
                    {
                        Assert.Equal("Cats", t.Name);
                        var column = Assert.Single(t.Columns, c => c.Name == "IdentityColumn");
                        Assert.Equal(ValueGenerated.OnAdd, column.ValueGenerated);
                        // TODO: Do we not reverse-engineer identity facets?
                        // Assert.Equal(100, column[JetAnnotationNames.IdentitySeed]);
                        // Assert.Equal(5, column[JetAnnotationNames.IdentityIncrement]);
                    },
                    t =>
                    {
                        Assert.Equal("Dogs", t.Name);
                        var column = Assert.Single(t.Columns, c => c.Name == "IdentityColumn");
                        Assert.Equal(ValueGenerated.OnAdd, column.ValueGenerated);
                        // TODO: Do we not reverse-engineer identity facets?
                        // Assert.Equal(100, column[JetAnnotationNames.IdentitySeed]);
                        // Assert.Equal(5, column[JetAnnotationNames.IdentityIncrement]);
                    });
            });

        AssertSql(
"""
ALTER TABLE [Dogs] ADD [IdentityColumn] int NOT NULL IDENTITY(2, 2);
""",
//
"""
ALTER TABLE [Cats] ADD [IdentityColumn] int NOT NULL IDENTITY(1, 2);
""",
//
"""
ALTER TABLE [Animal] ADD [IdentityColumn] int NOT NULL DEFAULT 0;
""");
    }

    public override async Task Alter_column_change_type()
    {
        await base.Alter_column_change_type();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeColumn');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [SomeColumn] bigint NOT NULL;
""");
    }

    public override async Task Alter_column_make_required()
    {
        await base.Alter_column_make_required();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeColumn');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
UPDATE [People] SET [SomeColumn] = N'' WHERE [SomeColumn] IS NULL;
ALTER TABLE [People] ALTER COLUMN [SomeColumn] nvarchar(max) NOT NULL;
ALTER TABLE [People] ADD DEFAULT N'' FOR [SomeColumn];
""");
    }

    public override async Task Alter_column_make_required_with_null_data()
    {
        await base.Alter_column_make_required_with_null_data();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeColumn');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
UPDATE [People] SET [SomeColumn] = N'' WHERE [SomeColumn] IS NULL;
ALTER TABLE [People] ALTER COLUMN [SomeColumn] nvarchar(max) NOT NULL;
ALTER TABLE [People] ADD DEFAULT N'' FOR [SomeColumn];
""");
    }

    [ConditionalFact]
    public override async Task Alter_column_make_required_with_index()
    {
        await base.Alter_column_make_required_with_index();

        AssertSql(
"""
DROP INDEX [IX_People_SomeColumn] ON [People];
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeColumn');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
UPDATE [People] SET [SomeColumn] = N'' WHERE [SomeColumn] IS NULL;
ALTER TABLE [People] ALTER COLUMN [SomeColumn] nvarchar(450) NOT NULL;
ALTER TABLE [People] ADD DEFAULT N'' FOR [SomeColumn];
CREATE INDEX [IX_People_SomeColumn] ON [People] ([SomeColumn]);
""");
    }

    [ConditionalFact]
    public override async Task Alter_column_make_required_with_composite_index()
    {
        await base.Alter_column_make_required_with_composite_index();

        AssertSql(
"""
DROP INDEX [IX_People_FirstName_LastName] ON [People];
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'FirstName');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
UPDATE [People] SET [FirstName] = N'' WHERE [FirstName] IS NULL;
ALTER TABLE [People] ALTER COLUMN [FirstName] nvarchar(450) NOT NULL;
ALTER TABLE [People] ADD DEFAULT N'' FOR [FirstName];
CREATE INDEX [IX_People_FirstName_LastName] ON [People] ([FirstName], [LastName]);
""");
    }

    public override async Task Alter_column_make_computed(bool? stored)
    {
        await base.Alter_column_make_computed(stored);

        var computedColumnTypeSql = stored == true ? " PERSISTED" : "";

        AssertSql(
$"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Sum');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [Sum];
ALTER TABLE [People] ADD [Sum] AS [X] + [Y]{computedColumnTypeSql};
""");
    }

    public override async Task Alter_column_change_computed()
    {
        await base.Alter_column_change_computed();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Sum');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [Sum];
ALTER TABLE [People] ADD [Sum] AS [X] - [Y];
""");
    }

    public override async Task Alter_column_change_computed_recreates_indexes()
    {
        await base.Alter_column_change_computed_recreates_indexes();

        AssertSql(
"""
DROP INDEX [IX_People_Sum] ON [People];
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Sum');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [Sum];
ALTER TABLE [People] ADD [Sum] AS [X] - [Y];
""",
//
"""
CREATE INDEX [IX_People_Sum] ON [People] ([Sum]);
""");
    }

    public override async Task Alter_column_change_computed_type()
    {
        await base.Alter_column_change_computed_type();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Sum');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [Sum];
ALTER TABLE [People] ADD [Sum] AS [X] + [Y] PERSISTED;
""");
    }

    public override async Task Alter_column_make_non_computed()
    {
        await base.Alter_column_make_non_computed();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Sum');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [Sum];
ALTER TABLE [People] ADD [Sum] int NOT NULL;
""");
    }

    [ConditionalFact]
    public override async Task Alter_column_add_comment()
    {
        await base.Alter_column_add_comment();

        AssertSql(
"""
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'Some comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'Id';
""");
    }

    [ConditionalFact]
    public override async Task Alter_computed_column_add_comment()
    {
        await base.Alter_computed_column_add_comment();

        AssertSql(
"""
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'Some comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'SomeColumn';
""");
    }

    [ConditionalFact]
    public override async Task Alter_column_change_comment()
    {
        await base.Alter_column_change_comment();

        AssertSql(
"""
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
EXEC sp_dropextendedproperty 'MS_Description', 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'Id';
SET @description = N'Some comment2';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'Id';
""");
    }

    [ConditionalFact]
    public override async Task Alter_column_remove_comment()
    {
        await base.Alter_column_remove_comment();

        AssertSql(
"""
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
EXEC sp_dropextendedproperty 'MS_Description', 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'Id';
""");
    }

    [ConditionalFact]
    public override async Task Alter_column_set_collation()
    {
        await base.Alter_column_set_collation();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(max) COLLATE German_PhoneBook_CI_AS NULL;
""");
    }

    [ConditionalFact]
    public virtual async Task Alter_column_set_collation_with_index()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<string>("Name");
                    e.HasIndex("Name");
                }),
            builder => { },
            builder => builder.Entity("People").Property<string>("Name")
                .UseCollation(NonDefaultCollation),
            model =>
            {
                var nameColumn = Assert.Single(Assert.Single(model.Tables).Columns);
                Assert.Equal(NonDefaultCollation, nameColumn.Collation);
            });

        AssertSql(
"""
DROP INDEX [IX_People_Name] ON [People];
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) COLLATE German_PhoneBook_CI_AS NULL;
CREATE INDEX [IX_People_Name] ON [People] ([Name]);
""");
    }

    [ConditionalFact]
    public override async Task Alter_column_reset_collation()
    {
        await base.Alter_column_reset_collation();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(max) NULL;
""");
    }

    [ConditionalFact]
    public virtual async Task Alter_column_make_required_with_index_with_included_properties()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("SomeColumn");
                    e.Property<string>("SomeOtherColumn");
                    e.HasIndex("SomeColumn").IncludeProperties("SomeOtherColumn");
                }),
            builder => { },
            builder => builder.Entity("People").Property<string>("SomeColumn").IsRequired(),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "SomeColumn");
                Assert.False(column.IsNullable);
                var index = Assert.Single(table.Indexes);
                Assert.Equal(1, index.Columns.Count);
                Assert.Contains(table.Columns.Single(c => c.Name == "SomeColumn"), index.Columns);
                var includedColumns = (IReadOnlyList<string>?)index[JetAnnotationNames.Include];
                Assert.Null(includedColumns);
            });

        AssertSql(
"""
DROP INDEX [IX_People_SomeColumn] ON [People];
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeColumn');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
UPDATE [People] SET [SomeColumn] = N'' WHERE [SomeColumn] IS NULL;
ALTER TABLE [People] ALTER COLUMN [SomeColumn] nvarchar(450) NOT NULL;
ALTER TABLE [People] ADD DEFAULT N'' FOR [SomeColumn];
CREATE INDEX [IX_People_SomeColumn] ON [People] ([SomeColumn]) INCLUDE ([SomeOtherColumn]);
""");
    }

    [ConditionalFact]
    public virtual async Task Alter_column_with_index_no_narrowing()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("Name");
                    e.HasIndex("Name");
                }),
            builder => builder.Entity("People").Property<string>("Name").IsRequired(),
            builder => builder.Entity("People").Property<string>("Name").IsRequired(false),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "Name");
                Assert.True(column.IsNullable);
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NULL;
""");
    }

    [ConditionalFact]
    public virtual async Task Alter_column_with_index_included_column()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("Name");
                    e.Property<string>("FirstName");
                    e.Property<string>("LastName");
                    e.HasIndex("FirstName", "LastName").IncludeProperties("Name");
                }),
            builder => { },
            builder => builder.Entity("People").Property<string>("Name").HasMaxLength(30),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var index = Assert.Single(table.Indexes);
                Assert.Equal(2, index.Columns.Count);
                Assert.Contains(table.Columns.Single(c => c.Name == "FirstName"), index.Columns);
                Assert.Contains(table.Columns.Single(c => c.Name == "LastName"), index.Columns);
                var includedColumns = (IReadOnlyList<string>?)index[JetAnnotationNames.Include];
                Assert.Null(includedColumns);
            });

        AssertSql(
"""
DROP INDEX [IX_People_FirstName_LastName] ON [People];
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(30) NULL;
CREATE INDEX [IX_People_FirstName_LastName] ON [People] ([FirstName], [LastName]) INCLUDE ([Name]);
""");
    }

    [ConditionalFact]
    public virtual async Task Alter_column_add_identity()
    {
        var ex = await TestThrows<InvalidOperationException>(
            builder => builder.Entity("People").Property<int>("SomeColumn"),
            builder => builder.Entity("People").Property<int>("SomeColumn").UseJetIdentityColumn());

        Assert.Equal(JetStrings.AlterIdentityColumn, ex.Message);
    }

    [ConditionalFact]
    public virtual async Task Alter_column_remove_identity()
    {
        var ex = await TestThrows<InvalidOperationException>(
            builder => builder.Entity("People").Property<int>("SomeColumn").UseJetIdentityColumn(),
            builder => builder.Entity("People").Property<int>("SomeColumn"));

        Assert.Equal(JetStrings.AlterIdentityColumn, ex.Message);
    }

    [ConditionalFact]
    public virtual async Task Alter_column_change_type_with_identity()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<string>("Id");
                    e.Property<int>("IdentityColumn").UseJetIdentityColumn();
                }),
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<string>("Id");
                    e.Property<long>("IdentityColumn").UseJetIdentityColumn();
                }),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var column = Assert.Single(table.Columns, c => c.Name == "IdentityColumn");
                Assert.Equal("bigint", column.StoreType);
                Assert.Equal(ValueGenerated.OnAdd, column.ValueGenerated);
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'IdentityColumn');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [IdentityColumn] bigint NOT NULL;
""");
    }

    [ConditionalFact]
    public virtual async Task Alter_column_change_default()
    {
        await Test(
            builder => builder.Entity("People").Property<string>("Name"),
            builder => { },
            builder => builder.Entity("People").Property<string>("Name")
                .HasDefaultValue("Doe"),
            model =>
            {
                var nameColumn = Assert.Single(Assert.Single(model.Tables).Columns);
                Assert.Equal("(N'Doe')", nameColumn.DefaultValueSql);
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ADD DEFAULT N'Doe' FOR [Name];
""");
    }

    [ConditionalFact]
    public virtual async Task Alter_column_change_comment_with_default()
    {
        await Test(
            builder => builder.Entity("People").Property<string>("Name").HasDefaultValue("Doe"),
            builder => { },
            builder => builder.Entity("People").Property<string>("Name")
                .HasComment("Some comment"),
            model =>
            {
                var nameColumn = Assert.Single(Assert.Single(model.Tables).Columns);
                Assert.Equal("(N'Doe')", nameColumn.DefaultValueSql);
                Assert.Equal("Some comment", nameColumn.Comment);
            });

        AssertSql(
"""
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'Some comment';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'People', 'COLUMN', N'Name';
""");
    }

    public override async Task Drop_column()
    {
        await base.Drop_column();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeColumn');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [SomeColumn];
""");
    }

    public override async Task Drop_column_primary_key()
    {
        await base.Drop_column_primary_key();

        AssertSql(
"""
ALTER TABLE [People] DROP CONSTRAINT [PK_People];
""",
//
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Id');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [Id];
""");
    }

    public override async Task Drop_column_computed_and_non_computed_with_dependency()
    {
        await base.Drop_column_computed_and_non_computed_with_dependency();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Y');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [Y];
""",
//
"""
DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'X');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [People] DROP COLUMN [X];
""");
    }

    public override async Task Rename_column()
    {
        await base.Rename_column();

        AssertSql(
"""
EXEC sp_rename N'[People].[SomeColumn]', N'SomeOtherColumn', N'COLUMN';
""");
    }

    public override async Task Create_index()
    {
        await base.Create_index();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'FirstName');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [FirstName] nvarchar(450) NULL;
""",
//
"""
CREATE INDEX [IX_People_FirstName] ON [People] ([FirstName]);
""");
    }

    public override async Task Create_index_unique()
    {
        await base.Create_index_unique();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'LastName');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [LastName] nvarchar(450) NULL;
""",
//
"""
DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'FirstName');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [People] ALTER COLUMN [FirstName] nvarchar(450) NULL;
""",
//
"""
CREATE UNIQUE INDEX [IX_People_FirstName_LastName] ON [People] ([FirstName], [LastName]) WHERE [FirstName] IS NOT NULL AND [LastName] IS NOT NULL;
""");
    }

    public override async Task Create_index_descending()
    {
        await base.Create_index_descending();

        AssertSql(
"""
CREATE INDEX [IX_People_X] ON [People] ([X] DESC);
""");
    }

    public override async Task Create_index_descending_mixed()
    {
        await base.Create_index_descending_mixed();

        AssertSql(
"""
CREATE INDEX [IX_People_X_Y_Z] ON [People] ([X], [Y] DESC, [Z]);
""");
    }

    public override async Task Alter_index_make_unique()
    {
        await base.Alter_index_make_unique();

        AssertSql(
"""
DROP INDEX [IX_People_X] ON [People];
""",
//
"""
CREATE UNIQUE INDEX [IX_People_X] ON [People] ([X]);
""");
    }

    public override async Task Alter_index_change_sort_order()
    {
        await base.Alter_index_change_sort_order();

        AssertSql(
"""
DROP INDEX [IX_People_X_Y_Z] ON [People];
""",
//
"""
CREATE INDEX [IX_People_X_Y_Z] ON [People] ([X], [Y] DESC, [Z]);
""");
    }

    public override async Task Create_index_with_filter()
    {
        await base.Create_index_with_filter();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NULL;
""",
//
"""
CREATE INDEX [IX_People_Name] ON [People] ([Name]) WHERE [Name] IS NOT NULL;
""");
    }

    [ConditionalFact]
    public virtual async Task CreateIndex_generates_exec_when_filter_and_idempotent()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("Name");
                }),
            builder => { },
            builder => builder.Entity("People").HasIndex("Name").HasFilter("[Name] IS NOT NULL"),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var index = Assert.Single(table.Indexes);
                Assert.Same(table.Columns.Single(c => c.Name == "Name"), Assert.Single(index.Columns));
                Assert.Contains("Name", index.Filter);
            },
            migrationsSqlGenerationOptions: MigrationsSqlGenerationOptions.Idempotent);

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NULL;
""",
//
"""
EXEC(N'CREATE INDEX [IX_People_Name] ON [People] ([Name]) WHERE [Name] IS NOT NULL');
""");
    }

    public override async Task Create_unique_index_with_filter()
    {
        await base.Create_unique_index_with_filter();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NULL;
""",
//
"""
CREATE UNIQUE INDEX [IX_People_Name] ON [People] ([Name]) WHERE [Name] IS NOT NULL AND [Name] <> '';
""");
    }

    [ConditionalFact]
    public virtual async Task Create_index_with_include()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("FirstName");
                    e.Property<string>("LastName");
                    e.Property<string>("Name");
                }),
            builder => { },
            builder => builder.Entity("People").HasIndex("Name")
                .IncludeProperties("FirstName", "LastName"),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var index = Assert.Single(table.Indexes);
                Assert.Equal(1, index.Columns.Count);
                Assert.Contains(table.Columns.Single(c => c.Name == "Name"), index.Columns);
                var includedColumns = (IReadOnlyList<string>?)index[JetAnnotationNames.Include];
                Assert.Null(includedColumns);
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NULL;
""",
//
"""
CREATE INDEX [IX_People_Name] ON [People] ([Name]) INCLUDE ([FirstName], [LastName]);
""");
    }

    [ConditionalFact]
    public virtual async Task Create_index_with_include_and_filter()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("FirstName");
                    e.Property<string>("LastName");
                    e.Property<string>("Name");
                }),
            builder => { },
            builder => builder.Entity("People").HasIndex("Name")
                .IncludeProperties("FirstName", "LastName")
                .HasFilter("[Name] IS NOT NULL"),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var index = Assert.Single(table.Indexes);
                Assert.Equal("([Name] IS NOT NULL)", index.Filter);
                Assert.Equal(1, index.Columns.Count);
                Assert.Contains(table.Columns.Single(c => c.Name == "Name"), index.Columns);
                var includedColumns = (IReadOnlyList<string>?)index[JetAnnotationNames.Include];
                Assert.Null(includedColumns);
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NULL;
""",
//
"""
CREATE INDEX [IX_People_Name] ON [People] ([Name]) INCLUDE ([FirstName], [LastName]) WHERE [Name] IS NOT NULL;
""");
    }

    [ConditionalFact]
    public virtual async Task Create_index_unique_with_include()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("FirstName");
                    e.Property<string>("LastName");
                    e.Property<string>("Name").IsRequired();
                }),
            builder => { },
            builder => builder.Entity("People").HasIndex("Name")
                .IsUnique()
                .IncludeProperties("FirstName", "LastName"),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var index = Assert.Single(table.Indexes);
                Assert.True(index.IsUnique);
                Assert.Equal(1, index.Columns.Count);
                Assert.Contains(table.Columns.Single(c => c.Name == "Name"), index.Columns);
                var includedColumns = (IReadOnlyList<string>?)index[JetAnnotationNames.Include];
                Assert.Null(includedColumns);
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NOT NULL;
""",
//
"""
CREATE UNIQUE INDEX [IX_People_Name] ON [People] ([Name]) INCLUDE ([FirstName], [LastName]);
""");
    }

    [ConditionalFact]
    public virtual async Task Create_index_unique_with_include_and_filter()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("FirstName");
                    e.Property<string>("LastName");
                    e.Property<string>("Name").IsRequired();
                }),
            builder => { },
            builder => builder.Entity("People").HasIndex("Name")
                .IsUnique()
                .IncludeProperties("FirstName", "LastName")
                .HasFilter("[Name] IS NOT NULL"),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var index = Assert.Single(table.Indexes);
                Assert.True(index.IsUnique);
                Assert.Equal("([Name] IS NOT NULL)", index.Filter);
                Assert.Equal(1, index.Columns.Count);
                Assert.Contains(table.Columns.Single(c => c.Name == "Name"), index.Columns);
                var includedColumns = (IReadOnlyList<string>?)index[JetAnnotationNames.Include];
                Assert.Null(includedColumns);
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NOT NULL;
""",
//
"""
CREATE UNIQUE INDEX [IX_People_Name] ON [People] ([Name]) INCLUDE ([FirstName], [LastName]) WHERE [Name] IS NOT NULL;
""");
    }

    [ConditionalFact]
    public virtual async Task Create_index_unique_with_include_filter_and_fillfactor()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("FirstName");
                    e.Property<string>("LastName");
                    e.Property<string>("Name").IsRequired();
                }),
            builder => { },
            builder => builder.Entity("People").HasIndex("Name")
                .IsUnique()
                .IncludeProperties("FirstName", "LastName")
                .HasFilter("[Name] IS NOT NULL")
                .HasFillFactor(90),
            model =>
            {
                var table = Assert.Single(model.Tables);
                var index = Assert.Single(table.Indexes);
                Assert.True(index.IsUnique);
                Assert.Equal("([Name] IS NOT NULL)", index.Filter);
                Assert.Equal(1, index.Columns.Count);
                Assert.Contains(table.Columns.Single(c => c.Name == "Name"), index.Columns);
                var includedColumns = (IReadOnlyList<string>?)index[JetAnnotationNames.Include];
                Assert.Null(includedColumns);
                // TODO: Online index not scaffolded?
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [Name] nvarchar(450) NOT NULL;
""",
//
"""
CREATE UNIQUE INDEX [IX_People_Name] ON [People] ([Name]) INCLUDE ([FirstName], [LastName]) WHERE [Name] IS NOT NULL WITH (FILLFACTOR = 90);
""");
    }

    public override async Task Drop_index()
    {
        await base.Drop_index();

        AssertSql(
"""
DROP INDEX [IX_People_SomeField] ON [People];
""");
    }

    public override async Task Rename_index()
    {
        await base.Rename_index();

        AssertSql(
"""
EXEC sp_rename N'[People].[Foo]', N'foo', N'INDEX';
""");
    }

    public override async Task Add_primary_key_int()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.Add_primary_key_int());

        Assert.Equal(JetStrings.AlterIdentityColumn, exception.Message);
    }

    public override async Task Add_primary_key_string()
    {
        await base.Add_primary_key_string();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeField');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [SomeField] nvarchar(450) NOT NULL;
""",
//
"""
ALTER TABLE [People] ADD CONSTRAINT [PK_People] PRIMARY KEY ([SomeField]);
""");
    }

    public override async Task Add_primary_key_with_name()
    {
        await base.Add_primary_key_with_name();

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeField');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
UPDATE [People] SET [SomeField] = N'' WHERE [SomeField] IS NULL;
ALTER TABLE [People] ALTER COLUMN [SomeField] nvarchar(450) NOT NULL;
ALTER TABLE [People] ADD DEFAULT N'' FOR [SomeField];
""",
//
"""
ALTER TABLE [People] ADD CONSTRAINT [PK_Foo] PRIMARY KEY ([SomeField]);
""");
    }

    public override async Task Add_primary_key_composite_with_name()
    {
        await base.Add_primary_key_composite_with_name();

        AssertSql(
"""
ALTER TABLE [People] ADD CONSTRAINT [PK_Foo] PRIMARY KEY ([SomeField1], [SomeField2]);
""");
    }

    public override async Task Drop_primary_key_int()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.Drop_primary_key_int());

        Assert.Equal(JetStrings.AlterIdentityColumn, exception.Message);
    }

    public override async Task Drop_primary_key_string()
    {
        await base.Drop_primary_key_string();

        AssertSql(
"""
ALTER TABLE [People] DROP CONSTRAINT [PK_People];
""",
//
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SomeField');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] ALTER COLUMN [SomeField] nvarchar(max) NOT NULL;
""");
    }

    public override async Task Add_foreign_key()
    {
        await base.Add_foreign_key();

        AssertSql(
"""
CREATE INDEX [IX_Orders_CustomerId] ON [Orders] ([CustomerId]);
""",
//
"""
ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE;
""");
    }

    public override async Task Add_foreign_key_with_name()
    {
        await base.Add_foreign_key_with_name();

        // AssertSql(
        //     @"ALTER TABLE [Orders] DROP CONSTRAINT [FK_Orders_Customers_CustomerId];",
        //     //
        //     @"DROP INDEX [IX_Orders_CustomerId] ON [Orders];");

        AssertSql(
"""
CREATE INDEX [IX_Orders_CustomerId] ON [Orders] ([CustomerId]);
""",
//
"""
ALTER TABLE [Orders] ADD CONSTRAINT [FK_Foo] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE;
""");
    }

    public override async Task Drop_foreign_key()
    {
        await base.Drop_foreign_key();

        AssertSql(
"""
ALTER TABLE [Orders] DROP CONSTRAINT [FK_Orders_Customers_CustomerId];
""",
//
"""
DROP INDEX [IX_Orders_CustomerId] ON [Orders];
""");
    }

    public override async Task Add_unique_constraint()
    {
        await base.Add_unique_constraint();

        AssertSql(
"""
ALTER TABLE [People] ADD CONSTRAINT [AK_People_AlternateKeyColumn] UNIQUE ([AlternateKeyColumn]);
""");
    }

    public override async Task Add_unique_constraint_composite_with_name()
    {
        await base.Add_unique_constraint_composite_with_name();

        AssertSql(
"""
ALTER TABLE [People] ADD CONSTRAINT [AK_Foo] UNIQUE ([AlternateKeyColumn1], [AlternateKeyColumn2]);
""");
    }

    public override async Task Drop_unique_constraint()
    {
        await base.Drop_unique_constraint();

        AssertSql(
"""
ALTER TABLE [People] DROP CONSTRAINT [AK_People_AlternateKeyColumn];
""");
    }

    public override async Task Add_check_constraint_with_name()
    {
        await base.Add_check_constraint_with_name();

        AssertSql(
"""
ALTER TABLE [People] ADD CONSTRAINT [CK_People_Foo] CHECK ([DriverLicense] > 0);
""");
    }

    [ConditionalFact]
    public virtual async Task Add_check_constraint_generates_exec_when_idempotent()
    {
        await Test(
            builder => builder.Entity(
                "People", e =>
                {
                    e.Property<int>("Id");
                    e.Property<int>("DriverLicense");
                }),
            builder => { },
            builder => builder.Entity("People").ToTable(tb => tb.HasCheckConstraint("CK_People_Foo", "[DriverLicense] > 0")),
            model =>
            {
                // TODO: no scaffolding support for check constraints, https://github.com/aspnet/EntityFrameworkCore/issues/15408
            },
            migrationsSqlGenerationOptions: MigrationsSqlGenerationOptions.Idempotent);

        AssertSql(
"""
EXEC(N'ALTER TABLE [People] ADD CONSTRAINT [CK_People_Foo] CHECK ([DriverLicense] > 0)');
""");
    }

    public override async Task Alter_check_constraint()
    {
        await base.Alter_check_constraint();

        AssertSql(
"""
ALTER TABLE [People] DROP CONSTRAINT [CK_People_Foo];
""",
//
"""
ALTER TABLE [People] ADD CONSTRAINT [CK_People_Foo] CHECK ([DriverLicense] > 1);
""");
    }

    public override async Task Drop_check_constraint()
    {
        await base.Drop_check_constraint();

        AssertSql(
"""
ALTER TABLE [People] DROP CONSTRAINT [CK_People_Foo];
""");
    }

    public override async Task Create_sequence()
    {
        await base.Create_sequence();

        AssertSql(
"""
CREATE SEQUENCE [TestSequence] AS int START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
""");
    }

    [ConditionalFact]
    public async Task Create_sequence_byte()
    {
        await Test(
            builder => { },
            builder => builder.HasSequence<byte>("TestSequence"),
            model =>
            {
                var sequence = Assert.Single(model.Sequences);
                Assert.Equal("TestSequence", sequence.Name);
            });
        AssertSql(
"""
CREATE SEQUENCE [TestSequence] AS tinyint START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
""");
    }

    [ConditionalFact]
    public async Task Create_sequence_decimal()
    {
        await Test(
            builder => { },
            builder => builder.HasSequence<decimal>("TestSequence"),
            model =>
            {
                var sequence = Assert.Single(model.Sequences);
                Assert.Equal("TestSequence", sequence.Name);
            });

        AssertSql(
"""
CREATE SEQUENCE [TestSequence] AS decimal START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
""");
    }

    public override async Task Create_sequence_long()
    {
        await base.Create_sequence_long();

        AssertSql(
"""
CREATE SEQUENCE [TestSequence] START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
""");
    }

    public override async Task Create_sequence_short()
    {
        await base.Create_sequence_short();

        AssertSql(
"""
CREATE SEQUENCE [TestSequence] AS smallint START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
""");
    }

    public override async Task Create_sequence_all_settings()
    {
        await base.Create_sequence_all_settings();

        AssertSql(
"""
IF SCHEMA_ID(N'dbo2') IS NULL EXEC(N'CREATE SCHEMA [dbo2];');
""",
//
"""
CREATE SEQUENCE [dbo2].[TestSequence] START WITH 3 INCREMENT BY 2 MINVALUE 2 MAXVALUE 916 CYCLE;
""");
    }

    public override async Task Alter_sequence_all_settings()
    {
        await base.Alter_sequence_all_settings();

        AssertSql(
"""
ALTER SEQUENCE [foo] INCREMENT BY 2 MINVALUE -5 MAXVALUE 10 CYCLE;
""",
//
"""
ALTER SEQUENCE [foo] RESTART WITH -3;
""");
    }

    public override async Task Alter_sequence_increment_by()
    {
        await base.Alter_sequence_increment_by();

        AssertSql(
"""
ALTER SEQUENCE [foo] INCREMENT BY 2 NO MINVALUE NO MAXVALUE NO CYCLE;
""");
    }

    public override async Task Drop_sequence()
    {
        await base.Drop_sequence();

        AssertSql(
"""
DROP SEQUENCE [TestSequence];
""");
    }

    public override async Task Rename_sequence()
    {
        await base.Rename_sequence();

        AssertSql(
"""
EXEC sp_rename N'[TestSequence]', N'testsequence';
""");
    }

    public override async Task Move_sequence()
    {
        await base.Move_sequence();

        AssertSql(
"""
IF SCHEMA_ID(N'TestSequenceSchema') IS NULL EXEC(N'CREATE SCHEMA [TestSequenceSchema];');
""",
//
"""
ALTER SCHEMA [TestSequenceSchema] TRANSFER [TestSequence];
""");
    }

    [ConditionalFact]
    public virtual async Task Move_sequence_into_default_schema()
    {
        await Test(
            builder => builder.HasSequence<int>("TestSequence", "TestSequenceSchema"),
            builder => builder.HasSequence<int>("TestSequence"),
            model =>
            {
                var sequence = Assert.Single(model.Sequences);
                Assert.Equal("dbo", sequence.Schema);
                Assert.Equal("TestSequence", sequence.Name);
            });

        AssertSql(
"""
DECLARE @defaultSchema sysname = SCHEMA_NAME();
EXEC(N'ALTER SCHEMA [' + @defaultSchema + N'] TRANSFER [TestSequenceSchema].[TestSequence];');
""");
    }

    [ConditionalFact]
    public async Task Create_sequence_and_dependent_column()
    {
        await Test(
            builder => builder.Entity("People").Property<int>("Id"),
            builder => { },
            builder =>
            {
                builder.HasSequence<int>("TestSequence");
                builder.Entity("People").Property<int>("SeqProp").HasDefaultValueSql("NEXT VALUE FOR TestSequence");
            },
            model =>
            {
                var sequence = Assert.Single(model.Sequences);
                Assert.Equal("TestSequence", sequence.Name);
            });

        AssertSql(
"""
CREATE SEQUENCE [TestSequence] AS int START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
""",
//
"""
ALTER TABLE [People] ADD [SeqProp] int NOT NULL DEFAULT (NEXT VALUE FOR TestSequence);
""");
    }

    [ConditionalFact]
    public async Task Drop_sequence_and_dependent_column()
    {
        await Test(
            builder => builder.Entity("People").Property<int>("Id"),
            builder =>
            {
                builder.HasSequence<int>("TestSequence");
                builder.Entity("People").Property<int>("SeqProp").HasDefaultValueSql("NEXT VALUE FOR TestSequence");
            },
            builder => { },
            model => Assert.Empty(model.Sequences));

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[People]') AND [c].[name] = N'SeqProp');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [People] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [People] DROP COLUMN [SeqProp];
""",
//
"""
DROP SEQUENCE [TestSequence];
""");
    }

    public override async Task InsertDataOperation()
    {
        await base.InsertDataOperation();

        AssertSql(
"""
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Person]'))
    SET IDENTITY_INSERT [Person] ON;
INSERT INTO [Person] ([Id], [Name])
VALUES (1, N'Daenerys Targaryen'),
(2, N'John Snow'),
(3, N'Arya Stark'),
(4, N'Harry Strickland'),
(5, NULL);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Person]'))
    SET IDENTITY_INSERT [Person] OFF;
""");
    }

    public override async Task DeleteDataOperation_simple_key()
    {
        await base.DeleteDataOperation_simple_key();

        // TODO remove rowcount
        AssertSql(
"""
DELETE FROM [Person]
WHERE [Id] = 2;
SELECT @@ROWCOUNT;
""");
    }

    public override async Task DeleteDataOperation_composite_key()
    {
        await base.DeleteDataOperation_composite_key();

        // TODO remove rowcount
        AssertSql(
"""
DELETE FROM [Person]
WHERE [AnotherId] = 12 AND [Id] = 2;
SELECT @@ROWCOUNT;
""");
    }

    public override async Task UpdateDataOperation_simple_key()
    {
        await base.UpdateDataOperation_simple_key();

        // TODO remove rowcount
        AssertSql(
"""
UPDATE [Person] SET [Name] = N'Another John Snow'
WHERE [Id] = 2;
SELECT @@ROWCOUNT;
""");
    }

    public override async Task UpdateDataOperation_composite_key()
    {
        await base.UpdateDataOperation_composite_key();

        // TODO remove rowcount
        AssertSql(
"""
UPDATE [Person] SET [Name] = N'Another John Snow'
WHERE [AnotherId] = 11 AND [Id] = 2;
SELECT @@ROWCOUNT;
""");
    }

    public override async Task UpdateDataOperation_multiple_columns()
    {
        await base.UpdateDataOperation_multiple_columns();

        // TODO remove rowcount
        AssertSql(
"""
UPDATE [Person] SET [Age] = 21, [Name] = N'Another John Snow'
WHERE [Id] = 2;
SELECT @@ROWCOUNT;
""");
    }

    [ConditionalFact]
    public virtual async Task InsertDataOperation_generates_exec_when_idempotent()
    {
        await Test(
            builder => builder.Entity(
                "Person", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("Name");
                    e.HasKey("Id");
                }),
            builder => { },
            builder => builder.Entity("Person")
                .HasData(
                    new Person { Id = 1, Name = "Daenerys Targaryen" },
                    new Person { Id = 2, Name = "John Snow" },
                    new Person { Id = 3, Name = "Arya Stark" },
                    new Person { Id = 4, Name = "Harry Strickland" },
                    new Person { Id = 5, Name = null }),
            model => { },
            migrationsSqlGenerationOptions: MigrationsSqlGenerationOptions.Idempotent);

        AssertSql(
"""
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Person]'))
    SET IDENTITY_INSERT [Person] ON;
EXEC(N'INSERT INTO [Person] ([Id], [Name])
VALUES (1, N''Daenerys Targaryen''),
(2, N''John Snow''),
(3, N''Arya Stark''),
(4, N''Harry Strickland''),
(5, NULL)');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Person]'))
    SET IDENTITY_INSERT [Person] OFF;
""");
    }

    [ConditionalFact]
    public virtual async Task DeleteDataOperation_generates_exec_when_idempotent()
    {
        await Test(
            builder => builder.Entity(
                "Person", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("Name");
                    e.HasKey("Id");
                    e.HasData(new Person { Id = 1, Name = "Daenerys Targaryen" });
                }),
            builder => builder.Entity("Person").HasData(new Person { Id = 2, Name = "John Snow" }),
            builder => { },
            model => { },
            migrationsSqlGenerationOptions: MigrationsSqlGenerationOptions.Idempotent);

        AssertSql(
"""
EXEC(N'DELETE FROM [Person]
WHERE [Id] = 2;
SELECT @@ROWCOUNT');
""");
    }

    [ConditionalFact]
    public virtual async Task UpdateDataOperation_generates_exec_when_idempotent()
    {
        await Test(
            builder => builder.Entity(
                "Person", e =>
                {
                    e.Property<int>("Id");
                    e.Property<string>("Name");
                    e.HasKey("Id");
                    e.HasData(new Person { Id = 1, Name = "Daenerys Targaryen" });
                }),
            builder => builder.Entity("Person").HasData(new Person { Id = 2, Name = "John Snow" }),
            builder => builder.Entity("Person").HasData(new Person { Id = 2, Name = "Another John Snow" }),
            model => { },
            migrationsSqlGenerationOptions: MigrationsSqlGenerationOptions.Idempotent);

        AssertSql(
"""
EXEC(N'UPDATE [Person] SET [Name] = N''Another John Snow''
WHERE [Id] = 2;
SELECT @@ROWCOUNT');
""");
    }

    [ConditionalFact]
    public virtual async Task Create_table_with_json_column()
    {
        await Test(
            builder => { },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");
                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson();
                            });

                        e.OwnsOne(
                            "Owned", "OwnedRequiredReference", o =>
                            {
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.Navigation("OwnedRequiredReference").IsRequired();
                    });
            },
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal("Entity", table.Name);

                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name),
                    c =>
                    {
                        Assert.Equal("OwnedCollection", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    },
                    c =>
                    {
                        Assert.Equal("OwnedReference", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                        Assert.True(c.IsNullable);
                    },
                    c =>
                    {
                        Assert.Equal("OwnedRequiredReference", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                        Assert.False(c.IsNullable);
                    });
                Assert.Same(
                    table.Columns.Single(c => c.Name == "Id"),
                    Assert.Single(table.PrimaryKey!.Columns));
            });

        AssertSql(
"""
CREATE TABLE [Entity] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NULL,
    [OwnedCollection] nvarchar(max) NULL,
    [OwnedReference] nvarchar(max) NULL,
    [OwnedRequiredReference] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Entity] PRIMARY KEY ([Id])
);
""");
    }

    [ConditionalFact]
    public virtual async Task Create_table_with_json_column_explicit_json_column_names()
    {
        await Test(
            builder => { },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");
                        e.OwnsOne(
                            "Owned", "json_reference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "json_reference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.OwnsMany(
                            "Owned2", "json_collection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson();
                            });
                    });
            },
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal("Entity", table.Name);

                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name),
                    c =>
                    {
                        Assert.Equal("json_collection", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    },
                    c =>
                    {
                        Assert.Equal("json_reference", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    });
                Assert.Same(
                    table.Columns.Single(c => c.Name == "Id"),
                    Assert.Single(table.PrimaryKey!.Columns));
            });

        AssertSql(
"""
CREATE TABLE [Entity] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NULL,
    [json_collection] nvarchar(max) NULL,
    [json_reference] nvarchar(max) NULL,
    CONSTRAINT [PK_Entity] PRIMARY KEY ([Id])
);
""");
    }

    [ConditionalFact]
    public virtual async Task Add_json_columns_to_existing_table()
    {
        await Test(
            builder => builder.Entity(
                "Entity", e =>
                {
                    e.Property<int>("Id").ValueGeneratedOnAdd();
                    e.HasKey("Id");
                    e.Property<string>("Name");
                }),
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.OwnsOne(
                            "Owned", "OwnedRequiredReference", o =>
                            {
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.Navigation("OwnedRequiredReference").IsRequired();

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson();
                            });
                    });
            },
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal("Entity", table.Name);

                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name),
                    c =>
                    {
                        Assert.Equal("OwnedCollection", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    },
                    c =>
                    {
                        Assert.Equal("OwnedReference", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                        Assert.True(c.IsNullable);
                    },
                    c =>
                    {
                        Assert.Equal("OwnedRequiredReference", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                        Assert.False(c.IsNullable);
                    });
                Assert.Same(
                    table.Columns.Single(c => c.Name == "Id"),
                    Assert.Single(table.PrimaryKey!.Columns));
            });

        AssertSql(
"""
ALTER TABLE [Entity] ADD [OwnedCollection] nvarchar(max) NULL;
""",
//
"""
ALTER TABLE [Entity] ADD [OwnedReference] nvarchar(max) NULL;
""",
//
"""
ALTER TABLE [Entity] ADD [OwnedRequiredReference] nvarchar(max) NOT NULL DEFAULT N'';
""");
    }

    [ConditionalFact]
    public virtual async Task Remove_json_columns_from_existing_table()
    {
        await Test(
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");
                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson();
                            });
                    });
            },
            builder => builder.Entity(
                "Entity", e =>
                {
                    e.Property<int>("Id").ValueGeneratedOnAdd();
                    e.HasKey("Id");
                    e.Property<string>("Name");
                }),
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal("Entity", table.Name);

                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name));
                Assert.Same(
                    table.Columns.Single(c => c.Name == "Id"),
                    Assert.Single(table.PrimaryKey!.Columns));
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Entity]') AND [c].[name] = N'OwnedCollection');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Entity] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [Entity] DROP COLUMN [OwnedCollection];
""",
//
"""
DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Entity]') AND [c].[name] = N'OwnedReference');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Entity] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [Entity] DROP COLUMN [OwnedReference];
""");
    }

    [ConditionalFact]
    public virtual async Task Rename_json_column()
    {
        await Test(
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson("json_reference");
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson("json_collection");
                            });
                    });
            },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson("new_json_reference");
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson("new_json_collection");
                            });
                    });
            },
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal("Entity", table.Name);

                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name),
                    c =>
                    {
                        Assert.Equal("new_json_collection", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    },
                    c =>
                    {
                        Assert.Equal("new_json_reference", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    });
                Assert.Same(
                    table.Columns.Single(c => c.Name == "Id"),
                    Assert.Single(table.PrimaryKey!.Columns));
            });

        AssertSql(
"""
EXEC sp_rename N'[Entity].[json_reference]', N'new_json_reference', N'COLUMN';
""",
//
"""
EXEC sp_rename N'[Entity].[json_collection]', N'new_json_collection', N'COLUMN';
""");
    }

    [ConditionalFact]
    public virtual async Task Rename_table_with_json_column()
    {
        await Test(
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");
                        e.ToTable("Entities");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson();
                            });
                    });
            },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");
                        e.ToTable("NewEntities");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson();
                            });
                    });
            },
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal("NewEntities", table.Name);

                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name),
                    c =>
                    {
                        Assert.Equal("OwnedCollection", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    },
                    c =>
                    {
                        Assert.Equal("OwnedReference", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    });
                Assert.Same(
                    table.Columns.Single(c => c.Name == "Id"),
                    Assert.Single(table.PrimaryKey!.Columns));
            });

        AssertSql(
"""
ALTER TABLE [Entities] DROP CONSTRAINT [PK_Entities];
""",
//
"""
EXEC sp_rename N'[Entities]', N'NewEntities';
""",
//
"""
ALTER TABLE [NewEntities] ADD CONSTRAINT [PK_NewEntities] PRIMARY KEY ([Id]);
""");
    }

    [ConditionalFact]
    public virtual async Task Convert_regular_owned_entities_to_json()
    {
        await Test(
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                            });
                    });
            },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson();
                            });
                    });
            },
            model =>
            {
                var table = Assert.Single(model.Tables);
                Assert.Equal("Entity", table.Name);

                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name),
                    c =>
                    {
                        Assert.Equal("OwnedCollection", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    },
                    c =>
                    {
                        Assert.Equal("OwnedReference", c.Name);
                        Assert.Equal("nvarchar(max)", c.StoreType);
                    });
                Assert.Same(
                    table.Columns.Single(c => c.Name == "Id"),
                    Assert.Single(table.PrimaryKey!.Columns));
            });

        AssertSql(
"""
DROP TABLE [Entity_NestedCollection];
""",
//
"""
DROP TABLE [Entity_OwnedCollection_NestedCollection2];
""",
//
"""
DROP TABLE [Entity_OwnedCollection];
""",
//
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Entity]') AND [c].[name] = N'OwnedReference_Date');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Entity] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [Entity] DROP COLUMN [OwnedReference_Date];
""",
//
"""
DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Entity]') AND [c].[name] = N'OwnedReference_NestedReference_Number');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Entity] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [Entity] DROP COLUMN [OwnedReference_NestedReference_Number];
""",
//
"""
ALTER TABLE [Entity] ADD [OwnedCollection] nvarchar(max) NULL;
""",
//
"""
ALTER TABLE [Entity] ADD [OwnedReference] nvarchar(max) NULL;
""");
    }

    [ConditionalFact]
    public virtual async Task Convert_json_entities_to_regular_owned()
    {
        await Test(
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                                o.ToJson();
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson();
                            });
                    });
            },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                            });

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                            });
                    });
            },
            model =>
            {
                Assert.Equal(4, model.Tables.Count());
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Entity]') AND [c].[name] = N'OwnedCollection');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Entity] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [Entity] DROP COLUMN [OwnedCollection];
""",
//
"""
DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Entity]') AND [c].[name] = N'OwnedReference');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Entity] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [Entity] DROP COLUMN [OwnedReference];
""",
//
"""
ALTER TABLE [Entity] ADD [OwnedReference_Date] datetime2 NULL;
""",
//
"""
ALTER TABLE [Entity] ADD [OwnedReference_NestedReference_Number] int NULL;
""",
//
"""
CREATE TABLE [Entity_NestedCollection] (
    [OwnedEntityId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [Number2] int NOT NULL,
    CONSTRAINT [PK_Entity_NestedCollection] PRIMARY KEY ([OwnedEntityId], [Id]),
    CONSTRAINT [FK_Entity_NestedCollection_Entity_OwnedEntityId] FOREIGN KEY ([OwnedEntityId]) REFERENCES [Entity] ([Id]) ON DELETE CASCADE
);
""",
//
"""
CREATE TABLE [Entity_OwnedCollection] (
    [EntityId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [Date2] datetime2 NOT NULL,
    [NestedReference2_Number3] int NULL,
    CONSTRAINT [PK_Entity_OwnedCollection] PRIMARY KEY ([EntityId], [Id]),
    CONSTRAINT [FK_Entity_OwnedCollection_Entity_EntityId] FOREIGN KEY ([EntityId]) REFERENCES [Entity] ([Id]) ON DELETE CASCADE
);
""",
//
"""
CREATE TABLE [Entity_OwnedCollection_NestedCollection2] (
    [Owned2EntityId] int NOT NULL,
    [Owned2Id] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [Number4] int NOT NULL,
    CONSTRAINT [PK_Entity_OwnedCollection_NestedCollection2] PRIMARY KEY ([Owned2EntityId], [Owned2Id], [Id]),
    CONSTRAINT [FK_Entity_OwnedCollection_NestedCollection2_Entity_OwnedCollection_Owned2EntityId_Owned2Id] FOREIGN KEY ([Owned2EntityId], [Owned2Id]) REFERENCES [Entity_OwnedCollection] ([EntityId], [Id]) ON DELETE CASCADE
);
""");
    }

    [ConditionalFact]
    public virtual async Task Convert_string_column_to_a_json_column_containing_reference()
    {
        await Test(
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");
                    });
            },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.ToJson("Name");
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                            });
                    });
            },
            model =>
            {
                var table = model.Tables.Single();
                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name));
            });

        AssertSql();
    }

    [ConditionalFact]
    public virtual async Task Convert_string_column_to_a_json_column_containing_required_reference()
    {
        await Test(
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");
                    });
            },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");

                        e.OwnsOne(
                            "Owned", "OwnedReference", o =>
                            {
                                o.ToJson("Name");
                                o.OwnsOne(
                                    "Nested", "NestedReference", n =>
                                    {
                                        n.Property<int>("Number");
                                    });
                                o.OwnsMany(
                                    "Nested2", "NestedCollection", n =>
                                    {
                                        n.Property<int>("Number2");
                                    });
                                o.Property<DateTime>("Date");
                            });

                        e.Navigation("OwnedReference").IsRequired();
                    });
            },
            model =>
            {
                var table = model.Tables.Single();
                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name));
            });

        AssertSql(
"""
DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Entity]') AND [c].[name] = N'Name');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Entity] DROP CONSTRAINT [' + @var0 + '];');
UPDATE [Entity] SET [Name] = N'' WHERE [Name] IS NULL;
ALTER TABLE [Entity] ALTER COLUMN [Name] nvarchar(max) NOT NULL;
ALTER TABLE [Entity] ADD DEFAULT N'' FOR [Name];
""");
    }

    [ConditionalFact]
    public virtual async Task Convert_string_column_to_a_json_column_containing_collection()
    {
        await Test(
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");
                        e.Property<string>("Name");
                    });
            },
            builder =>
            {
                builder.Entity(
                    "Entity", e =>
                    {
                        e.Property<int>("Id").ValueGeneratedOnAdd();
                        e.HasKey("Id");

                        e.OwnsMany(
                            "Owned2", "OwnedCollection", o =>
                            {
                                o.OwnsOne(
                                    "Nested3", "NestedReference2", n =>
                                    {
                                        n.Property<int>("Number3");
                                    });
                                o.OwnsMany(
                                    "Nested4", "NestedCollection2", n =>
                                    {
                                        n.Property<int>("Number4");
                                    });
                                o.Property<DateTime>("Date2");
                                o.ToJson("Name");
                            });
                    });
            },
            model =>
            {
                var table = model.Tables.Single();
                Assert.Collection(
                    table.Columns,
                    c => Assert.Equal("Id", c.Name),
                    c => Assert.Equal("Name", c.Name));
            });

        AssertSql();
    }

    protected override string NonDefaultCollation
        => _nonDefaultCollation ??= GetDatabaseCollation() == "German_PhoneBook_CI_AS"
            ? "French_CI_AS"
            : "German_PhoneBook_CI_AS";

    private string? _nonDefaultCollation;

    private string? GetDatabaseCollation()
    {
        using var ctx = CreateContext();
        var connection = ctx.Database.GetDbConnection();
        using var command = connection.CreateCommand();

        command.CommandText = $@"
SELECT collation_name
FROM sys.databases
WHERE name = '{connection.Database}';";

        return command.ExecuteScalar() is string collation
            ? collation
            : null;
    }

    public class MigrationsJetFixture : MigrationsFixtureBase
    {
        protected override string StoreName
            => nameof(MigrationsJetTest);

        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;

        public override RelationalTestHelpers TestHelpers
            => JetTestHelpers.Instance;

        protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
            => base.AddServices(serviceCollection)
                .AddScoped<IDatabaseModelFactory, JetDatabaseModelFactory>();
    }
}
