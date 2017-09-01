using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class QueryNoClientEvalJetTest : QueryNoClientEvalTestBase<QueryNoClientEvalJetFixture>
    {
        public QueryNoClientEvalJetTest(QueryNoClientEvalJetFixture fixture)
            : base(fixture)
        {
        }

        public override void Doesnt_throw_when_from_sql_not_composed()
        {
            {
                using (NorthwindContext context = this.CreateContext())
                    Assert.Equal(91, context.Customers.FromSql("select * from [Customers]").ToList().Count);
            }
        }
    }
}