using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Inheritance;

public class TPCFiltersInheritanceQueryJetFixture : TPCInheritanceQueryJetFixture
{
    public override bool EnableFilters
        => true;

    public override bool UseGeneratedKeys
        => false;
}
