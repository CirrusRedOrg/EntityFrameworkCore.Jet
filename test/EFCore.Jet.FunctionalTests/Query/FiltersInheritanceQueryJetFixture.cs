// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class FiltersInheritanceQueryJetFixture : InheritanceQueryJetFixture
    {
        protected override bool EnableFilters => true;
    }
}
