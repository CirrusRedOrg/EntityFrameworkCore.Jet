using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model14_StackOverflow31992383
{
    public abstract class Test : TestBase<TestContext>
    {

        // With EF 6.1.1 this test case worked

        [TestMethod]
        // Entity validation has been changed
        /*
        [ExpectedException(typeof(DbEntityValidationException))]
        */
        public void Run()
        {
            // MyProperty not specified
            Context.C1s.Add(new Class1());
            Context.SaveChanges(); // Here in EF 6.1.3 is raised an exception because MyProperty is required

            // MyProperty not specified
            Context.C3s.Add(new Class3());
            Context.SaveChanges(); // Here in EF 6.1.3 is raised an exception because MyProperty is required
        }
    }
}
