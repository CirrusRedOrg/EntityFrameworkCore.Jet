using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.BulkUpdates.Inheritance;
using Microsoft.EntityFrameworkCore.Query.Inheritance;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Inheritance;

public class TPTManyToManyQueryJetFixture : TPTManyToManyQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;
}
