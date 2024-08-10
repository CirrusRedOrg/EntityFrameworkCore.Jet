// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EntityFrameworkCore.Jet.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using EntityFrameworkCore.Jet.Diagnostics.Internal;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Metadata.Internal;

#nullable enable
// ReSharper disable InconsistentNaming

namespace EntityFrameworkCore.Jet.FunctionalTests.Scaffolding
{
    public class JetDatabaseModelFactoryTest : IClassFixture<JetDatabaseModelFactoryTest.JetDatabaseModelFixture>
    {
        protected JetDatabaseModelFixture Fixture { get; }

        public JetDatabaseModelFactoryTest(JetDatabaseModelFixture fixture)
        {
            Fixture = fixture;
            Fixture.OperationReporter.Clear();
        }

        #region Model

        [ConditionalFact]
        public void Create_tables()
            => Test(
                @"
CREATE TABLE `Everest` ( id int );

CREATE TABLE `Denali` ( id int );",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    Assert.Collection(
                        dbModel.Tables.OrderBy(t => t.Name),
                        d =>
                        {
                            Assert.Null(d.Schema);
                            Assert.Equal("Denali", d.Name);
                        },
                        e =>
                        {
                            Assert.Null(e.Schema);
                            Assert.Equal("Everest", e.Name);
                        });
                },
                @"
DROP TABLE `Everest`;

DROP TABLE `Denali`;");

        [ConditionalFact]
        public void Default_database_collation_is_not_scaffolded()
            => Test(
                @"",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel => Assert.Null(dbModel.Collation),
                @"");

        #endregion

        #region FilteringSchemaTable

        [ConditionalFact]
        public void Filter_tables()
            => Test(
                @"
CREATE TABLE `K2` ( Id int, A varchar, UNIQUE (A ) );

CREATE TABLE `Kilimanjaro` ( Id int, B varchar, UNIQUE (B), FOREIGN KEY (B) REFERENCES K2 (A) );",
                new[] { "K2" },
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var table = Assert.Single(dbModel.Tables);
                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Equal("K2", table.Name);
                    Assert.Equal(2, table.Columns.Count);
                    Assert.Equal(1, table.UniqueConstraints.Count);
                    Assert.Empty(table.ForeignKeys);
                },
                @"
DROP TABLE `Kilimanjaro`;

DROP TABLE `K2`;");

        [ConditionalFact]
        public void Filter_tables_with_quote_in_name()
            => Test(
                @"
CREATE TABLE `K2'` ( Id int, A varchar, UNIQUE (A ) );

CREATE TABLE `Kilimanjaro` ( Id int, B varchar, UNIQUE (B), FOREIGN KEY (B) REFERENCES `K2'` (A) );",
                new[] { "K2'" },
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var table = Assert.Single(dbModel.Tables);
                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Equal("K2'", table.Name);
                    Assert.Equal(2, table.Columns.Count);
                    Assert.Equal(1, table.UniqueConstraints.Count);
                    Assert.Empty(table.ForeignKeys);
                },
                @"
DROP TABLE `Kilimanjaro`;

DROP TABLE `K2'`;");

        [ConditionalFact]
        public void Filter_tables_with_qualified_name()
            => Test(
                @"
CREATE TABLE `K2` ( `Id` int, `A` varchar, UNIQUE (A ) );

CREATE TABLE `Kilimanjaro` ( `Id` int, `B` varchar, UNIQUE (B) );",
                new[] { "`K2`" },
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var table = Assert.Single(dbModel.Tables);
                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Equal("K2", table.Name);
                    Assert.Equal(2, table.Columns.Count);
                    Assert.Equal(1, table.UniqueConstraints.Count);
                    Assert.Empty(table.ForeignKeys);
                },
                @"
DROP TABLE `Kilimanjaro`;

DROP TABLE `K2`;");

        #endregion

        #region Table

        [ConditionalFact]
        public void Create_columns()
            => Test(
                @"
CREATE TABLE `Blogs` (
    Id int,
    Name varchar(100) NOT NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var table = dbModel.Tables.Single();

                    Assert.Equal(2, table.Columns.Count);
                    Assert.All(
                        table.Columns, c =>
                        {
                            Assert.Null(c.Table.Schema);
                            Assert.Equal("Blogs", c.Table.Name);
                        });

                    Assert.Single(table.Columns.Where(c => c.Name == "Id"));
                    Assert.Single(table.Columns.Where(c => c.Name == "Name"));
                },
                "DROP TABLE `Blogs`");

        [ConditionalFact]
        public void Create_view_columns()
            => Test(
                @"
CREATE VIEW `BlogsView`
 AS
SELECT
 CLng(100) AS Id,
 '' AS Name;",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var table = Assert.IsType<DatabaseView>(dbModel.Tables.Single());

                    Assert.Equal(2, table.Columns.Count);
                    Assert.Null(table.PrimaryKey);
                    Assert.All(
                        table.Columns, c =>
                        {
                            Assert.Null(c.Table.Schema);
                            Assert.Equal("BlogsView", c.Table.Name);
                        });

                    Assert.Single(table.Columns.Where(c => c.Name == "Id"));
                    Assert.Single(table.Columns.Where(c => c.Name == "Name"));
                },
                "DROP VIEW `BlogsView`;");

        [ConditionalFact]
        public void Create_primary_key()
            => Test(
                @"
CREATE TABLE PrimaryKeyTable (
    Id int CONSTRAINT PK__PrimaryKeyTable PRIMARY KEY
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var pk = dbModel.Tables.Single().PrimaryKey;

                    Assert.Null(pk!.Table!.Schema);
                    Assert.Equal("PrimaryKeyTable", pk.Table.Name);
                    Assert.StartsWith("PK__PrimaryK", pk.Name);
                    Assert.Equal(
                        new List<string> { "Id" }, pk.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE PrimaryKeyTable;");

        [ConditionalFact]
        public void Create_unique_constraints()
            => Test(
                @"
CREATE TABLE UniqueConstraint (
	Id int,
    Name int CONSTRAINT UQ__UniqueConstraint UNIQUE,
    IndexProperty int
);

CREATE INDEX IX_INDEX on UniqueConstraint ( IndexProperty );",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var uniqueConstraint = Assert.Single(dbModel.Tables.Single().UniqueConstraints);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(uniqueConstraint.Table.Schema);
                    Assert.Equal("UniqueConstraint", uniqueConstraint.Table.Name);
                    Assert.StartsWith("UQ__UniqueCo", uniqueConstraint.Name);
                    Assert.Equal(
                        new List<string> { "Name" }, uniqueConstraint.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE UniqueConstraint;");

        [ConditionalFact]
        public void Create_indexes()
            => Test(
                @"
CREATE TABLE IndexTable (
	Id int,
	Name int,
    IndexProperty int
);

CREATE INDEX IX_NAME on IndexTable ( Name );
CREATE INDEX IX_INDEX on IndexTable ( IndexProperty );",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var table = dbModel.Tables.Single();

                    Assert.Equal(2, table.Indexes.Count);
                    Assert.All(
                        table.Indexes, c =>
                        {
                            Assert.Null(c.Table!.Schema);
                            Assert.Equal("IndexTable", c.Table.Name);
                        });

                    Assert.Single(table.Indexes.Where(c => c.Name == "IX_NAME"));
                    Assert.Single(table.Indexes.Where(c => c.Name == "IX_INDEX"));
                },
                "DROP TABLE IndexTable;");

        [ConditionalFact]
        public void Create_multiple_indexes_on_same_column()
            => Test(
                @"
CREATE TABLE IndexTable (
	Id int,
	IndexProperty int
);

CREATE INDEX IX_One on IndexTable ( IndexProperty );
CREATE INDEX IX_Two on IndexTable ( IndexProperty );",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var table = dbModel.Tables.Single();

                    Assert.Equal(2, table.Indexes.Count);
                    Assert.All(
                        table.Indexes, c =>
                        {
                            Assert.Null(c.Table!.Schema);
                            Assert.Equal("IndexTable", c.Table.Name);
                        });

                    Assert.Collection(
                        table.Indexes.OrderBy(i => i.Name),
                        index => { Assert.Equal("IX_One", index.Name); },
                        index => { Assert.Equal("IX_Two", index.Name); });
                },
                "DROP TABLE IndexTable;");

        [ConditionalFact]
        public void Create_foreign_keys()
            => Test(
                @"
CREATE TABLE PrincipalTable (
	Id int CONSTRAINT PK__PrincipalTable PRIMARY KEY
);

CREATE TABLE FirstDependent (
	Id int PRIMARY KEY,
	ForeignKeyId int,
    FOREIGN KEY (ForeignKeyId) REFERENCES PrincipalTable(Id) ON DELETE CASCADE
);

CREATE TABLE SecondDependent (
	Id int PRIMARY KEY,
    FOREIGN KEY (Id) REFERENCES PrincipalTable(Id) ON DELETE NO ACTION
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var firstFk = Assert.Single(dbModel.Tables.Single(t => t.Name == "FirstDependent").ForeignKeys);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(firstFk.Table.Schema);
                    Assert.Equal("FirstDependent", firstFk.Table.Name);
                    Assert.Null(firstFk.PrincipalTable.Schema);
                    Assert.Equal("PrincipalTable", firstFk.PrincipalTable.Name);
                    Assert.Equal(
                        new List<string> { "ForeignKeyId" }, firstFk.Columns.Select(ic => ic.Name).ToList());
                    Assert.Equal(
                        new List<string> { "Id" }, firstFk.PrincipalColumns.Select(ic => ic.Name).ToList());
                    Assert.Equal(ReferentialAction.Cascade, firstFk.OnDelete);

                    var secondFk = Assert.Single(dbModel.Tables.Single(t => t.Name == "SecondDependent").ForeignKeys);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(secondFk.Table.Schema);
                    Assert.Equal("SecondDependent", secondFk.Table.Name);
                    Assert.Null(secondFk.PrincipalTable.Schema);
                    Assert.Equal("PrincipalTable", secondFk.PrincipalTable.Name);
                    Assert.Equal(
                        new List<string> { "Id" }, secondFk.Columns.Select(ic => ic.Name).ToList());
                    Assert.Equal(
                        new List<string> { "Id" }, secondFk.PrincipalColumns.Select(ic => ic.Name).ToList());
                    Assert.Equal(ReferentialAction.NoAction, secondFk.OnDelete);
                },
                @"
DROP TABLE SecondDependent;
DROP TABLE FirstDependent;
DROP TABLE PrincipalTable;");

        #endregion

        #region ColumnFacets

        [ConditionalFact]
        public void Column_with_sysname_assigns_underlying_store_type_and_nullability()
            => Test(
                @"
CREATE TABLE TypeAlias (
	Id int,
	typeAliasColumn sysname
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var column = Assert.Single(dbModel.Tables.Single().Columns.Where(c => c.Name == "typeAliasColumn"));

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Equal("varchar(255)", column.StoreType);
                    Assert.True(column.IsNullable);
                },
                @"
DROP TABLE TypeAlias;");

        [ConditionalFact]
        public void Decimal_numeric_types_have_precision_scale()
            => Test(
                @"
CREATE TABLE NumericColumns (
	Id int,
	decimalColumn decimal NOT NULL,
	decimal105Column decimal(10, 5) NOT NULL,
	decimalDefaultColumn decimal(18, 2) NOT NULL,
	numericColumn numeric NOT NULL,
	numeric152Column numeric(15, 2) NOT NULL,
	numericDefaultColumn numeric(18, 2) NOT NULL,
    numericDefaultPrecisionColumn numeric(28, 5) NOT NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("decimal(18,0)", columns.Single(c => c.Name == "decimalColumn").StoreType);
                    Assert.Equal("decimal(10,5)", columns.Single(c => c.Name == "decimal105Column").StoreType);
                    Assert.Equal("decimal(18,2)", columns.Single(c => c.Name == "decimalDefaultColumn").StoreType);
                    Assert.Equal("decimal(18,0)", columns.Single(c => c.Name == "numericColumn").StoreType);
                    Assert.Equal("decimal(15,2)", columns.Single(c => c.Name == "numeric152Column").StoreType);
                    Assert.Equal("decimal(18,2)", columns.Single(c => c.Name == "numericDefaultColumn").StoreType);
                    Assert.Equal("decimal(28,5)",
                        columns.Single(c => c.Name == "numericDefaultPrecisionColumn").StoreType);
                },
                "DROP TABLE NumericColumns;");

        [ConditionalFact]
        public void Max_length_of_negative_one_translate_to_max_in_store_type()
            => Test(
                @"
CREATE TABLE MaxColumns (
	Id int,
	varcharMaxColumn varchar(255) NULL,
	nvarcharMaxColumn nvarchar(255) NULL,
	varbinaryMaxColumn varbinary(max) NULL,
	binaryVaryingMaxColumn binary varying(max) NULL,
	charVaryingMaxColumn char varying(max) NULL,
	characterVaryingMaxColumn character varying(max) NULL,
	nationalCharVaryingMaxColumn national char varying(max) NULL,
	nationalCharacterVaryingMaxColumn national char varying(max) NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("varchar(255)", columns.Single(c => c.Name == "varcharMaxColumn").StoreType);
                    Assert.Equal("nvarchar(255)", columns.Single(c => c.Name == "nvarcharMaxColumn").StoreType);
                    Assert.Equal("varbinary(max)", columns.Single(c => c.Name == "varbinaryMaxColumn").StoreType);
                    Assert.Equal("varbinary(max)", columns.Single(c => c.Name == "binaryVaryingMaxColumn").StoreType);
                    Assert.Equal("varchar(255)", columns.Single(c => c.Name == "charVaryingMaxColumn").StoreType);
                    Assert.Equal("nvarchar(255)",
                        columns.Single(c => c.Name == "nationalCharVaryingMaxColumn").StoreType);
                    Assert.Equal("nvarchar(255)",
                        columns.Single(c => c.Name == "nationalCharacterVaryingMaxColumn").StoreType);
                },
                "DROP TABLE MaxColumns;");

        [ConditionalFact]
        public void Specific_max_length_are_add_to_store_type()
            => Test(
                @"
CREATE TABLE LengthColumns (
	Id int,
	char10Column char(10) NULL,
	varchar66Column varchar(66) NULL,
	nchar99Column nchar(99) NULL,
    varchar100Column varchar(100) NULL,
	binary111Column binary(111) NULL,
	varbinary123Column varbinary(123) NULL,
	binaryVarying133Column binary varying(133) NULL,
	charVarying144Column char varying(144) NULL,
	character155Column character(155) NULL,
	characterVarying166Column character varying(166) NULL,
	nationalCharacter171Column national character(171) NULL,
	nationalCharVarying177Column national char varying(177) NULL,
    nationalCharacterVarying188Column national char varying(188) NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("char(10)", columns.Single(c => c.Name == "char10Column").StoreType);
                    Assert.Equal("varchar(66)", columns.Single(c => c.Name == "varchar66Column").StoreType);
                    Assert.Equal("char(99)", columns.Single(c => c.Name == "nchar99Column").StoreType);
                    Assert.Equal("varchar(100)", columns.Single(c => c.Name == "varchar100Column").StoreType);
                    Assert.Equal("varbinary(111)", columns.Single(c => c.Name == "binary111Column").StoreType);
                    Assert.Equal("varbinary(123)", columns.Single(c => c.Name == "varbinary123Column").StoreType);
                    Assert.Equal("varbinary(133)", columns.Single(c => c.Name == "binaryVarying133Column").StoreType);
                    Assert.Equal("varchar(144)", columns.Single(c => c.Name == "charVarying144Column").StoreType);
                    Assert.Equal("char(155)", columns.Single(c => c.Name == "character155Column").StoreType);
                    Assert.Equal("varchar(166)", columns.Single(c => c.Name == "characterVarying166Column").StoreType);
                    Assert.Equal("char(171)", columns.Single(c => c.Name == "nationalCharacter171Column").StoreType);
                    Assert.Equal("varchar(177)",
                        columns.Single(c => c.Name == "nationalCharVarying177Column").StoreType);
                    Assert.Equal("varchar(188)",
                        columns.Single(c => c.Name == "nationalCharacterVarying188Column").StoreType);
                },
                "DROP TABLE LengthColumns;");

        [ConditionalFact]
        public void Default_max_length_are_added_to_binary_varbinary()
            => Test(
                @"
CREATE TABLE DefaultRequiredLengthBinaryColumns (
	Id int,
	binaryColumn binary(8000),
	binaryVaryingColumn binary varying(8000),
	varbinaryColumn varbinary(8000)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("binary(8000)", columns.Single(c => c.Name == "binaryColumn").StoreType);
                    Assert.Equal("varbinary(8000)", columns.Single(c => c.Name == "binaryVaryingColumn").StoreType);
                    Assert.Equal("varbinary(8000)", columns.Single(c => c.Name == "varbinaryColumn").StoreType);
                },
                "DROP TABLE DefaultRequiredLengthBinaryColumns;");

        [ConditionalFact]
        public void Default_max_length_are_added_to_char_1()
            => Test(
                @"
CREATE TABLE DefaultRequiredLengthCharColumns (
	Id int,
    charColumn char(255)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("char(255)", columns.Single(c => c.Name == "charColumn").StoreType);
                },
                "DROP TABLE DefaultRequiredLengthCharColumns;");

        [ConditionalFact]
        public void Default_max_length_are_added_to_char_2()
            => Test(
                @"
CREATE TABLE DefaultRequiredLengthCharColumns (
	Id int,
    characterColumn character(255)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("char(255)", columns.Single(c => c.Name == "characterColumn").StoreType);
                },
                "DROP TABLE DefaultRequiredLengthCharColumns;");

        [ConditionalFact]
        public void Default_max_length_are_added_to_varchar()
            => Test(
                @"
CREATE TABLE DefaultRequiredLengthVarcharColumns (
	Id int,
    charVaryingColumn char varying(255),
    characterVaryingColumn character varying(255),
    varcharColumn varchar(255)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("varchar(255)", columns.Single(c => c.Name == "charVaryingColumn").StoreType);
                    Assert.Equal("varchar(255)", columns.Single(c => c.Name == "characterVaryingColumn").StoreType);
                    Assert.Equal("varchar(255)", columns.Single(c => c.Name == "varcharColumn").StoreType);
                },
                "DROP TABLE DefaultRequiredLengthVarcharColumns;");

        [ConditionalFact]
        public void Default_max_length_are_added_to_nchar_1()
            => Test(
                @"
CREATE TABLE DefaultRequiredLengthNcharColumns (
	Id int,
    nationalCharColumn national char(255)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("char(255)", columns.Single(c => c.Name == "nationalCharColumn").StoreType);
                },
                "DROP TABLE DefaultRequiredLengthNcharColumns;");

        [ConditionalFact]
        public void Default_max_length_are_added_to_nchar_2()
            => Test(
                @"
CREATE TABLE DefaultRequiredLengthNcharColumns (
	Id int,
    nationalCharacterColumn national character(255)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("char(255)", columns.Single(c => c.Name == "nationalCharacterColumn").StoreType);
                },
                "DROP TABLE DefaultRequiredLengthNcharColumns;");

        [ConditionalFact]
        public void Default_max_length_are_added_to_nchar_3()
            => Test(
                @"
CREATE TABLE DefaultRequiredLengthNcharColumns (
	Id int,
    ncharColumn nchar(255)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("char(255)", columns.Single(c => c.Name == "ncharColumn").StoreType);
                },
                "DROP TABLE DefaultRequiredLengthNcharColumns;");

        [ConditionalFact]
        public void Default_max_length_are_added_to_nvarchar()
            => Test(
                @"
CREATE TABLE DefaultRequiredLengthNvarcharColumns (
	Id int,
    nationalCharVaryingColumn national char varying(255),
    nationalCharacterVaryingColumn national character varying(255),
    varcharColumn varchar(255)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("varchar(255)", columns.Single(c => c.Name == "nationalCharVaryingColumn").StoreType);
                    Assert.Equal("varchar(255)",
                        columns.Single(c => c.Name == "nationalCharacterVaryingColumn").StoreType);
                    Assert.Equal("varchar(255)", columns.Single(c => c.Name == "varcharColumn").StoreType);
                },
                "DROP TABLE DefaultRequiredLengthNvarcharColumns;");


        [ConditionalFact]
        public void Types_with_required_length_uses_length_of_one()
            => Test(
                @"
CREATE TABLE OneLengthColumns (
	Id int,
	binaryColumn binary NULL,
	binaryVaryingColumn binary varying NULL,
	characterColumn character NULL,
	characterVaryingColumn character varying NULL,
	charColumn char NULL,
	charVaryingColumn char varying NULL,
	nationalCharColumn national char NULL,
	nationalCharacterColumn national character NULL,
	nationalCharacterVaryingColumn national char varying NULL,
	nationalCharVaryingColumn national char varying NULL,
	ncharColumn nchar NULL,
	varcharColumn varchar NULL,
    varbinaryColumn varbinary NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal("binary(1)", columns.Single(c => c.Name == "binaryColumn").StoreType);
                    Assert.Equal("varbinary(1)", columns.Single(c => c.Name == "binaryVaryingColumn").StoreType);
                    Assert.Equal("char(1)", columns.Single(c => c.Name == "characterColumn").StoreType);
                    Assert.Equal("varchar(1)", columns.Single(c => c.Name == "characterVaryingColumn").StoreType);
                    Assert.Equal("char(1)", columns.Single(c => c.Name == "charColumn").StoreType);
                    Assert.Equal("varchar(1)", columns.Single(c => c.Name == "charVaryingColumn").StoreType);
                    Assert.Equal("nchar(1)", columns.Single(c => c.Name == "nationalCharColumn").StoreType);
                    Assert.Equal("nchar(1)", columns.Single(c => c.Name == "nationalCharacterColumn").StoreType);
                    Assert.Equal("nvarchar(1)",
                        columns.Single(c => c.Name == "nationalCharacterVaryingColumn").StoreType);
                    Assert.Equal("nvarchar(1)", columns.Single(c => c.Name == "nationalCharVaryingColumn").StoreType);
                    Assert.Equal("nchar(1)", columns.Single(c => c.Name == "ncharColumn").StoreType);
                    Assert.Equal("nvarchar(1)", columns.Single(c => c.Name == "nvarcharColumn").StoreType);
                    Assert.Equal("varbinary(1)", columns.Single(c => c.Name == "varbinaryColumn").StoreType);
                    Assert.Equal("varchar(1)", columns.Single(c => c.Name == "varcharColumn").StoreType);
                },
                "DROP TABLE OneLengthColumns;");

        [ConditionalFact]
        public void Store_types_without_any_facets()
            => Test(
                @"
CREATE TABLE NoFacetTypes (
	Id int,
	bigintColumn bigint NOT NULL,
	bitColumn bit NOT NULL,
	dateColumn date NOT NULL,
	datetime2Column datetime2 NULL,
	datetimeColumn datetime NULL,
	datetimeoffsetColumn datetimeoffset NULL,
	floatColumn float NOT NULL,
	geographyColumn geography NULL,
	geometryColumn geometry NULL,
	hierarchyidColumn hierarchyid NULL,
	imageColumn image NULL,
	intColumn int NOT NULL,
	moneyColumn money NOT NULL,
	ntextColumn ntext NULL,
	realColumn real NULL,
	smalldatetimeColumn smalldatetime NULL,
	smallintColumn smallint NOT NULL,
	smallmoneyColumn smallmoney NOT NULL,
	sql_variantColumn sql_variant NULL,
	textColumn text NULL,
	timeColumn time NULL,
	timestampColumn timestamp NULL,
	tinyintColumn tinyint NOT NULL,
	uniqueidentifierColumn uniqueidentifier NULL,
    xmlColumn xml NULL
);

CREATE TABLE RowversionType (
	Id int,
    rowversionColumn varbinary(8) NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single(t => t.Name == "NoFacetTypes").Columns;

                    Assert.Equal("bigint", columns.Single(c => c.Name == "bigintColumn").StoreType);
                    Assert.Equal("bit", columns.Single(c => c.Name == "bitColumn").StoreType);
                    Assert.Equal("date", columns.Single(c => c.Name == "dateColumn").StoreType);
                    Assert.Equal("datetime2", columns.Single(c => c.Name == "datetime2Column").StoreType);
                    Assert.Equal("datetime", columns.Single(c => c.Name == "datetimeColumn").StoreType);
                    Assert.Equal("datetimeoffset", columns.Single(c => c.Name == "datetimeoffsetColumn").StoreType);
                    Assert.Equal("float", columns.Single(c => c.Name == "floatColumn").StoreType);
                    Assert.Equal("geography", columns.Single(c => c.Name == "geographyColumn").StoreType);
                    Assert.Equal("geometry", columns.Single(c => c.Name == "geometryColumn").StoreType);
                    Assert.Equal("hierarchyid", columns.Single(c => c.Name == "hierarchyidColumn").StoreType);
                    Assert.Equal("image", columns.Single(c => c.Name == "imageColumn").StoreType);
                    Assert.Equal("int", columns.Single(c => c.Name == "intColumn").StoreType);
                    Assert.Equal("money", columns.Single(c => c.Name == "moneyColumn").StoreType);
                    Assert.Equal("ntext", columns.Single(c => c.Name == "ntextColumn").StoreType);
                    Assert.Equal("real", columns.Single(c => c.Name == "realColumn").StoreType);
                    Assert.Equal("smalldatetime", columns.Single(c => c.Name == "smalldatetimeColumn").StoreType);
                    Assert.Equal("smallint", columns.Single(c => c.Name == "smallintColumn").StoreType);
                    Assert.Equal("smallmoney", columns.Single(c => c.Name == "smallmoneyColumn").StoreType);
                    Assert.Equal("sql_variant", columns.Single(c => c.Name == "sql_variantColumn").StoreType);
                    Assert.Equal("text", columns.Single(c => c.Name == "textColumn").StoreType);
                    Assert.Equal("time", columns.Single(c => c.Name == "timeColumn").StoreType);
                    Assert.Equal("tinyint", columns.Single(c => c.Name == "tinyintColumn").StoreType);
                    Assert.Equal("uniqueidentifier", columns.Single(c => c.Name == "uniqueidentifierColumn").StoreType);
                    Assert.Equal("xml", columns.Single(c => c.Name == "xmlColumn").StoreType);

                    Assert.Equal(
                        "rowversion",
                        dbModel.Tables.Single(t => t.Name == "RowversionType").Columns
                            .Single(c => c.Name == "rowversionColumn").StoreType);
                },
                @"
DROP TABLE NoFacetTypes;
DROP TABLE RowversionType;");

        [ConditionalFact]
        public void Default_and_computed_values_are_stored()
            => Test(
                @"
CREATE TABLE DefaultComputedValues (
	Id int,
	FixedDefaultValue datetime2 NOT NULL DEFAULT ('October 20, 2015 11am'),
	ComputedValue AS GETDATE(),
	A int NOT NULL,
	B int NOT NULL,
	SumOfAAndB AS A + B,
	SumOfAAndBPersisted AS A + B PERSISTED
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var fixedDefaultValue = columns.Single(c => c.Name == "FixedDefaultValue");
                    Assert.Equal("('October 20, 2015 11am')", fixedDefaultValue.DefaultValueSql);
                    Assert.Null(fixedDefaultValue.ComputedColumnSql);

                    var computedValue = columns.Single(c => c.Name == "ComputedValue");
                    Assert.Null(computedValue.DefaultValueSql);
                    Assert.Equal("(getdate())", computedValue.ComputedColumnSql);

                    var sumOfAAndB = columns.Single(c => c.Name == "SumOfAAndB");
                    Assert.Null(sumOfAAndB.DefaultValueSql);
                    Assert.Equal("([A]+[B])", sumOfAAndB.ComputedColumnSql);
                    Assert.False(sumOfAAndB.IsStored);

                    var sumOfAAndBPersisted = columns.Single(c => c.Name == "SumOfAAndBPersisted");
                    Assert.Null(sumOfAAndBPersisted.DefaultValueSql);
                    Assert.Equal("([A]+[B])", sumOfAAndBPersisted.ComputedColumnSql);
                    Assert.True(sumOfAAndBPersisted.IsStored);
                },
                "DROP TABLE DefaultComputedValues;");

        [ConditionalFact(Skip = "Jet can only understand literal defaults")]
        public void Non_literal_bool_default_values_are_passed_through()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A bit DEFAULT (CHOOSE(1, 0, 1, 2)),
	B bit DEFAULT ((CONVERT([bit],(CHOOSE(1, 0, 1, 2))))),
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("(choose((1),(0),(1),(2)))", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("(CONVERT([bit],choose((1),(0),(1),(2))))", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_int_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A int DEFAULT -1,
	B int DEFAULT 0,
	C int DEFAULT 0,
	D int DEFAULT -2,
	E int DEFAULT  2,
	F int DEFAULT 3 ,
	G int DEFAULT 4
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("-1", column.DefaultValueSql);
                    Assert.Equal(-1, column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("0", column.DefaultValueSql);
                    Assert.Equal(0, column.DefaultValue);

                    column = columns.Single(c => c.Name == "C");
                    Assert.Equal("0", column.DefaultValueSql);
                    Assert.Equal(0, column.DefaultValue);

                    column = columns.Single(c => c.Name == "D");
                    Assert.Equal("-2", column.DefaultValueSql);
                    Assert.Equal(-2, column.DefaultValue);

                    column = columns.Single(c => c.Name == "E");
                    Assert.Equal("2", column.DefaultValueSql);
                    Assert.Equal(2, column.DefaultValue);

                    column = columns.Single(c => c.Name == "F");
                    Assert.Equal("3", column.DefaultValueSql);
                    Assert.Equal(3, column.DefaultValue);

                    column = columns.Single(c => c.Name == "G");
                    Assert.Equal("4", column.DefaultValueSql);
                    Assert.Equal(4, column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_short_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A smallint DEFAULT -1,
	B smallint DEFAULT 0
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("-1", column.DefaultValueSql);
                    Assert.Equal((short)-1, column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("0", column.DefaultValueSql);
                    Assert.Equal((short)0, column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        //Uses decimal as no bigint in Jet.Use bigint if in Access 365. TODO
        [ConditionalFact]
        public void Simple_long_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A decimal(20,0) DEFAULT -1,
	B decimal(20,0) DEFAULT 0
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("-1", column.DefaultValueSql);
                    Assert.Equal((decimal)-1, column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("0", column.DefaultValueSql);
                    Assert.Equal((decimal)0, column.DefaultValue);

                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_byte_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A tinyint DEFAULT 1,
	B tinyint DEFAULT 0
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("1", column.DefaultValueSql);
                    Assert.Equal((byte)1, column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("0", column.DefaultValueSql);
                    Assert.Equal((byte)0, column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact(Skip = "Jet can only understand literal defaults")]
        public void Non_literal_int_default_values_are_passed_through()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A int DEFAULT (CHOOSE(1, 0, 1, 2)),
	B int DEFAULT ((CONVERT([int],(CHOOSE(1, 0, 1, 2))))),
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("(choose((1),(0),(1),(2)))", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("(CONVERT([int],choose((1),(0),(1),(2))))", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_double_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A float DEFAULT -1.1111,
	B float DEFAULT 0.0,
	C float DEFAULT 1.1000000000000001e+000
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("-1.1111", column.DefaultValueSql);
                    Assert.Equal(-1.1111, column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("0.0", column.DefaultValueSql);
                    Assert.Equal((double)0, column.DefaultValue);

                    column = columns.Single(c => c.Name == "C");
                    Assert.Equal("1.1000000000000001e+000", column.DefaultValueSql);
                    Assert.Equal(1.1000000000000001e+000, column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_float_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A real DEFAULT -1.1111,
	B real DEFAULT 0.0,
	C real DEFAULT 1.1000000000000001e+000
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("-1.1111", column.DefaultValueSql);
                    Assert.Equal((float)-1.1111, column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("0.0", column.DefaultValueSql);
                    Assert.Equal((float)0, column.DefaultValue);

                    column = columns.Single(c => c.Name == "C");
                    Assert.Equal("1.1000000000000001e+000", column.DefaultValueSql);
                    Assert.Equal((float)1.1000000000000001e+000, column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_decimal_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A decimal DEFAULT -1.1111,
	B decimal DEFAULT 0.0,
	C decimal DEFAULT 0
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("-1.1111", column.DefaultValueSql);
                    Assert.Equal((decimal)-1.1111, column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("0.0", column.DefaultValueSql);
                    Assert.Equal((decimal)0, column.DefaultValue);

                    column = columns.Single(c => c.Name == "C");
                    Assert.Equal("0", column.DefaultValueSql);
                    Assert.Equal((decimal)0, column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_bool_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A bit DEFAULT 0,
	B bit DEFAULT 1,
	C bit DEFAULT FALSE,
	D bit DEFAULT TRUE
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("0", column.DefaultValueSql);
                    Assert.Equal(false, column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("1", column.DefaultValueSql);
                    Assert.Equal(true, column.DefaultValue);

                    column = columns.Single(c => c.Name == "C");
                    Assert.Equal("FALSE", column.DefaultValueSql);
                    Assert.Equal(false, column.DefaultValue);

                    column = columns.Single(c => c.Name == "D");
                    Assert.Equal("TRUE", column.DefaultValueSql);
                    Assert.Equal(true, column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_DateTime_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A datetime DEFAULT '1973-09-03 12:00:01'
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("'1973-09-03 12:00:01'", column.DefaultValueSql);
                    Assert.Equal(new DateTime(1973, 9, 3, 12, 0, 1, 0, DateTimeKind.Unspecified), column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact(Skip = "Jet can only understand literal defaults")]
        public void Non_literal_or_non_parsable_DateTime_default_values_are_passed_through()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A datetime2 DEFAULT (CONVERT([datetime2],(getdate()))),
	B datetime DEFAULT getdate(),
	C datetime2 DEFAULT ((CONVERT([datetime2],('12-01-16 12:32'))))
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("(CONVERT([datetime2],getdate()))", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("(getdate())", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));

                    column = columns.Single(c => c.Name == "C");
                    Assert.Equal("(CONVERT([datetime2],'12-01-16 12:32'))", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_DateOnly_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A date DEFAULT '1968-10-23'
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("'1968-10-23'", column.DefaultValueSql);
                    Assert.Equal(new DateOnly(1968, 10, 23), column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_TimeOnly_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A time DEFAULT '12:00:01'
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("'12:00:01'", column.DefaultValueSql);
                    Assert.Equal(new TimeOnly(12, 0, 1, 0), column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_DateTimeOffset_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A varchar(50) DEFAULT '1973-09-03T12:00:01.0000000+10:00'
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("'1973-09-03T12:00:01.0000000+10:00'", column.DefaultValueSql);
                    Assert.Equal(
                        new DateTimeOffset(new DateTime(1973, 9, 3, 12, 0, 1, 0, DateTimeKind.Unspecified),
                            new TimeSpan(0, 10, 0, 0, 0)),
                        column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_Guid_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A uniqueidentifier DEFAULT '0E984725-C51C-4BF4-9960-E1C80E27ABA0'
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("'0E984725-C51C-4BF4-9960-E1C80E27ABA0'", column.DefaultValueSql);
                    Assert.Equal(new Guid("0E984725-C51C-4BF4-9960-E1C80E27ABA0"), column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact(Skip = "Jet can only understand literal defaults")]
        public void Non_literal_Guid_default_values_are_passed_through()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A uniqueidentifier DEFAULT (CONVERT([uniqueidentifier],(newid()))),
	B uniqueidentifier DEFAULT NEWSEQUENTIALID(),
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("(CONVERT([uniqueidentifier],newid()))", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("(newsequentialid())", column.DefaultValueSql);
                    Assert.Null(column.FindAnnotation(RelationalAnnotationNames.DefaultValue));
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void Simple_string_literals_are_parsed_for_HasDefaultValue()
            => Test(
                @"
CREATE TABLE MyTable (
	Id int,
	A nvarchar(255) DEFAULT 'Hot',
	B varchar(255) DEFAULT 'Buttered',
	C character(100) DEFAULT '',
	D text DEFAULT '',
	E nvarchar(100) DEFAULT  ' Toast! '
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    var column = columns.Single(c => c.Name == "A");
                    Assert.Equal("'Hot'", column.DefaultValueSql);
                    Assert.Equal("Hot", column.DefaultValue);

                    column = columns.Single(c => c.Name == "B");
                    Assert.Equal("'Buttered'", column.DefaultValueSql);
                    Assert.Equal("Buttered", column.DefaultValue);

                    column = columns.Single(c => c.Name == "C");
                    Assert.Equal("''", column.DefaultValueSql);
                    Assert.Equal("", column.DefaultValue);

                    column = columns.Single(c => c.Name == "D");
                    Assert.Equal("''", column.DefaultValueSql);
                    Assert.Equal("", column.DefaultValue);

                    column = columns.Single(c => c.Name == "E");
                    Assert.Equal("' Toast! '", column.DefaultValueSql);
                    Assert.Equal(" Toast! ", column.DefaultValue);
                },
                "DROP TABLE MyTable;");

        [ConditionalFact]
        public void ValueGenerated_is_set_for_identity_and_computed_column()
            => Test(
                @"
CREATE TABLE ValueGeneratedProperties (
	Id int IDENTITY(1, 1),
	NoValueGenerationColumn nvarchar(255),
	FixedDefaultValue datetime2 NOT NULL DEFAULT ('October 20, 2015 11am'),
	ComputedValue AS GETDATE(),
	rowversionColumn varbinary(8) NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.Equal(ValueGenerated.OnAdd, columns.Single(c => c.Name == "Id").ValueGenerated);
                    Assert.Null(columns.Single(c => c.Name == "NoValueGenerationColumn").ValueGenerated);
                    Assert.Null(columns.Single(c => c.Name == "FixedDefaultValue").ValueGenerated);
                    Assert.Null(columns.Single(c => c.Name == "ComputedValue").ValueGenerated);
                    Assert.Equal(ValueGenerated.OnAddOrUpdate,
                        columns.Single(c => c.Name == "rowversionColumn").ValueGenerated);
                },
                "DROP TABLE ValueGeneratedProperties;");

        [ConditionalFact]
        public void ConcurrencyToken_is_set_for_rowVersion()
            => Test(
                @"
CREATE TABLE RowVersionTable (
	Id int,
	rowversionColumn varbinary(8)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;
                    //TODO: Check
                    /*Assert.True(
                        (bool)columns.Single(c => c.Name == "rowversionColumn")[
                            ScaffoldingAnnotationNames.ConcurrencyToken]!);*/
                },
                "DROP TABLE RowVersionTable;");

        [ConditionalFact]
        public void Column_nullability_is_set()
            => Test(
                @"
CREATE TABLE NullableColumns (
	Id int,
	NullableInt int NULL,
	NonNullString varchar(255) NOT NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var columns = dbModel.Tables.Single().Columns;

                    Assert.True(columns.Single(c => c.Name == "NullableInt").IsNullable);
                    Assert.False(columns.Single(c => c.Name == "NonNullString").IsNullable);
                },
                "DROP TABLE NullableColumns;");

        #endregion

        #region PrimaryKeyFacets

        [ConditionalFact]
        public void Create_composite_primary_key()
            => Test(
                @"
CREATE TABLE CompositePrimaryKeyTable (
	Id1 int,
	Id2 int,
	CONSTRAINT PK__CompositPrimaryKeyTable PRIMARY KEY (Id2, Id1)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var pk = dbModel.Tables.Single().PrimaryKey;

                    Assert.Null(pk!.Table!.Schema);
                    Assert.Equal("CompositePrimaryKeyTable", pk.Table.Name);
                    Assert.StartsWith("PK__Composit", pk.Name);
                    Assert.Equal(
                        new List<string> { "Id2", "Id1" }, pk.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE CompositePrimaryKeyTable;");

        [ConditionalFact]
        public void Set_primary_key_name_from_index()
            => Test(
                @"
CREATE TABLE PrimaryKeyName (
	Id1 int,
	Id2 int,
    CONSTRAINT MyPK PRIMARY KEY ( Id2 )
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var pk = dbModel.Tables.Single().PrimaryKey;

                    Assert.Null(pk!.Table!.Schema);
                    Assert.Equal("PrimaryKeyName", pk.Table.Name);
                    Assert.StartsWith("MyPK", pk.Name);
                    Assert.Equal(
                        new List<string> { "Id2" }, pk.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE PrimaryKeyName;");

        #endregion

        #region UniqueConstraintFacets

        [ConditionalFact]
        public void Create_composite_unique_constraint()
            => Test(
                @"
CREATE TABLE CompositeUniqueConstraintTable (
	Id1 int,
	Id2 int,
	CONSTRAINT UX UNIQUE (Id2, Id1)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var uniqueConstraint = Assert.Single(dbModel.Tables.Single().UniqueConstraints);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(uniqueConstraint.Table.Schema);
                    Assert.Equal("CompositeUniqueConstraintTable", uniqueConstraint.Table.Name);
                    Assert.Equal("UX", uniqueConstraint.Name);
                    Assert.Equal(
                        new List<string> { "Id2", "Id1" }, uniqueConstraint.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE CompositeUniqueConstraintTable;");

        [ConditionalFact]
        public void Set_unique_constraint_name_from_index()
            => Test(
                @"
CREATE TABLE UniqueConstraintName (
	Id1 int,
	Id2 int,
    CONSTRAINT MyUC UNIQUE ( Id2 )
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var uniqueConstraint = Assert.Single(dbModel.Tables.Single().UniqueConstraints);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(uniqueConstraint.Table.Schema);
                    Assert.Equal("UniqueConstraintName", uniqueConstraint.Table.Name);
                    Assert.Equal("MyUC", uniqueConstraint.Name);
                    Assert.Equal(
                        new List<string> { "Id2" }, uniqueConstraint.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE UniqueConstraintName;");

        #endregion

        #region IndexFacets

        [ConditionalFact]
        public void Create_composite_index()
            => Test(
                @"
CREATE TABLE CompositeIndexTable (
	Id1 int,
    Id2 int
);

CREATE INDEX IX_COMPOSITE ON CompositeIndexTable ( Id2, Id1 );",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var index = Assert.Single(dbModel.Tables.Single().Indexes);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(index!.Table!.Schema);
                    Assert.Equal("CompositeIndexTable", index.Table.Name);
                    Assert.Equal("IX_COMPOSITE", index.Name);
                    Assert.Equal(
                        new List<string> { "Id2", "Id1" }, index.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE CompositeIndexTable;");

        [ConditionalFact]
        public void Set_unique_true_for_unique_index()
            => Test(
                @"
CREATE TABLE UniqueIndexTable (
	Id1 int,
    Id2 int
);

CREATE UNIQUE INDEX IX_UNIQUE ON UniqueIndexTable ( Id2 );",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var index = Assert.Single(dbModel.Tables.Single().Indexes);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(index!.Table!.Schema);
                    Assert.Equal("UniqueIndexTable", index.Table.Name);
                    Assert.Equal("IX_UNIQUE", index.Name);
                    Assert.True(index.IsUnique);
                    Assert.Null(index.Filter);
                    Assert.Equal(
                        new List<string> { "Id2" }, index.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE UniqueIndexTable;");

        [ConditionalFact(Skip = "Jet does not support filtered index")]
        public void Set_filter_for_filtered_index()
            => Test(
                @"
CREATE TABLE `FilteredIndexTable` (
	Id1 int,
    Id2 int NULL
);

CREATE UNIQUE INDEX IX_UNIQUE ON `FilteredIndexTable` ( Id2 ) WHERE Id2 > 10;",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var index = Assert.Single(dbModel.Tables.Single().Indexes);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(index!.Table!.Schema);
                    Assert.Equal("FilteredIndexTable", index.Table.Name);
                    Assert.Equal("IX_UNIQUE", index.Name);
                    Assert.Equal("(`Id2`>(10))", index.Filter);
                    Assert.Equal(
                        new List<string> { "Id2" }, index.Columns.Select(ic => ic.Name).ToList());
                },
                "DROP TABLE `FilteredIndexTable`;");

        [ConditionalFact]
        public void Ignore_columnstore_index()
            => Test(
                @"
CREATE TABLE ColumnStoreIndexTable (
	Id1 int,
	Id2 int NULL
);

CREATE NONCLUSTERED COLUMNSTORE INDEX ixColumnStore ON ColumnStoreIndexTable ( Id1, Id2 )",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel => { Assert.Empty(dbModel.Tables.Single().Indexes); },
                "DROP TABLE ColumnStoreIndexTable;");

        [ConditionalFact(Skip = "Jet does not support include for index")]
        public void Set_include_for_index()
            => Test(
                @"
CREATE TABLE IncludeIndexTable (
	Id int,
	IndexProperty int,
	IncludeProperty int
);

CREATE INDEX IX_INCLUDE ON IncludeIndexTable(IndexProperty) INCLUDE (IncludeProperty);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var index = Assert.Single(dbModel.Tables.Single().Indexes);
                    Assert.Equal(new[] { "IndexProperty" }, index.Columns.Select(ic => ic.Name).ToList());
                    Assert.Null(index[JetAnnotationNames.Include]);
                },
                "DROP TABLE IncludeIndexTable;");

        #endregion

        #region ForeignKeyFacets

        [ConditionalFact]
        public void Create_composite_foreign_key()
            => Test(
                @"
CREATE TABLE PrincipalTable (
	Id1 int,
	Id2 int,
	PRIMARY KEY (Id1, Id2)
);

CREATE TABLE DependentTable (
	Id int PRIMARY KEY,
	ForeignKeyId1 int,
	ForeignKeyId2 int,
	FOREIGN KEY (ForeignKeyId1, ForeignKeyId2) REFERENCES PrincipalTable(Id1, Id2) ON DELETE CASCADE
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var fk = Assert.Single(dbModel.Tables.Single(t => t.Name == "DependentTable").ForeignKeys);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(fk.Table.Schema);
                    Assert.Equal("DependentTable", fk.Table.Name);
                    Assert.Null(fk.PrincipalTable.Schema);
                    Assert.Equal("PrincipalTable", fk.PrincipalTable.Name);
                    Assert.Equal(
                        new List<string> { "ForeignKeyId1", "ForeignKeyId2" },
                        fk.Columns.Select(ic => ic.Name).ToList());
                    Assert.Equal(
                        new List<string> { "Id1", "Id2" }, fk.PrincipalColumns.Select(ic => ic.Name).ToList());
                    Assert.Equal(ReferentialAction.Cascade, fk.OnDelete);
                },
                @"
DROP TABLE DependentTable;
DROP TABLE PrincipalTable;");

        [ConditionalFact]
        public void Create_multiple_foreign_key_in_same_table()
            => Test(
                @"
CREATE TABLE PrincipalTable (
	Id int PRIMARY KEY
);

CREATE TABLE AnotherPrincipalTable (
	Id int PRIMARY KEY
);

CREATE TABLE DependentTable (
	Id int PRIMARY KEY,
	ForeignKeyId1 int,
	ForeignKeyId2 int,
	FOREIGN KEY (ForeignKeyId1) REFERENCES PrincipalTable(Id) ON DELETE CASCADE,
	FOREIGN KEY (ForeignKeyId2) REFERENCES AnotherPrincipalTable(Id) ON DELETE CASCADE
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var foreignKeys = dbModel.Tables.Single(t => t.Name == "DependentTable").ForeignKeys;

                    Assert.Equal(2, foreignKeys.Count);

                    var principalFk = Assert.Single(foreignKeys.Where(f => f.PrincipalTable.Name == "PrincipalTable"));

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(principalFk.Table.Schema);
                    Assert.Equal("DependentTable", principalFk.Table.Name);
                    Assert.Null(principalFk.PrincipalTable.Schema);
                    Assert.Equal("PrincipalTable", principalFk.PrincipalTable.Name);
                    Assert.Equal(
                        new List<string> { "ForeignKeyId1" }, principalFk.Columns.Select(ic => ic.Name).ToList());
                    Assert.Equal(
                        new List<string> { "Id" }, principalFk.PrincipalColumns.Select(ic => ic.Name).ToList());
                    Assert.Equal(ReferentialAction.Cascade, principalFk.OnDelete);

                    var anotherPrincipalFk =
                        Assert.Single(foreignKeys.Where(f => f.PrincipalTable.Name == "AnotherPrincipalTable"));

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(anotherPrincipalFk.Table.Schema);
                    Assert.Equal("DependentTable", anotherPrincipalFk.Table.Name);
                    Assert.Null(anotherPrincipalFk.PrincipalTable.Schema);
                    Assert.Equal("AnotherPrincipalTable", anotherPrincipalFk.PrincipalTable.Name);
                    Assert.Equal(
                        new List<string> { "ForeignKeyId2" },
                        anotherPrincipalFk.Columns.Select(ic => ic.Name).ToList());
                    Assert.Equal(
                        new List<string> { "Id" }, anotherPrincipalFk.PrincipalColumns.Select(ic => ic.Name).ToList());
                    Assert.Equal(ReferentialAction.Cascade, anotherPrincipalFk.OnDelete);
                },
                @"
DROP TABLE DependentTable;
DROP TABLE AnotherPrincipalTable;
DROP TABLE PrincipalTable;");

        [ConditionalFact]
        public void Create_foreign_key_referencing_unique_constraint()
            => Test(
                @"
CREATE TABLE PrincipalTable (
	Id1 int,
	Id2 int UNIQUE
);

CREATE TABLE DependentTable (
	Id int PRIMARY KEY,
	ForeignKeyId int,
	FOREIGN KEY (ForeignKeyId) REFERENCES PrincipalTable(Id2) ON DELETE CASCADE
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var fk = Assert.Single(dbModel.Tables.Single(t => t.Name == "DependentTable").ForeignKeys);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(fk.Table.Schema);
                    Assert.Equal("DependentTable", fk.Table.Name);
                    Assert.Null(fk.PrincipalTable.Schema);
                    Assert.Equal("PrincipalTable", fk.PrincipalTable.Name);
                    Assert.Equal(
                        new List<string> { "ForeignKeyId" }, fk.Columns.Select(ic => ic.Name).ToList());
                    Assert.Equal(
                        new List<string> { "Id2" }, fk.PrincipalColumns.Select(ic => ic.Name).ToList());
                    Assert.Equal(ReferentialAction.Cascade, fk.OnDelete);
                },
                @"
DROP TABLE DependentTable;
DROP TABLE PrincipalTable;");

        [ConditionalFact]
        public void Set_name_for_foreign_key()
            => Test(
                @"
CREATE TABLE PrincipalTable (
	Id int CONSTRAINT PK__PrincipalTable PRIMARY KEY
);

CREATE TABLE DependentTable (
	Id int CONSTRAINT PK__DependentTable PRIMARY KEY,
	ForeignKeyId int,
	CONSTRAINT MYFK FOREIGN KEY (ForeignKeyId) REFERENCES PrincipalTable(Id) ON DELETE CASCADE
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var fk = Assert.Single(dbModel.Tables.Single(t => t.Name == "DependentTable").ForeignKeys);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(fk.Table.Schema);
                    Assert.Equal("DependentTable", fk.Table.Name);
                    Assert.Null(fk.PrincipalTable.Schema);
                    Assert.Equal("PrincipalTable", fk.PrincipalTable.Name);
                    Assert.Equal(
                        new List<string> { "ForeignKeyId" }, fk.Columns.Select(ic => ic.Name).ToList());
                    Assert.Equal(
                        new List<string> { "Id" }, fk.PrincipalColumns.Select(ic => ic.Name).ToList());
                    Assert.Equal(ReferentialAction.Cascade, fk.OnDelete);
                    Assert.Equal("MYFK", fk.Name);
                },
                @"
DROP TABLE DependentTable;
DROP TABLE PrincipalTable;");

        [ConditionalFact]
        public void Set_referential_action_for_foreign_key()
            => Test(
                @"
CREATE TABLE PrincipalTable (
    Id int PRIMARY KEY
);

CREATE TABLE DependentTable (
    Id int PRIMARY KEY,
    ForeignKeyId int,
	FOREIGN KEY (ForeignKeyId) REFERENCES PrincipalTable(Id) ON DELETE SET NULL
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var fk = Assert.Single(dbModel.Tables.Single(t => t.Name == "DependentTable").ForeignKeys);

                    // ReSharper disable once PossibleNullReferenceException
                    Assert.Null(fk.Table.Schema);
                    Assert.Equal("DependentTable", fk.Table.Name);
                    Assert.Null(fk.PrincipalTable.Schema);
                    Assert.Equal("PrincipalTable", fk.PrincipalTable.Name);
                    Assert.Equal(
                        new List<string> { "ForeignKeyId" }, fk.Columns.Select(ic => ic.Name).ToList());
                    Assert.Equal(
                        new List<string> { "Id" }, fk.PrincipalColumns.Select(ic => ic.Name).ToList());
                    Assert.Equal(ReferentialAction.SetNull, fk.OnDelete);
                },
                @"
DROP TABLE DependentTable;
DROP TABLE PrincipalTable;");

        #endregion

        #region Warnings

        [ConditionalFact]
        public void Warn_missing_schema()
            => Test(
                @"
CREATE TABLE `Blank` (
	`Id` int
);",
                Enumerable.Empty<string>(),
                new[] { "MySchema" },
                dbModel =>
                {
                    //Assert.Empty(dbModel.Tables);

                    var message = Fixture.OperationReporter.Messages.Single(m => m.Level == LogLevel.Warning).Message;

                    Assert.Equal(
                        JetResources.LogMissingSchema(new TestLogger<JetLoggingDefinitions>())
                            .GenerateMessage("MySchema"),
                        message);
                },
                "DROP TABLE `Blank`;");

        [ConditionalFact]
        public void Warn_missing_table()
            => Test(
                @"
CREATE TABLE `Blank` (
	`Id` int
);",
                new[] { "MyTable" },
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    Assert.Empty(dbModel.Tables);

                    var message = Fixture.OperationReporter.Messages.Single(m => m.Level == LogLevel.Warning).Message;

                    Assert.Equal(
                        JetResources.LogMissingTable(new TestLogger<JetLoggingDefinitions>())
                            .GenerateMessage("MyTable"),
                        message);
                },
                "DROP TABLE `Blank`;");

        [ConditionalFact]
        public void Warn_missing_principal_table_for_foreign_key()
            => Test(
                @"
CREATE TABLE PrincipalTable (
	Id int PRIMARY KEY
);

CREATE TABLE DependentTable (
	Id int PRIMARY KEY,
	ForeignKeyId int,
	CONSTRAINT MYFK FOREIGN KEY (ForeignKeyId) REFERENCES PrincipalTable(Id) ON DELETE CASCADE
);",
                new[] { "DependentTable" },
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var message = Fixture.OperationReporter.Messages.Single(m => m.Level == LogLevel.Warning).Message;

                    Assert.Equal(
                        JetResources.LogPrincipalTableNotInSelectionSet(new TestLogger<JetLoggingDefinitions>())
                            .GenerateMessage(
                                "MYFK", "DependentTable", "PrincipalTable"), message);
                },
                @"
DROP TABLE DependentTable;
DROP TABLE PrincipalTable;");

        [ConditionalFact]
        public void Skip_reflexive_foreign_key()
            => Test(
                @"
CREATE TABLE PrincipalTable (
	Id int PRIMARY KEY,
	CONSTRAINT MYFK FOREIGN KEY (Id) REFERENCES PrincipalTable(Id)
);",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var level = Fixture.OperationReporter.Messages
                        .Single(
                            m => m.Message
                                 == JetResources.LogReflexiveConstraintIgnored(new TestLogger<JetLoggingDefinitions>())
                                     .GenerateMessage("MYFK", "dbo.PrincipalTable")).Level;

                    Assert.Equal(LogLevel.Debug, level);

                    var table = Assert.Single(dbModel.Tables);
                    Assert.Empty(table.ForeignKeys);
                },
                @"
DROP TABLE PrincipalTable;");

        /*[ConditionalFact]
        public void Skip_duplicate_foreign_key()
            => Test(
                @"CREATE TABLE PrincipalTable (
    Id int PRIMARY KEY,
    Value1 uniqueidentifier,
    Value2 uniqueidentifier,
    CONSTRAINT [UNIQUE_Value1] UNIQUE ([Value1] ASC),
    CONSTRAINT [UNIQUE_Value2] UNIQUE ([Value2] ASC),
    );
    
    CREATE TABLE OtherPrincipalTable (
    Id int PRIMARY KEY,
    );
    
    CREATE TABLE DependentTable (
    Id int PRIMARY KEY,
    ForeignKeyId int,
    ValueKey uniqueidentifier,
    CONSTRAINT MYFK1 FOREIGN KEY (ForeignKeyId) REFERENCES PrincipalTable(Id),
    CONSTRAINT MYFK2 FOREIGN KEY (ForeignKeyId) REFERENCES PrincipalTable(Id),
    CONSTRAINT MYFK3 FOREIGN KEY (ForeignKeyId) REFERENCES OtherPrincipalTable(Id),
    CONSTRAINT MYFK4 FOREIGN KEY (ValueKey) REFERENCES PrincipalTable(Value1),
    CONSTRAINT MYFK5 FOREIGN KEY (ValueKey) REFERENCES PrincipalTable(Value2),
    );",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var level = Fixture.OperationReporter.Messages
                        .Single(
                            m => m.Message
                                == JetResources.LogDuplicateForeignKeyConstraintIgnored(new TestLogger<JetLoggingDefinitions>())
                                    .GenerateMessage("MYFK2", "dbo.DependentTable", "MYFK1")).Level;
    
                    Assert.Equal(LogLevel.Warning, level);
    
                    var table = dbModel.Tables.Single(t => t.Name == "DependentTable");
                    Assert.Equal(4, table.ForeignKeys.Count);
                },
                @"
    DROP TABLE DependentTable;
    DROP TABLE PrincipalTable;
    DROP TABLE OtherPrincipalTable;");*/

        /*[ConditionalFact]
        public void No_warning_missing_view_definition()
            => Test(
                @"CREATE TABLE TestViewDefinition (
    Id int PRIMARY KEY,
    );",
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                dbModel =>
                {
                    var message = Fixture.OperationReporter.Messages
                        .SingleOrDefault(
                            m => m.Message
                                == JetResources.LogMissingViewDefinitionRights(new TestLogger<JetLoggingDefinitions>())
                                    .GenerateMessage()).Message;
    
                    Assert.Null(message);
                },
                @"
    DROP TABLE TestViewDefinition;");*/

        #endregion

        private void Test(
            string? createSql,
            IEnumerable<string> tables,
            IEnumerable<string> schemas,
            Action<DatabaseModel> asserter,
            string? cleanupSql)
            => Test(
                string.IsNullOrEmpty(createSql) ? Array.Empty<string>() : new[] { createSql },
                tables,
                schemas,
                asserter,
                cleanupSql);

        private void Test(
            string[] createSqls,
            IEnumerable<string> tables,
            IEnumerable<string> schemas,
            Action<DatabaseModel> asserter,
            string? cleanupSql)
        {
            foreach (var createSql in createSqls)
            {
                Fixture.TestStore.ExecuteNonQuery(createSql);
            }

            try
            {
                var databaseModelFactory = JetTestHelpers.Instance.CreateDesignServiceProvider(
                        reporter: Fixture.OperationReporter)
                    .CreateScope().ServiceProvider.GetRequiredService<IDatabaseModelFactory>();

                var databaseModel = databaseModelFactory.Create(
                    Fixture.TestStore.ConnectionString,
                    new DatabaseModelFactoryOptions(tables, schemas));
                Assert.NotNull(databaseModel);
                asserter(databaseModel);
            }
            finally
            {
                if (!string.IsNullOrEmpty(cleanupSql))
                {
                    Fixture.TestStore.ExecuteNonQuery(cleanupSql);
                }
            }
        }

        public class JetDatabaseModelFixture : SharedStoreFixtureBase<PoolableDbContext>
        {
            protected override string StoreName
                => JetTestHelpers.Instance.GetStoreName(nameof(JetDatabaseModelFactoryTest));

            protected override ITestStoreFactory TestStoreFactory
                => JetTestStoreFactory.Instance;

            public new JetTestStore TestStore
                => (JetTestStore)base.TestStore;

            public TestOperationReporter OperationReporter { get; } = new();

            public override async Task InitializeAsync()
            {
                await base.InitializeAsync();
            }

            protected override bool ShouldLogCategory(string logCategory)
                => logCategory == DbLoggerCategory.Scaffolding.Name;
        }
    }
}
