using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model68_sbyte
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            {
                Context.Infos.Add(new Info() {Sbyte = 12});
                Context.SaveChanges();
            }
        }
    }
}