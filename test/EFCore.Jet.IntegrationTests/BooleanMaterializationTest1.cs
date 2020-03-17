using System;
using System.Data.Common;
using System.Linq;
using EntityFrameworkCore.Jet.IntegrationTests.Model01;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests
{
    [TestClass]
    public class BooleanMaterializationTest1 : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            // ReSharper disable once RedundantCast
            Console.WriteLine(Context.Students.Select(c => new { MyNewProperty = (bool)true }).ToList().Count);
        }

        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
