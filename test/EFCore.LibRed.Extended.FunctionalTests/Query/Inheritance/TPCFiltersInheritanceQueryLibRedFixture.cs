using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query.Inheritance;

public class TPCFiltersInheritanceQueryLibRedFixture : TPCInheritanceQueryLibRedFixture
{
    public override bool EnableFilters
        => true;

    public override bool UseGeneratedKeys
        => false;
}
