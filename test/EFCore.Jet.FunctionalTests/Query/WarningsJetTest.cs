// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class WarningsJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
        : WarningsTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>(fixture);
}
