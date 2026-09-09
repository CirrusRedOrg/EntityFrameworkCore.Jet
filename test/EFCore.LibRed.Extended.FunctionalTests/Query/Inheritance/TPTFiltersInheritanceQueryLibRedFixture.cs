using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query.Inheritance;

public class TPTFiltersInheritanceQueryLibRedFixture : TPTInheritanceQueryLibRedFixture
{
    public override bool EnableFilters
        => true;
}
