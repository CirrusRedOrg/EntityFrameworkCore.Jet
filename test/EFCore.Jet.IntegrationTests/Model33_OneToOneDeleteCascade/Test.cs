using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model33_OneToOneDeleteCascade
{
    public abstract class Test:TestBase<TestContext>
    {
        [TestMethod]
        public void Run()
        {
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                Context.Adresses.ToList();

        }
    }
}
