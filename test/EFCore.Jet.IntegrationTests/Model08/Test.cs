using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model08
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Model08Run()
        {
            Context.Files.Add(new File() { });
            Context.SaveChanges();
        }
    }
}
