using System;
using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model15_SortInheritedColumns
{
    public abstract class Test : TestBase<TestContext>
    {
        [TestMethod]
        public void Model15_SortInheritedColumnsRun()
        {
            Context.Brands.Add(new Brand());
            Context.SaveChanges(); // Just to run DB and Table creation statement

            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            Context.Brands.Where(b => b.addDate.ToString().Contains("abc")).ToList();
        }
    }
}
