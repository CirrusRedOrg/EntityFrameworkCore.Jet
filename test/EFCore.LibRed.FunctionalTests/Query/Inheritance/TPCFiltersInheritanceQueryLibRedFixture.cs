using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Inheritance;

public class TPCFiltersInheritanceQueryLibRedFixture : TPCInheritanceQueryLibRedFixture
{
    public override bool EnableFilters
        => true;

    public override bool UseGeneratedKeys
        => false;
}
