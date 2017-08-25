using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.EntityFrameworkCore
{
    public class SqlCeDatabaseModelFactoryTest : IClassFixture<SqlCeDatabaseModelFixture>
    {
        [Fact]
        public void It_reads_tables()
        {
            var sql = new List<string>
            {
                "CREATE TABLE [Everest] ( id int );",
                "CREATE TABLE [Denali] ( id int );"
            };
            var dbInfo = CreateModel(sql, new List<string> { "Everest", "Denali" });

            Assert.Collection(dbInfo.Tables.OrderBy(t => t.Name),
                d =>
                {
                    Assert.Equal(null, d.Schema);
                    Assert.Equal("Denali", d.Name);
                },
                e =>
                {
                    Assert.Equal(null, e.Schema);
                    Assert.Equal("Everest", e.Name);
                });
        }

        [Fact]
        public void It_reads_foreign_keys()
        {
            var sql = new List<string> {
                "CREATE TABLE Ranges ( Id INT IDENTITY (1,1) PRIMARY KEY);",
                "CREATE TABLE Mountains ( RangeId INT NOT NULL, FOREIGN KEY (RangeId) REFERENCES Ranges(Id) ON DELETE CASCADE)"
            };
            var dbInfo = CreateModel(sql, new List<string> { "Ranges", "Mountains" });

            var fk = Assert.Single(dbInfo.Tables.Single(t => t.ForeignKeys.Count > 0).ForeignKeys);

            Assert.Equal(null, fk.Table.Schema);
            Assert.Equal("Mountains", fk.Table.Name);
            Assert.Equal(null, fk.PrincipalTable.Schema);
            Assert.Equal("Ranges", fk.PrincipalTable.Name);
            Assert.Equal("RangeId", fk.Columns.Single().Name);
            Assert.Equal("Id", fk.PrincipalColumns.Single().Name);
            Assert.Equal(ReferentialAction.Cascade, fk.OnDelete);
        }

        [Fact]
        public void It_reads_composite_foreign_keys()
        {
            var sql = new List<string> {
                "CREATE TABLE Ranges1 ( Id INT IDENTITY (1,1), AltId INT, PRIMARY KEY(Id, AltId));",
                "CREATE TABLE Mountains1 ( RangeId INT NOT NULL, RangeAltId INT NOT NULL, FOREIGN KEY (RangeId, RangeAltId) REFERENCES Ranges1(Id, AltId) ON DELETE NO ACTION)"
            };
            var dbInfo = CreateModel(sql, new List<string> { "Ranges1", "Mountains1" });

            var fk = Assert.Single(dbInfo.Tables.Single(t => t.ForeignKeys.Count > 0).ForeignKeys);

            Assert.Equal(null, fk.Table.Schema);
            Assert.Equal("Mountains1", fk.Table.Name);
            Assert.Equal(null, fk.PrincipalTable.Schema);
            Assert.Equal("Ranges1", fk.PrincipalTable.Name);
            Assert.Equal(new[] { "RangeId", "RangeAltId" }, fk.Columns.Select(c => c.Name).ToArray());
            Assert.Equal(new[] { "Id", "AltId" }, fk.PrincipalColumns.Select(c => c.Name).ToArray());
            Assert.Equal(ReferentialAction.NoAction, fk.OnDelete);
        }

        [Fact]
        public void It_reads_primary_keys()
        {
            var sql = new List<string>
            {
                "CREATE TABLE Place1 ( Id int PRIMARY KEY NONCLUSTERED, Name int UNIQUE, Location int);",
                "CREATE NONCLUSTERED INDEX IX_Location_Name ON Place1 (Location, Name);", 
                "CREATE NONCLUSTERED INDEX IX_Location ON Place1 (Location);"
            };
            var dbModel = CreateModel(sql, new List<string> { "Place1" });

            var pkIndex = dbModel.Tables.Single().PrimaryKey;

            Assert.Equal(null, pkIndex.Table.Schema);
            Assert.Equal("Place1", pkIndex.Table.Name);
            Assert.StartsWith("PK__Place1", pkIndex.Name);
            Assert.Equal(new List<string> { "Id" }, pkIndex.Columns.Select(ic => ic.Name).ToList());
        }

        [Fact]
        public void It_reads_unique_constraints()
        {
            var sql = new List<string>
            {
                "CREATE TABLE Place2 ( Id int PRIMARY KEY NONCLUSTERED, Name int UNIQUE, Location int );",
                "CREATE NONCLUSTERED INDEX IX_Location ON Place2 (Location);"
            };
            var dbModel = CreateModel(sql, new List<string> { "Place2" });

            var indexes = dbModel.Tables.Single().UniqueConstraints;

            Assert.All(
                indexes, c =>
                {
                    Assert.Equal(null, c.Table.Schema);
                    Assert.Equal("Place2", c.Table.Name);
                });

            Assert.Collection(
                indexes,
                unique =>
                {
                    Assert.Equal("Name", unique.Columns.Single().Name);
                });
        }

        [Fact]
        public void It_reads_indexes()
        {
            var sql = new List<string>
            {
                "CREATE TABLE Place ( Id int PRIMARY KEY NONCLUSTERED, Name int UNIQUE, Location int );",
                "CREATE NONCLUSTERED INDEX IX_Location ON Place (Location);"
            };
            var dbInfo = CreateModel(sql, new List<string> { "Place" });

            var indexes = dbInfo.Tables.Single().Indexes;

            Assert.All(indexes, c =>
            {
                Assert.Equal(null, c.Table.Schema);
                Assert.Equal("Place", c.Table.Name);
            });

            Assert.Collection(indexes,
                nonClustered =>
                {
                    Assert.Equal("IX_Location", nonClustered.Name);
                    Assert.Equal("Location", nonClustered.Columns.Select(c => c.Name).Single());
                });
        }

        [Fact]
        public void It_reads_columns()
        {
            var sql = @"
CREATE TABLE [MountainsColumns] (
    Id int,
    Name nvarchar(100) NOT NULL,
    Latitude decimal( 5, 2 ) DEFAULT 0.0,
    Created datetime DEFAULT('October 20, 2015 11am'),
    DiscoveredDate datetime,
    Modified rowversion,
    --VarbinaryMax image NOT NULL,
    Primary Key (Name, Id)
);";
            var dbModel = CreateModel(new List<string>{ sql }, new List<string> { "MountainsColumns" });

            var columns = dbModel.Tables.Single().Columns;

            Assert.All(
                columns, c =>
                {
                    Assert.Equal(null, c.Table.Schema);
                    Assert.Equal("MountainsColumns", c.Table.Name);
                });

            Assert.Collection(
                columns,
                id =>
                {
                    Assert.Equal("Id", id.Name);
                    Assert.Equal("int", id.StoreType);
                    Assert.False(id.IsNullable);
                    Assert.Null(id.DefaultValueSql);
                },
                name =>
                {
                    Assert.Equal("Name", name.Name);
                    Assert.Equal("nvarchar(100)", name.StoreType);
                    Assert.False(name.IsNullable);
                    Assert.Null(name.DefaultValueSql);
                },
                lat =>
                {
                    Assert.Equal("Latitude", lat.Name);
                    Assert.Equal("numeric(5, 2)", lat.StoreType);
                    Assert.True(lat.IsNullable);
                    Assert.Equal("0.0", lat.DefaultValueSql);
                },
                created =>
                {
                    Assert.Equal("Created", created.Name);
                    Assert.Equal("datetime", created.StoreType);
                    Assert.True(created.IsNullable);
                    Assert.Equal("('October 20, 2015 11am')", created.DefaultValueSql);
                },
                discovered =>
                {
                    Assert.Equal("DiscoveredDate", discovered.Name);
                    Assert.Equal("datetime", discovered.StoreType);
                    Assert.True(discovered.IsNullable);
                    Assert.Null(discovered.DefaultValueSql);

                },
                modified =>
                {
                    Assert.Equal("Modified", modified.Name);
                    Assert.Equal(ValueGenerated.OnAddOrUpdate, modified.ValueGenerated);
                    Assert.Equal("rowversion", modified.StoreType);
                });
        }

        [Theory]
        [InlineData("nvarchar(55)", 55)]
        [InlineData("nchar(14)", 14)]
        [InlineData("ntext", null)]
        public void It_reads_max_length(string type, int? length)
        {
            var tables = _fixture.Query<string>("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Strings';");
            if (tables.Count() > 0)
            {
                _fixture.ExecuteNonQuery("DROP TABLE [Strings];");
            }

            var sql = new List<string>
            {
                "CREATE TABLE [Strings] ( CharColumn " + type + ");"
            };
            var db = CreateModel(sql, new List<string> { "Strings" });

            Assert.Equal(type, db.Tables.Single().Columns.Single().StoreType);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void It_reads_identity(bool isIdentity)
        {
            var tables = _fixture.Query<string>("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Identities';");
            if (tables.Count() > 0)
            {
                _fixture.ExecuteNonQuery("DROP TABLE [Identities];");
            }

            var sql = new List<string>
            {
                "CREATE TABLE [Identities] ( Id INT " + (isIdentity ? "IDENTITY(1,1)" : "") + ")"
            };

            var dbModel = CreateModel(sql, new List<string> { "Identities" });

            var column = Assert.Single(dbModel.Tables.Single().Columns);
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Equal(isIdentity ? ValueGenerated.OnAdd : default(ValueGenerated?), column.ValueGenerated);
        }

        [Fact]
        public void It_filters_tables()
        {
            var sql = new List<string>
            {
                "CREATE TABLE [K2] ( Id int, A nvarchar, UNIQUE (A) );",
                "CREATE TABLE [Kilimanjaro] ( Id int,B nvarchar, UNIQUE (B ), FOREIGN KEY (B) REFERENCES K2 (A) );"
            };
            var selectionSet = new List<string> { "K2" };

            var dbModel = CreateModel(sql, selectionSet);
            var table = Assert.Single(dbModel.Tables);
            // ReSharper disable once PossibleNullReferenceException
            Assert.Equal("K2", table.Name);
            Assert.Equal(2, table.Columns.Count);
            Assert.Equal(1, table.UniqueConstraints.Count);
            Assert.Empty(table.ForeignKeys);
        }

        private readonly SqlCeDatabaseModelFixture _fixture;

        public DatabaseModel CreateModel(List<string> createSql, IEnumerable<string> tables = null)
            => _fixture.CreateModel(createSql, tables);

        public SqlCeDatabaseModelFactoryTest(SqlCeDatabaseModelFixture fixture)
        {
            _fixture = fixture;
        }
    }
}
