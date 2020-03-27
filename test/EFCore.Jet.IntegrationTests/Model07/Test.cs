using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model07
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            {

                Context.As.Add(new EntityA());
                Context.SaveChanges();
            }
        }

    }
}
