// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class InheritanceQueryJetFixture : InheritanceQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
    }
}
