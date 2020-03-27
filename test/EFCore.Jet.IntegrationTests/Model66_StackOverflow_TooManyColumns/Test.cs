using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model66_StackOverflow_TooManyColumns
{
    public abstract class Test : TestBase<Context>
    {

        [TestMethod]
        public void Run()
        {
            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            Context.FasleManJdls.ToList();
        }

    }
}
