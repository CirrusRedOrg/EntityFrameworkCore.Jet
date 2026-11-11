using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Types;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Types;

public abstract class LibRedTypeFixture<T> : RelationalTypeFixtureBase<T>
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;

    public virtual bool UsingJsonType
        => false;
}
