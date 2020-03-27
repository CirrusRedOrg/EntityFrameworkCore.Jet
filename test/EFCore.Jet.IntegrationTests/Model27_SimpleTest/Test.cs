using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model27_SimpleTest
{
    public abstract class Test : TestBase<MyDbContext>
    {

        [TestMethod]
        public void Model27_SimpleTestRun()
        {
            Context.Users.Add(new User {Email = "x@b.com", Name = "x"});
            Context.Users.Add(new User {Email = "x@b.com", Name = "x"});
            Context.Vehicles.Add(new Vehicle {BuildYear = 2016, Model = "BMW X4"});
            Context.SaveChanges();
        }
    }
}
