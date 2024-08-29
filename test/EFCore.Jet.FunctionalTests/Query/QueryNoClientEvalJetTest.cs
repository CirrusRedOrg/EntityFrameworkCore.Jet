// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class QueryNoClientEvalJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
        : QueryNoClientEvalTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>(fixture);
}
