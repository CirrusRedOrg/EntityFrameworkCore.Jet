// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query
{
    public class WarningsLibRedTest(NorthwindQueryLibRedFixture<NoopModelCustomizer> fixture)
        : WarningsTestBase<NorthwindQueryLibRedFixture<NoopModelCustomizer>>(fixture);
}
