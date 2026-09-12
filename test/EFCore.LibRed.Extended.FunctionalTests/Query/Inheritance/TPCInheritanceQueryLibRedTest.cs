// ReSharper disable InconsistentNaming

using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query.Inheritance;

public class TPCInheritanceQueryLibRedTest(TPCInheritanceQueryLibRedFixture fixture, ITestOutputHelper testOutputHelper)
    : TPCInheritanceQueryLibRedTestBase<TPCInheritanceQueryLibRedFixture>(fixture, testOutputHelper);
