using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model20_HiddenBackingField
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
