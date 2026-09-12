using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Inheritance;

public class TPTFiltersInheritanceQueryJetFixture : TPTInheritanceQueryJetFixture
{
    public override bool EnableFilters
        => true;
}
