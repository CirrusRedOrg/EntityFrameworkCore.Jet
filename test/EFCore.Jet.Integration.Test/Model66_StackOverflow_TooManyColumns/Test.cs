using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model66_StackOverflow_TooManyColumns
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
