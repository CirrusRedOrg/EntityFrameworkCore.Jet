using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class PropertyEntryJetTest : PropertyEntryTestBase<F1JetFixture>
    {
        public PropertyEntryJetTest(F1JetFixture fixture)
            : base(fixture)
        {
        }


        private string Sql => Fixture.TestSqlLoggerFactory.Sql;
    }
}
