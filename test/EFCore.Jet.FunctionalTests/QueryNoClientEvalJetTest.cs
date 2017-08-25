using Microsoft.EntityFrameworkCore.Query;

namespace EntityFramework.Jet.FunctionalTests
{
    public class QueryNoClientEvalJetTest : QueryNoClientEvalTestBase<QueryNoClientEvalJetFixture>
    {
        public QueryNoClientEvalJetTest(QueryNoClientEvalJetFixture fixture)
            : base(fixture)
        {
        }
    }
}