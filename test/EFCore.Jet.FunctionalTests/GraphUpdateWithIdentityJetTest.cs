using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class GraphUpdatesWithIdentityJetTest : GraphUpdatesJetTestBase<GraphUpdatesWithIdentityJetTest.GraphUpdatesWithIdentityJetFixture>
    {
        public GraphUpdatesWithIdentityJetTest(GraphUpdatesWithIdentityJetFixture fixture)
            : base(fixture)
        {
        }

        public class GraphUpdatesWithIdentityJetFixture : GraphUpdatesJetFixtureBase
        {
            protected override string DatabaseName => "GraphIdentityUpdatesTest";
        }

        public override void Optional_One_to_one_with_AK_relationships_are_one_to_one()
        {
            base.Optional_One_to_one_with_AK_relationships_are_one_to_one();
        }

        public override void Optional_One_to_one_relationships_are_one_to_one()
        {
            base.Optional_One_to_one_relationships_are_one_to_one();
        }
    }
}
