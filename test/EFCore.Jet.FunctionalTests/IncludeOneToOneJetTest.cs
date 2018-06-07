using EntityFramework.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFramework.Jet.FunctionalTests
{
    public class IncludeOneToOneJetTest : IncludeOneToOneTestBase<IncludeOneToOneJetTest.OneToOneQueryJetFixture>
    {


        private readonly OneToOneQueryJetFixture _fixture;

        public IncludeOneToOneJetTest(OneToOneQueryJetFixture fixture) : base(fixture)
        {
            _fixture = fixture;
        }

        protected override DbContext CreateContext()
        {
            return _fixture.CreateContext();
        }

        public class OneToOneQueryJetFixture : OneToOneQueryFixtureBase
        {
            public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        }

    }
}
