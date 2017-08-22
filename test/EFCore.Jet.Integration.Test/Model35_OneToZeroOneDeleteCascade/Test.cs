using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model35_OneToZeroOneDeleteCascade
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Model35_OneToZeroOneDeleteCascadeRun()
        {
            var categoriesList = Context.Dependents.Count();
            Console.WriteLine(categoriesList);
        }
    }
}
