// ReSharper disable InconsistentNaming

using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Inheritance;

public class TPCInheritanceQueryJetTest(TPCInheritanceQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
    : TPCInheritanceQueryJetTestBase<TPCInheritanceQueryJetFixture>(fixture, testOutputHelper);
