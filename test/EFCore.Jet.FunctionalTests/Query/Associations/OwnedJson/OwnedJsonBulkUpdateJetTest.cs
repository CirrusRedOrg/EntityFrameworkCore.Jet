using Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Associations.OwnedJson;

public class OwnedJsonBulkUpdateJetTest(
    OwnedJsonJetFixture fixture,
    ITestOutputHelper testOutputHelper)
    : OwnedJsonBulkUpdateRelationalTestBase<OwnedJsonJetFixture>(fixture, testOutputHelper);
