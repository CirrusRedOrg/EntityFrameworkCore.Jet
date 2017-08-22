using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model41
{
    public abstract class Test : TestBase<DemoContext>
    {
        [TestMethod]
        public void Model41Run()
        {
            {
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                Context.Applicants.Count();
                //Context.Applicants.AsQueryable().Where("Ciao");

            }
        }
    }
}
