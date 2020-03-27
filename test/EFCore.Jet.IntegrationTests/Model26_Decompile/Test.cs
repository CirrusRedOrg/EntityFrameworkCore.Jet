using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model26_Decompile
{
    public abstract class Test : TestBase<ParentChildModel>
    {

        [TestMethod]
        public void ParentChildModelRun()
        {
            //var decompiled = 
            //    Context.Parents.Where(p => p.Children.Any(c => c.FullName.StartsWith("A"))).Decompile();
        }
    }
}
