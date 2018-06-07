using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

#pragma warning disable 1998

namespace EntityFramework.Jet.FunctionalTests
{
    public class AsyncQueryJetTest : AsyncQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {


        public AsyncQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
            : base(fixture)
        {
        }

    }
}
