using Microsoft.EntityFrameworkCore;

namespace EntityFramework.Jet.FunctionalTests
{
    public class QueryNoClientEvalJetFixture : NorthwindQueryJetFixture
    {
        protected override DbContextOptionsBuilder ConfigureOptions(DbContextOptionsBuilder dbContextOptionsBuilder)
            => dbContextOptionsBuilder.ConfigureWarnings(c => c.Default(WarningBehavior.Throw));
    }
}