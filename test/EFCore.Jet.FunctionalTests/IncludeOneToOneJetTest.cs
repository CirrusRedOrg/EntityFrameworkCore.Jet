using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class IncludeOneToOneJetTest : IncludeOneToOneTestBase, IClassFixture<OneToOneQueryJetFixture>
    {


        private readonly OneToOneQueryJetFixture _fixture;

        public IncludeOneToOneJetTest(OneToOneQueryJetFixture fixture)
        {
            _fixture = fixture;
        }

        protected override DbContext CreateContext()
        {
            return _fixture.CreateContext();
        }

        private  string Sql
        {
            get { return _fixture.TestSqlLoggerFactory.SqlStatements.Last(); }
        }
    }
}
