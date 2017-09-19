using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Xunit;

namespace EntityFrameworkCore.Jet.Design.FunctionalTests
{
    public class Issue4Test : IClassFixture<JetDatabaseModelIssue4Fixture>
    {
        [Fact]
        public void It_reads_tables()
        {
            var model = ReadModel();

            Assert.Collection(model.Tables.OrderBy(t => t.Name),
                d =>
                {
                    Assert.Equal("Jet", d.Schema);
                    Assert.Equal("tAnsprechpartnertypen", d.Name);
                },
                e =>
                {
                    Assert.Equal("Jet", e.Schema);
                    Assert.Equal("tBerichte", e.Name);
                });
        }




        [Fact]
        public void It_reads_columns()
        {
            var model = ReadModel();

            var columns = model.Tables.Single(_ => _.Name == "tAnsprechpartnertypen").Columns;

            Assert.All(
                columns, c =>
                {
                    Assert.Equal("Jet", c.Table.Schema);
                    Assert.Equal("tAnsprechpartnertypen", c.Table.Name);
                });

            Assert.Collection(
                columns,
                c1 =>
                {
                    Assert.Equal("Bezeichnung", c1.Name);
                    Assert.Equal("varchar(255)", c1.StoreType);
                    Assert.True(c1.IsNullable);
                    Assert.Null(c1.DefaultValueSql);
                },
                c2 =>
                {
                    Assert.Equal("Code", c2.Name);
                    Assert.Equal("byte", c2.StoreType);
                    Assert.True(c2.IsNullable);
                    Assert.Equal("0", c2.DefaultValueSql);
                });
        }





        private readonly JetDatabaseModelFixture _fixture;

        public DatabaseModel ReadModel(IEnumerable<string> tables = null)
            => _fixture.ReadModel(tables);


        public Issue4Test(JetDatabaseModelIssue4Fixture fixture)
        {
            _fixture = fixture;
        }


    }
}
