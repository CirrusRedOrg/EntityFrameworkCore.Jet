using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model47_200
{
    public abstract class Test : TestBase<Context>
    {


        [TestMethod]
        public void Model47_200Run()
        {
            Context.Depts.Count();
        }
    }
}
