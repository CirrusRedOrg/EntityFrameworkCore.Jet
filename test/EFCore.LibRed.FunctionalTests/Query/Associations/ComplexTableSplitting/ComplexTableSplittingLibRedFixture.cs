using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Associations.ComplexTableSplitting;

public class ComplexTableSplittingLibRedFixture : ComplexTableSplittingRelationalFixtureBase, ITestSqlLoggerFactory
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;
}
