using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class OwnedQueryJetTest : OwnedQueryTestBase, IClassFixture<OwnedQueryJetFixture>
    {
        private readonly OwnedQueryJetFixture _fixture;

        public OwnedQueryJetTest(OwnedQueryJetFixture fixture)
        {
            _fixture = fixture;
            fixture.TestSqlLoggerFactory.Clear();
        }

        [Fact(Skip = "#8973")]
        public override void Query_for_base_type_loads_all_owned_navs()
        {
            base.Query_for_base_type_loads_all_owned_navs();

            AssertSql("");
        }


        [Fact(Skip = "#8973")]
        public override void Query_for_branch_type_loads_all_owned_navs()
        {
            base.Query_for_branch_type_loads_all_owned_navs();

            AssertSql("");
        }

        [Fact(Skip = "#8973")]
        public override void Query_for_leaf_type_loads_all_owned_navs()
        {
            base.Query_for_leaf_type_loads_all_owned_navs();

            AssertSql(@"SELECT [o].[Id], [o].[Discriminator], [o].[Id], [o].[Id], [o].[LeafAAddress_Country_Name], [o].[Id], [o].[Id], [o].[BranchAddress_Country_Name], [o].[Id], [o].[Id], [o].[PersonAddress_Country_Name]
FROM [OwnedPerson] AS [o]
WHERE [o].[Discriminator] = N'LeafA'");
        }

        [Fact(Skip = "#8973")]
        public override void Query_when_group_by()
        {
            base.Query_when_group_by();
        }

        [Fact(Skip = "#8973")]
        public override void Query_when_subquery()
        {
            base.Query_when_subquery();
        }

        protected override DbContext CreateContext() => _fixture.CreateContext();

        private void AssertSql(params string[] expected)
            => _fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}