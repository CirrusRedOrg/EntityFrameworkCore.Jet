using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model09
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
                Context.Ones.Add(new One() { });
                Context.SaveChanges();
        }

    }
}
