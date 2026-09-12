// ReSharper disable InconsistentNaming

using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Inheritance;

public class TPCInheritanceQueryLibRedTest(TPCInheritanceQueryLibRedFixture fixture, ITestOutputHelper testOutputHelper)
    : TPCInheritanceQueryLibRedTestBase<TPCInheritanceQueryLibRedFixture>(fixture, testOutputHelper);
