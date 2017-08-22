using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model07
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
