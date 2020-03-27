using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model05_WithIndex
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {

            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            Context.Bars.ToList();
        }

    }
}
