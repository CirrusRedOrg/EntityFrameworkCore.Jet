using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Types;

namespace EntityFrameworkCore.Jet.FunctionalTests.Types;

public abstract class JetTypeFixture<T> : RelationalTypeFixtureBase<T>
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    public virtual bool UsingJsonType
        => false;
}
