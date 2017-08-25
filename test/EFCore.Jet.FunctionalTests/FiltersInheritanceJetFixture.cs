namespace EntityFramework.Jet.FunctionalTests
{
    public class FiltersInheritanceJetFixture : InheritanceJetFixture
    {
        protected override bool EnableFilters => true;
        protected override string DatabaseName => "FiltersInheritanceSqlServerTest";
    }
}
