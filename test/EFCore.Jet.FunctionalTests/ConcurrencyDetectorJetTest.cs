using Microsoft.EntityFrameworkCore;

namespace EntityFramework.Jet.FunctionalTests
{
    public class ConcurrencyDetectorSqlServerTest : ConcurrencyDetectorRelationalTest<NorthwindQueryJetFixture>
    {
        public ConcurrencyDetectorSqlServerTest(NorthwindQueryJetFixture fixture)
            : base(fixture)
        {
        }
    }
}