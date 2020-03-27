// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class DatabindingJetTest : DatabindingTestBase<F1JetFixture>
    {
        public DatabindingJetTest(F1JetFixture fixture)
            : base(fixture)
        {
        }
    }
}
