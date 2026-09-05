using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests;

public class ModelBuilding101LibRedTest : ModelBuilding101RelationalTestBase
{
    protected override DbContextOptionsBuilder ConfigureContext(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseLibRed();
}
