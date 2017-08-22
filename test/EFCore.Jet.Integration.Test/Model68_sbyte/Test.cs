using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model68_sbyte
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