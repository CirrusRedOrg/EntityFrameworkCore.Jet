using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model13_TableSplit_1_1rel
{
    public abstract class Test : TestBase<TestContext>
    {

        [TestMethod]
        public void Model13_TableSplit_1_1relRun()
        {
            var visit = new Visit
            {
                Description = "Visit",
                Address = {Description = "AddressDescription"}
            };

            Context.Visits.Add(visit);

            Context.SaveChanges();
        }
    }
}
