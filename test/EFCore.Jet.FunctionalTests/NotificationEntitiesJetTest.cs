// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class NotificationEntitiesJetTest(NotificationEntitiesJetTest.NotificationEntitiesJetFixture fixture)
        : NotificationEntitiesTestBase<NotificationEntitiesJetTest.NotificationEntitiesJetFixture>(fixture)
    {
        public class NotificationEntitiesJetFixture : NotificationEntitiesFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        }
    }
}
