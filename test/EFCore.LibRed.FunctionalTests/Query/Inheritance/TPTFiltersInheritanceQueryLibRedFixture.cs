using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Inheritance;

public class TPTFiltersInheritanceQueryLibRedFixture : TPTInheritanceQueryLibRedFixture
{
    public override bool EnableFilters
        => true;
}
