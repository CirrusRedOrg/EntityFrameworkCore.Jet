using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model69_PrivateBackingField
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            Context.Infos.Add(new Info() {SByte = 12});
            Context.SaveChanges();
        }
    }
}