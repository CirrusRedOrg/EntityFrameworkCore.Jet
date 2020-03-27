using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model20_HiddenBackingField
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            Context.Companies.Add(new Company());
        }


    }
}
