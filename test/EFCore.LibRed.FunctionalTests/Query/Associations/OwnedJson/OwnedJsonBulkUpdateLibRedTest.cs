using Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using Xunit.Abstractions;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Associations.OwnedJson;

public class OwnedJsonBulkUpdateLibRedTest(
    OwnedJsonLibRedFixture fixture,
    ITestOutputHelper testOutputHelper)
    : OwnedJsonBulkUpdateRelationalTestBase<OwnedJsonLibRedFixture>(fixture, testOutputHelper);
